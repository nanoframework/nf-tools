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
        [ObservableProperty]
        private double _bucketValue;

        [ObservableProperty]
        private SolidColorBrush _bucketColor;

    }
}