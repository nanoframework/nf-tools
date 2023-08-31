using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace nanoFramework.Tools.NanoProfiler.ViewModels
{
    public partial class ProfileLauncherViewModel: ObservableObject
    {
        #region Properties
        [ObservableProperty]
        private string _testname = "Test Name";

        [ObservableProperty]
        private SolidColorBrush _backgroundProfileLauncher = Brushes.AliceBlue;

        #endregion


    }
}
