﻿using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace nanoFramework.Tools.NanoProfiler.Models
{
    public partial class BucketDataModel: ObservableObject
    {
        //[ObservableProperty]
        //private double _bucketValue;

        [ObservableProperty]
        private SolidColorBrush _bucketColor;

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
