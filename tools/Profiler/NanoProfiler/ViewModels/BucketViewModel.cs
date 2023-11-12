////
// Copyright (c) .NET Foundation and Contributors.
// See LICENSE file in the project root for full license information.
////

using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace nanoFramework.Tools.NanoProfiler.Models
{
    public partial class BucketViewModel : ObservableObject
    {
        //[ObservableProperty]
        //private double _bucketValue;

        [ObservableProperty]
        private SolidColorBrush? _bucketColor;

        [ObservableProperty]
        private Bucket _fullBucket;

        [ObservableProperty]
        private int _bucketPosition;

    }
    public partial class BucketDataModel1 : ObservableObject
    {

        //[ObservableProperty]
        //private string _pesma;

        [ObservableProperty]
        private double _sectionValue;


    }
}
