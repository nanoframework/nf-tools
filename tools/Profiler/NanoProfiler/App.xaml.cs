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
            ProfileLauncherView mw = new ProfileLauncherView();
            mw.Show();
        }
    }
}
