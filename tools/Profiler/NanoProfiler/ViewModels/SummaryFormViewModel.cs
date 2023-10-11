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

namespace nanoFramework.Tools.NanoProfiler.ViewModels;

public partial class SummaryFormViewModel : ObservableObject
{
    internal FileInfo logFileInfo;
    internal Font font;
    private string logFileName;
    private long logFileStartOffset;
    private long logFileEndOffset;
    internal ReadNewLog log;
    internal ReadLogResult lastLogResult;
    internal static MainForm instance;
    internal string prevlogFileName;
    internal string currlogFileName;
    internal Graph.GraphType graphtype = Graph.GraphType.Invalid;
    internal bool runaswindow = false;
    private string _scenario;
    public SummaryFormViewModel(FileInfo logFileInfoInstance)
    {
        logFileInfo = logFileInfoInstance;
        LoadLogFile();

    }

    private void LoadLogFile()
    {
        if (!logFileInfo.Exists)
        {
            return;
        }
        _scenario = logFileInfo.Name;
        logFileStartOffset = 0;
        logFileEndOffset = long.MaxValue;

        log = new ReadNewLog(logFileInfo.FullName);
        lastLogResult = null;
        ObjectGraph.cachedGraph = null;
        ReadLogResult readLogResult = GetLogResult();
        log.ReadFile(logFileStartOffset, logFileEndOffset, readLogResult);
        lastLogResult = readLogResult;
    }

    private ReadLogResult GetLogResult()
    {
        ReadLogResult readLogResult = lastLogResult;
        if (readLogResult == null)
        {
            readLogResult = new ReadLogResult();
        }
        readLogResult.liveObjectTable = new LiveObjectTable(log);
        readLogResult.sampleObjectTable = new SampleObjectTable(log);
        readLogResult.allocatedHistogram = new Histogram(log);
        readLogResult.callstackHistogram = new Histogram(log);
        readLogResult.relocatedHistogram = new Histogram(log);
        readLogResult.finalizerHistogram = new Histogram(log);
        readLogResult.criticalFinalizerHistogram = new Histogram(log);
        readLogResult.createdHandlesHistogram = new Histogram(log);
        readLogResult.destroyedHandlesHistogram = new Histogram(log);

        if (readLogResult.objectGraph != null)
            readLogResult.objectGraph.Neuter();

        readLogResult.objectGraph = new ObjectGraph(log, 0);
        readLogResult.functionList = new FunctionList(log);
        readLogResult.hadCallInfo = readLogResult.hadAllocInfo = false;
        readLogResult.handleHash = new Dictionary<ulong, HandleInfo>();

        // We may just have turned a lot of data into garbage - let's try to reclaim the memory
        GC.Collect();

        return readLogResult;
    }

    [RelayCommand]
    private void AllocatedHistogram()
    {
        string title = "Histogram by Size for Allocated Objects for: " + _scenario;

        ////WinFrom option
        //HistogramViewForm histogramViewForm = new HistogramViewForm(_logResult.allocatedHistogram, title);
        //histogramViewForm.Show();

        HistogramViewModel viewModel = new HistogramViewModel(lastLogResult.allocatedHistogram, title);
        HistogramView histogramView = new HistogramView();
        histogramView.DataContext = viewModel;
        histogramView.Show();


    }
}
