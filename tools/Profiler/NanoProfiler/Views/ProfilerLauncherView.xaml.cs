﻿////
// Copyright (c) .NET Foundation and Contributors.
// See LICENSE file in the project root for full license information.
////

using CommunityToolkit.Mvvm.Messaging;
using nanoFramework.Tools.NanoProfiler.ViewModels;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace nanoFramework.Tools.NanoProfiler.Views
{
    /// <summary>
    /// Interaction logic for ProfileLauncherView.xaml
    /// </summary>
    public partial class ProfilerLauncherView : Window
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

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _viewModel = DataContext as ProfilerLauncherViewModel;
            if (_viewModel != null)
            {
                ViewUnLoaded = _viewModel.ViewLoadedCommand;
            }
        }

        private void Window_Unloaded(object sender, RoutedEventArgs e)
        {
            if (ViewUnLoaded != null)
                ViewUnLoaded.Execute(true);
        }
    }
}
