////
// Copyright (c) .NET Foundation and Contributors.
// See LICENSE file in the project root for full license information.
////

using CommunityToolkit.Mvvm.Messaging;
using nanoFramework.Tools.NanoProfiler.ViewModels;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace nanoFramework.Tools.NanoProfiler.Views
{
    /// <summary>
    /// Interaction logic for ProfileLauncherView.xaml
    /// </summary>
    public partial class ProfilerLauncherView : UserControl
    {
        private ICommand ViewUnLoaded { get; set; }
        private ProfilerLauncherViewModel _viewModel;

        // Trace logging (in GUI) related
        private const int _maxLogMessages = 1000;
        private Queue<string> _logMessages = new Queue<string>();
        private bool _logTruncated = false;
        private System.Timers.Timer _logGUIUpdateTimer;
        private Guid _logMessagesFingerprint = new Guid();
        private Guid _lastDisplayedLogMessagesFingerprint = new Guid();

        public ProfilerLauncherView()
        {
            InitializeComponent();

            txtOutputHelp.Text = txtOutputHelp.Text.Replace("@MAX_LINES@", _maxLogMessages.ToString());

            // A timer is used to update the on-screen trace logger every 500msec: otherwise
            // impossible to keep up
            _logGUIUpdateTimer = new System.Timers.Timer(500);
            _logGUIUpdateTimer.AutoReset = true;
            _logGUIUpdateTimer.Elapsed += LogGUIUpdateTimer_Elapsed;
            _logGUIUpdateTimer.Start();

            ClearLog();

            // register message to update log text
            WeakReferenceMessenger.Default.Register<UpdateLogTextMessage>(this, (r, m) =>
            {
                lock(this)
                {
                    if (_logMessages.Count >= _maxLogMessages)
                    {
                        _logTruncated = true;
                        _logMessages.Dequeue();
                    }
                    _logMessages.Enqueue(m.Value);
                    _logMessagesFingerprint = Guid.NewGuid();
                }
            });

            // register message to clear log text
            WeakReferenceMessenger.Default.Register<ClearLogTextMessage>(this, (r, m) =>
            {
                ClearLog();
            });

            this.Loaded += ProfilerLauncherView_Loaded;
        }

        private void ProfilerLauncherView_Closed(object? sender, EventArgs e)
        {
            _logGUIUpdateTimer.Elapsed -= LogGUIUpdateTimer_Elapsed;

            // TODO: shouldn't something like _viewModel.Cancel() be called here?
            // Something to immediately stop executing threads and ensure EXE exits fairly quickly
            // (unlike clicking disconnect button which is very slow)
        }

        private void LogGUIUpdateTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            lock(this)
            {
                if (_lastDisplayedLogMessagesFingerprint != _logMessagesFingerprint)
                {
                    _lastDisplayedLogMessagesFingerprint = _logMessagesFingerprint;
                    string messages = String.Join(Environment.NewLine, _logMessages);
                    if (_logTruncated)
                    {
                        messages = $"[ truncated; only last {_maxLogMessages} messages shown ]" + Environment.NewLine + messages;
                    }

                    Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                    {
                        textLog.Text = messages;
                        textLog.ScrollToEnd();
                    });
                }
            }
        }

        private void ClearLog()
        {
            lock (this)
            {
                _logTruncated = false;
                _logMessages.Clear();
            }
        }

        private void ProfilerLauncherView_Loaded(object sender, RoutedEventArgs e)
        {
            _viewModel = DataContext as ProfilerLauncherViewModel;
            if (_viewModel != null)
            {
                ViewUnLoaded = _viewModel.ViewLoadedCommand;
                this.Loaded-=this.ProfilerLauncherView_Loaded;
            }
        }

        private void ProfilerLauncherView_Unloaded(object sender, RoutedEventArgs e)
        {
          
                ViewUnLoaded?.Execute(true);
            
               
        }
    }
}
