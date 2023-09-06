using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace nanoFramework.Tools.NanoProfiler.Models
{
    public partial class GraphDataModel: ObservableObject
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private double _graphPercentage;

        [ObservableProperty]
        private string _graphBytes = string.Empty;

        [ObservableProperty]
        private SolidColorBrush _graphColor = Brushes.Transparent;

        //[ObservableProperty]
        //private string _title = string.Empty;


    }
}
