using System;
using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using v2rayF.Models;

namespace v2rayF.ViewModels;

public partial class AppNetworkItemViewModel : ObservableObject
{
    public AppNetworkItemViewModel(InstalledAppInfo app, AppNetworkMode mode)
    {
        Id = app.Id;
        DisplayName = app.DisplayName;
        IsSelf = app.IsSelf;
        Mode = mode;
        Icon = TryDecodeIcon(app.IconPng);
    }

    public string Id { get; }

    public string DisplayName { get; }

    public bool IsSelf { get; }

    public Bitmap? Icon { get; }

    [ObservableProperty]
    private AppNetworkMode _mode;

    [ObservableProperty]
    private string _trafficText = "";

    public bool IsVpn
    {
        get => Mode == AppNetworkMode.Vpn;
        set
        {
            if (value)
                Mode = AppNetworkMode.Vpn;
        }
    }

    public bool IsDirect
    {
        get => Mode == AppNetworkMode.Direct;
        set
        {
            if (value)
                Mode = AppNetworkMode.Direct;
        }
    }

    public bool IsBlock
    {
        get => Mode == AppNetworkMode.Block;
        set
        {
            if (value)
                Mode = AppNetworkMode.Block;
        }
    }

    partial void OnModeChanged(AppNetworkMode value)
    {
        OnPropertyChanged(nameof(IsVpn));
        OnPropertyChanged(nameof(IsDirect));
        OnPropertyChanged(nameof(IsBlock));
        OnPropertyChanged(nameof(ModeLabel));
    }

    public string ModeLabel => Mode switch
    {
        AppNetworkMode.Direct => "Direct",
        AppNetworkMode.Block => "Block",
        _ => "VPN"
    };

    private static Bitmap? TryDecodeIcon(byte[]? png)
    {
        if (png is null || png.Length == 0)
            return null;

        try
        {
            using var ms = new MemoryStream(png);
            return new Bitmap(ms);
        }
        catch
        {
            return null;
        }
    }
}
