////
// Copyright (c) .NET Foundation and Contributors.
// See LICENSE file in the project root for full license information.
////

using CommunityToolkit.Mvvm.Messaging;
using nanoFramework.Tools.NanoProfiler.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace nanoFramework.Tools.NanoProfiler.Launcher;

/// <summary>
/// Interaction logic for ProfileLauncherView.xaml
/// </summary>
public partial class ProfilerLauncherView : UserControl
{
    private ICommand ViewUnLoaded { get; set; }
    private ProfilerLauncherViewModel _viewModel;

    public ProfilerLauncherView()
    {
        InitializeComponent();

        // register message to update log text
        WeakReferenceMessenger.Default.Register<UpdateLogTextMessage>(this, (r, m) =>
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                textLog.AppendText(m.Value + "\r\n");
                textLog.ScrollToEnd();
            });
        });

        // register message to clear log text
        WeakReferenceMessenger.Default.Register<ClearLogTextMessage>(this, (r, m) =>
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                textLog.Clear();
            });
        });
    }

 
}
