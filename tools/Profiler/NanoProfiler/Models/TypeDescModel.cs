using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace nanoFramework.Tools.NanoProfiler.Models
{
    public partial class TypeDescModel : ObservableObject
    {
        [ObservableProperty]
        private string _title;

        [ObservableProperty]
        private TypeDesc _typeDesc;

        [ObservableProperty]
        private double _valueSize;

        [ObservableProperty]
        private double _bucketTotalSize;

    }

}
