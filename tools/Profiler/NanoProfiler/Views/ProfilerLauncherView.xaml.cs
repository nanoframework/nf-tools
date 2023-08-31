using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.Debugger.Extensions;
using nanoFramework.Tools.Debugger.WireProtocol;
using nanoFramework.Tools.NanoProfiler.Helpers;
using nanoFramework.Tools.NanoProfiler.ViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using _DBG = nanoFramework.Tools.Debugger;
using _PRF = nanoFramework.Tools.NanoProfiler;
using _WP = nanoFramework.Tools.Debugger.WireProtocol;

namespace nanoFramework.Tools.NanoProfiler.Views
{
    /// <summary>
    /// Interaction logic for ProfileLauncherView.xaml
    /// </summary>
    public partial class ProfilerLauncherView : Window
    {
        private ICommand ViewUnLoaded { get; set; }
        private ProfileLauncherViewModel _viewModel;

        public ProfilerLauncherView()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _viewModel = DataContext as ProfileLauncherViewModel;
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
