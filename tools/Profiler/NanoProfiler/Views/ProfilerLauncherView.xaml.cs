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

        // Trace logging (in GUI) related
        private const int maxLogMessages = 1000;
        private Queue<string> logMessages = new Queue<string>();
        private bool logTruncated = false;
        private System.Timers.Timer logGUIUpdateTimer;
        private Guid logMessagesFingerprint = new Guid();
        private Guid lastDisplayedLogMessagesFingerprint = new Guid();

        public ProfilerLauncherView()
        {
            InitializeComponent();

            txtOutputHelp.Text = txtOutputHelp.Text.Replace("@MAX_LINES@", maxLogMessages.ToString());

            // A timer is used to update the on-screen trace logger every 500msec: otherwise
            // impossible to keep up
            logGUIUpdateTimer = new System.Timers.Timer(500);
            logGUIUpdateTimer.AutoReset = true;
            logGUIUpdateTimer.Elapsed += LogGUIUpdateTimer_Elapsed;
            logGUIUpdateTimer.Start();

            clearLog();

            // register message to update log text
            WeakReferenceMessenger.Default.Register<UpdateLogTextMessage>(this, (r, m) =>
            {
                lock(this)
                {
                    if (logMessages.Count >= maxLogMessages)
                    {
                        logTruncated = true;
                        logMessages.Dequeue();
                    }
                    logMessages.Enqueue(m.Value);
                    logMessagesFingerprint = Guid.NewGuid();
                }
            });

            // register message to clear log text
            WeakReferenceMessenger.Default.Register<ClearLogTextMessage>(this, (r, m) =>
            {
                clearLog();
            });

            Closed += ProfilerLauncherView_Closed;
        }

        private void ProfilerLauncherView_Closed(object? sender, EventArgs e)
        {
            logGUIUpdateTimer.Elapsed -= LogGUIUpdateTimer_Elapsed;

            // TODO: shouldn't something like _viewModel.Cancel() be called here?
            // Something to immediately stop executing threads and ensure EXE exits fairly quickly
            // (unlike clicking disconnect button which is very slow)
        }

        private void LogGUIUpdateTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            lock(this)
            {
                if (lastDisplayedLogMessagesFingerprint != logMessagesFingerprint)
                {
                    lastDisplayedLogMessagesFingerprint = logMessagesFingerprint;
                    string messages = String.Join(Environment.NewLine, logMessages);
                    if (logTruncated)
                    {
                        messages = $"[ truncated; only last {maxLogMessages} messages shown ]" + Environment.NewLine + messages;
                    }

                    Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                    {
                        textLog.Text = messages;
                        textLog.ScrollToEnd();
                    });
                }
            }
        }

        private void clearLog()
        {
            lock (this)
            {
                logTruncated = false;
                logMessages.Clear();
            }
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
