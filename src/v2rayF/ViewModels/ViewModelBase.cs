using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace v2rayF.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    protected static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}
