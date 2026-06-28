using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using v2rayF.ViewModels;

namespace v2rayF;

internal static class DesktopTrayHost
{
    private static TrayIcon? _trayIcon;

    public static void Setup(IClassicDesktopStyleApplicationLifetime desktop, Window mainWindow, MainWindowViewModel viewModel)
    {
        _trayIcon = new TrayIcon
        {
            ToolTipText = "v2rayF — Disconnected",
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://v2rayF/Assets/avalonia-logo.ico"))),
            IsVisible = true
        };

        var menu = new NativeMenu();
        var showItem = new NativeMenuItem("Show");
        showItem.Click += (_, _) =>
        {
            mainWindow.Show();
            mainWindow.Activate();
        };
        menu.Add(showItem);

        var connectItem = new NativeMenuItem("Connect / Disconnect");
        connectItem.Click += async (_, _) => await viewModel.ToggleConnectionCommand.ExecuteAsync(null);
        menu.Add(connectItem);
        menu.Add(new NativeMenuItemSeparator());

        var quitItem = new NativeMenuItem("Quit");
        quitItem.Click += async (_, _) =>
        {
            await viewModel.ShutdownAsync();
            desktop.Shutdown();
        };
        menu.Add(quitItem);

        _trayIcon.Menu = menu;
        _trayIcon.Clicked += (_, _) =>
        {
            mainWindow.Show();
            mainWindow.Activate();
        };

        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainWindowViewModel.TrayToolTip) or nameof(MainWindowViewModel.IsConnected))
                _trayIcon!.ToolTipText = viewModel.TrayToolTip;
        };
    }
}
