using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using v2rayF.Models;
using v2rayF.ViewModels;

namespace v2rayF.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.IsConnected)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private async void ServerList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        if (sender is not ListBox listBox)
            return;

        if (listBox.SelectedItem is not ProxyServer server)
            return;

        await vm.ConnectToServerCommand.ExecuteAsync(server);
    }
}
