using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using v2rayF.Models;
using v2rayF.ViewModels;

namespace v2rayF.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    private void ServerList_Tapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        if (sender is ListBox listBox && listBox.SelectedItem is ProxyServer server)
            vm.SelectedServer = server;
    }

    private async void RemoveServer_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProxyServer server })
            return;

        if (DataContext is not MainWindowViewModel vm)
            return;

        e.Handled = true;
        await vm.RemoveServerCommand.ExecuteAsync(server);
    }
}
