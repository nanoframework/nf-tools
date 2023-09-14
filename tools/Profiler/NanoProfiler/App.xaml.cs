////
// Copyright (c) .NET Foundation and Contributors.
// See LICENSE file in the project root for full license information.
////

using nanoFramework.Tools.NanoProfiler.Views;
using System.Windows;

namespace nanoFramework.Tools.NanoProfiler
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            MainWindow = new ProfilerLauncherView();
            MainWindow.Show();
        }
    }
}
