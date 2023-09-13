////
// Copyright (c) .NET Foundation and Contributors.
// See LICENSE file in the project root for full license information.
////

using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace nanoFramework.Tools.NanoProfiler.Models
{
    public partial class GraphDataModel : ObservableObject
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private double _graphPercentage;

        [ObservableProperty]
        private string _graphBytes = string.Empty;

        [ObservableProperty]
        private SolidColorBrush _graphColor = Brushes.Transparent;
    }
}
