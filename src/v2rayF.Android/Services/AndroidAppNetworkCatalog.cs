using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Net;
using v2rayF.Models;
using v2rayF.Services;
using Application = Android.App.Application;

namespace v2rayF.Android.Services;

/// <summary>
/// Installed-app catalog + panel-gated UID traffic for App Network.
/// </summary>
public sealed class AndroidAppNetworkCatalog
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(8);

    private IReadOnlyList<InstalledAppInfo>? _cache;
    private DateTimeOffset _cacheUtc = DateTimeOffset.MinValue;
    private readonly Dictionary<string, (long Rx, long Tx, DateTimeOffset At)> _prevTraffic = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<InstalledAppInfo>> GetNetworkAppsAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (!forceRefresh &&
            _cache is not null &&
            DateTimeOffset.UtcNow - _cacheUtc < CacheTtl)
        {
            return Task.FromResult(_cache);
        }

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var list = LoadApps(cancellationToken);
            _cache = list;
            _cacheUtc = DateTimeOffset.UtcNow;
            return (IReadOnlyList<InstalledAppInfo>)list;
        }, cancellationToken);
    }

    public Task<IReadOnlyDictionary<string, AppTrafficSnapshot>> GetAppTrafficAsync(
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = DateTimeOffset.UtcNow;
            var result = new Dictionary<string, AppTrafficSnapshot>(StringComparer.OrdinalIgnoreCase);
            if (ids.Count == 0)
                return (IReadOnlyDictionary<string, AppTrafficSnapshot>)result;

            var uidByPackage = BuildUidMap(ids);
            foreach (var id in ids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!uidByPackage.TryGetValue(id, out var uid))
                    continue;

                long rx;
                long tx;
                try
                {
                    rx = TrafficStats.GetUidRxBytes(uid);
                    tx = TrafficStats.GetUidTxBytes(uid);
                }
                catch
                {
                    continue;
                }

                if (rx < 0)
                    rx = 0;
                if (tx < 0)
                    tx = 0;

                double downBps = 0;
                double upBps = 0;
                if (_prevTraffic.TryGetValue(id, out var prev))
                {
                    var dt = (now - prev.At).TotalSeconds;
                    if (dt > 0.2)
                    {
                        downBps = Math.Max(0, (rx - prev.Rx) / dt);
                        upBps = Math.Max(0, (tx - prev.Tx) / dt);
                    }
                }

                _prevTraffic[id] = (rx, tx, now);
                result[id] = new AppTrafficSnapshot
                {
                    Id = id,
                    RxBytes = rx,
                    TxBytes = tx,
                    DownloadBytesPerSec = downBps,
                    UploadBytesPerSec = upBps
                };
            }

            return (IReadOnlyDictionary<string, AppTrafficSnapshot>)result;
        }, cancellationToken);
    }

    private static Dictionary<string, int> BuildUidMap(IReadOnlyList<string> ids)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var pm = Application.Context!.PackageManager!;
        foreach (var id in ids)
        {
            try
            {
                var info = pm.GetApplicationInfo(id, 0);
                if (info is not null)
                    map[id] = info.Uid;
            }
            catch
            {
                // Package may have been uninstalled.
            }
        }

        return map;
    }

    private static List<InstalledAppInfo> LoadApps(CancellationToken cancellationToken)
    {
        var context = Application.Context!;
        var pm = context.PackageManager!;
        var self = context.PackageName ?? AppNetworkPolicy.AndroidSelfPackage;

        IList<ApplicationInfo> apps;
        try
        {
            apps = pm.GetInstalledApplications(PackageInfoFlags.MetaData) ?? [];
        }
        catch
        {
            apps = [];
        }

        var list = new List<InstalledAppInfo>(Math.Min(apps.Count, 512));
        foreach (var app in apps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (app.PackageName is null)
                continue;

            // Prefer user-facing apps (launchable) to keep the list light.
            try
            {
                if (pm.GetLaunchIntentForPackage(app.PackageName) is null &&
                    !string.Equals(app.PackageName, self, StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            catch
            {
                continue;
            }

            string label;
            try
            {
                label = pm.GetApplicationLabel(app)?.ToString() ?? app.PackageName;
            }
            catch
            {
                label = app.PackageName;
            }

            byte[]? icon = null;
            try
            {
                using var drawable = pm.GetApplicationIcon(app.PackageName);
                icon = DrawableToPng(drawable, maxPx: 48);
            }
            catch
            {
                // Icon optional.
            }

            list.Add(new InstalledAppInfo
            {
                Id = app.PackageName,
                DisplayName = label,
                IconPng = icon,
                Uid = app.Uid,
                IsSelf = string.Equals(app.PackageName, self, StringComparison.OrdinalIgnoreCase)
            });
        }

        return list
            .OrderBy(a => a.IsSelf ? 0 : 1)
            .ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static byte[]? DrawableToPng(Drawable? drawable, int maxPx)
    {
        if (drawable is null)
            return null;

        var w = Math.Max(drawable.IntrinsicWidth > 0 ? drawable.IntrinsicWidth : maxPx, 1);
        var h = Math.Max(drawable.IntrinsicHeight > 0 ? drawable.IntrinsicHeight : maxPx, 1);
        var scale = Math.Min(1f, maxPx / (float)Math.Max(w, h));
        var tw = Math.Max(1, (int)(w * scale));
        var th = Math.Max(1, (int)(h * scale));

        using var bitmap = Bitmap.CreateBitmap(tw, th, Bitmap.Config.Argb8888!);
        var canvas = new Canvas(bitmap);
        drawable.SetBounds(0, 0, tw, th);
        drawable.Draw(canvas);

        using var ms = new MemoryStream();
        if (!bitmap.Compress(Bitmap.CompressFormat.Png!, 80, ms))
            return null;

        return ms.ToArray();
    }
}
