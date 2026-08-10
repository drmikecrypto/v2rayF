using System;
using System.Threading;
using System.Threading.Tasks;

namespace v2rayF.Services;

/// <summary>
/// Single shared poller for Xray traffic stats. Computes rates from consecutive totals
/// so UI and Android notification do not each spawn <c>xray api</c> processes.
/// </summary>
public sealed class TrafficStatsHub : IDisposable
{
    public readonly record struct LiveTraffic(
        long UplinkBytes,
        long DownlinkBytes,
        long UplinkBps,
        long DownlinkBps);

    private static readonly Lazy<TrafficStatsHub> LazyShared = new(() => new TrafficStatsHub());
    public static TrafficStatsHub Shared => LazyShared.Value;

    private TrafficStatsService? _stats;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private int _subscribers;
    private long _lastUp;
    private long _lastDown;
    private DateTimeOffset _lastSampleUtc = DateTimeOffset.MinValue;
    private LiveTraffic _latest;
    private int? _connectedPingMs;

    public event Action<LiveTraffic>? Updated;

    /// <summary>Latest proxy-path ping (ms) while connected; null when unknown.</summary>
    public int? ConnectedPingMs
    {
        get => _connectedPingMs;
        set => _connectedPingMs = value;
    }

    public LiveTraffic Latest => _latest;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(2500);

    public TrafficStatsHub(TrafficStatsService? stats = null)
    {
        _stats = stats;
    }

    private TrafficStatsService Stats =>
        _stats ??= new TrafficStatsService(AppServices.CoreEnvironment);

    public void Subscribe()
    {
        lock (_gate)
        {
            _subscribers++;
            if (_subscribers == 1)
                StartUnlocked();
        }
    }

    public void Unsubscribe()
    {
        lock (_gate)
        {
            if (_subscribers <= 0)
                return;

            _subscribers--;
            if (_subscribers == 0)
                StopUnlocked();
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _lastUp = 0;
            _lastDown = 0;
            _lastSampleUtc = DateTimeOffset.MinValue;
            _latest = default;
            _connectedPingMs = null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _subscribers = 0;
            StopUnlocked();
        }
    }

    private void StartUnlocked()
    {
        StopUnlocked();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(() => LoopAsync(token), CancellationToken.None);
    }

    private void StopUnlocked()
    {
        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
        catch
        {
            // Best effort.
        }

        _cts = null;
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var snap = await Stats.QueryAsync(cancellationToken).ConfigureAwait(false);
                var now = DateTimeOffset.UtcNow;
                LiveTraffic live;
                if (snap is { } traffic)
                {
                    long upBps = 0;
                    long downBps = 0;
                    if (_lastSampleUtc != DateTimeOffset.MinValue)
                    {
                        var dt = (now - _lastSampleUtc).TotalSeconds;
                        if (dt > 0.2)
                        {
                            upBps = Math.Max(0, (long)((traffic.UplinkBytes - _lastUp) / dt));
                            downBps = Math.Max(0, (long)((traffic.DownlinkBytes - _lastDown) / dt));
                        }
                    }

                    _lastUp = traffic.UplinkBytes;
                    _lastDown = traffic.DownlinkBytes;
                    _lastSampleUtc = now;
                    live = new LiveTraffic(traffic.UplinkBytes, traffic.DownlinkBytes, upBps, downBps);
                }
                else
                {
                    live = _latest;
                }

                _latest = live;
                Updated?.Invoke(live);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // Keep polling.
            }

            try
            {
                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
