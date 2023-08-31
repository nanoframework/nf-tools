using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace nanoFramework.Tools.NanoProfiler.ViewModels
{
    public partial class ProfileLauncherViewModel: ObservableObject
    {
        #region Properties
        [ObservableProperty]
        private string _testname = "Test Name";

        #endregion


    }
}
