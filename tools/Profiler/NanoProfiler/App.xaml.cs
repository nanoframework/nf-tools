using nanoFramework.Tools.NanoProfiler.Views;
using System.Windows;
using nanoFramework.Tools.NanoProfiler;

namespace nanoFramework.Tools.NanoProfiler
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            //this.InitializeComponent();
            //MainWindow = new ProfilerLauncher();
            MainWindow = new ProfilerLauncherView();
            MainWindow.Show();
        }
    }
}
