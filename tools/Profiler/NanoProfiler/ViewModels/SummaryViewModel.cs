////
// Copyright (c) .NET Foundation and Contributors.
// See LICENSE file in the project root for full license information.
////

using CLRProfiler;
using CommunityToolkit.Mvvm.ComponentModel;
using nanoFramework.Tools.NanoProfiler.CLRProfiler;
using System.Collections.Generic;
using System;
using System.Drawing;
using System.IO;
using nanoFramework.Tools.NanoProfiler.Views;
using CommunityToolkit.Mvvm.Input;
using nanoFramework.Tools.NanoProfiler.Services;

namespace nanoFramework.Tools.NanoProfiler.ViewModels;

public partial class SummaryViewModel : ObservableObject
{
    internal FileInfo logFileInfo;
    internal Font font;
  
    internal ReadNewLog log;
    internal ReadLogResult lastLogResult;
    internal static MainForm instance;
    internal string prevlogFileName;
    internal string currlogFileName;
    internal Graph.GraphType graphtype = Graph.GraphType.Invalid;
    internal bool runaswindow = false;
    private string _scenario; 
    public SummaryViewModel(ReadLogResult readLogResultModel)
    {
        lastLogResult = readLogResultModel;
    }

    [RelayCommand]
    private void AllocatedHistogram()
    {
        string title = "Histogram by Size for Allocated Objects for: " + _scenario;
        HistogramViewModel viewModel = new HistogramViewModel(lastLogResult.allocatedHistogram, title);
        HistogramView histogramView = new HistogramView();
        histogramView.DataContext = viewModel;
        histogramView.Show();

    }
}
