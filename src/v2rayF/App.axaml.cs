using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using v2rayF.ViewModels;
using v2rayF.Views;

namespace v2rayF;

public partial class App : Application
{
    private MainWindowViewModel? _viewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _viewModel = new MainWindowViewModel();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow { DataContext = _viewModel };
            desktop.Exit += async (_, _) =>
            {
                if (_viewModel is not null)
                    await _viewModel.ShutdownAsync();
            };

            DesktopTrayHost.Setup(desktop, desktop.MainWindow, _viewModel);
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = new MainView { DataContext = _viewModel };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
