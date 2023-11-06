////
// Copyright (c) .NET Foundation and Contributors.
// See LICENSE file in the project root for full license information.
////

using CLRProfiler;
using CommunityToolkit.Mvvm.ComponentModel;
using nanoFramework.Tools.NanoProfiler.CLRProfiler;
using System.Drawing;
using System.IO;
using nanoFramework.Tools.NanoProfiler.Views;
using CommunityToolkit.Mvvm.Input;
using Microsoft.VisualBasic.Logging;


namespace nanoFramework.Tools.NanoProfiler.ViewModels;

public partial class SummaryViewModel : ObservableObject
{
    private const string Unknown = "Unknown";
    private string _scenario = Unknown;

    //internal Font font;
    internal ReadNewLog? log;
    internal ReadLogResult? lastLogResult;
    internal string prevlogFileName = Unknown;
    internal string currlogFileName = Unknown;
    internal Graph.GraphType graphtype = Graph.GraphType.Invalid;
    internal bool runaswindow = false;

    #region  Observable Properties
    [ObservableProperty]
    private string _allocatedBytesValueLabel = Unknown;
    [ObservableProperty]
    private string _relocatedBytesValueLabel = Unknown;
    [ObservableProperty]
    private string _finalHeapBytesValueLabel = Unknown;
    [ObservableProperty]
    private string _gen0CollectionsValueLabel = Unknown;
    [ObservableProperty]
    private string _gen1CollectionsValueLabel = Unknown;
    [ObservableProperty]
    private string _gen2CollectionsValueLabel = Unknown;
    [ObservableProperty]
    private string _inducedCollectionsValueLabel = Unknown;
    [ObservableProperty]
    private string _gen0HeapBytesValueLabel = Unknown;
    [ObservableProperty]
    private string _gen1HeapBytesValueLabel = Unknown;
    [ObservableProperty]
    private string _gen2HeapBytesValueLabel = Unknown;
    [ObservableProperty]
    private string _objectsFinalizedValueLabel = Unknown;
    [ObservableProperty]
    private string _criticalObjectsFinalizedValueLabel = Unknown;
    [ObservableProperty]
    private string _largeObjectHeapBytesValueLabel = Unknown;
    [ObservableProperty]
    private string _handlesCreatedValueLabel = Unknown;
    [ObservableProperty]
    private string _handlesDestroyedValueLabel = Unknown;
    [ObservableProperty]
    private string _handlesSurvivingValueLabel = Unknown;
    [ObservableProperty]
    private string _commentsValueLabel = Unknown;
    [ObservableProperty]
    private string _heapDumpsValueLabel = Unknown;
    [ObservableProperty]
    private string _title = Unknown;
    #endregion

    public SummaryViewModel(ReadNewLog? readNewLog, ReadLogResult? readLogResultModel)
    {
        log =  readNewLog;
        lastLogResult = readLogResultModel;
        _scenario = log.fileName;
        Title= $"Summary for {_scenario}";
        FillInNumbers();
    }

    #region  Private Methods
    private void FillInNumbers()
    {
        AllocatedBytesValueLabel  = CalculateTotalSize(lastLogResult!.allocatedHistogram);
        RelocatedBytesValueLabel  = CalculateTotalSize(lastLogResult!.relocatedHistogram);
        FinalHeapBytesValueLabel  = CalculateTotalSize(GetFinalHeapHistogram());

        Gen0CollectionsValueLabel  = FormatNumber(lastLogResult.liveObjectTable.lastGcGen0Count);
        Gen1CollectionsValueLabel  = FormatNumber(lastLogResult.liveObjectTable.lastGcGen1Count);
        Gen2CollectionsValueLabel  = FormatNumber(lastLogResult.liveObjectTable.lastGcGen2Count);
        InducedCollectionsValueLabel  = Unknown;

        Gen0HeapBytesValueLabel  = Unknown;
        Gen1HeapBytesValueLabel  = Unknown;
        Gen2HeapBytesValueLabel  = Unknown;
        ObjectsFinalizedValueLabel  = Unknown;
        CriticalObjectsFinalizedValueLabel  = Unknown;
        LargeObjectHeapBytesValueLabel  = Unknown;

        if (log.gcCount[0] > 0)
        {
            ObjectsFinalizedValueLabel  = CalculateTotalCount(lastLogResult.finalizerHistogram);
            CriticalObjectsFinalizedValueLabel  = CalculateTotalCount(lastLogResult.criticalFinalizerHistogram);
            InducedCollectionsValueLabel  = FormatNumber(log.inducedGcCount[0]);
            Gen0HeapBytesValueLabel  = FormatNumber(log.cumulativeGenerationSize[0] / (uint)log.gcCount[0]);

            if (log.gcCount[1] > 0)
            {
                Gen1HeapBytesValueLabel  = FormatNumber(log.cumulativeGenerationSize[1] / (uint)log.gcCount[1]);
            }
            else
            {
                Gen1HeapBytesValueLabel  = FormatNumber(log.generationSize[1]);
            }

            if (log.gcCount[2] > 0)
            {
                Gen2HeapBytesValueLabel  = FormatNumber(log.cumulativeGenerationSize[2] / (uint)log.gcCount[2]);
            }
            else
            {
                Gen2HeapBytesValueLabel  = FormatNumber(log.generationSize[2]);
            }

            if (log.gcCount[3] > 0)
            {
                LargeObjectHeapBytesValueLabel  = FormatNumber(log.cumulativeGenerationSize[3] / (uint)log.gcCount[3]);
            }
            else
            {
                LargeObjectHeapBytesValueLabel  = FormatNumber(log.generationSize[3]);
            }
        }
        else if (!lastLogResult.createdHandlesHistogram.Empty)
        {
            // we know this is a new format log file
            // log.gcCount[0] was zero because there were no collections
            // in that case we know there were no induced collections and no finalized objects
            InducedCollectionsValueLabel  = "0";
            ObjectsFinalizedValueLabel  = "0";
            CriticalObjectsFinalizedValueLabel  = "0";
        }

        if (lastLogResult.createdHandlesHistogram.Empty)
        {
            HandlesCreatedValueLabel  = Unknown;
            HandlesDestroyedValueLabel  = Unknown;
            HandlesSurvivingValueLabel  = Unknown;
        }
        else
        {
            HandlesCreatedValueLabel  = CalculateTotalCount(lastLogResult.createdHandlesHistogram);
            HandlesDestroyedValueLabel  = CalculateTotalCount(lastLogResult.destroyedHandlesHistogram);

            int count = 0;

            foreach (HandleInfo handleInfo in lastLogResult.handleHash.Values)
            {
                count++;
            }

            HandlesSurvivingValueLabel  = FormatNumber(count);
        }

        CommentsValueLabel  = FormatNumber(log.commentEventList.count);
        HeapDumpsValueLabel  = FormatNumber(log.heapDumpEventList.count);
    }
    private string FormatNumber(double number)
    {
        return string.Format("{0:N0}", number);
    }
    private string CalculateTotalSize(Histogram histogram)
    {
        double totalSize = 0.0;

        for (int i = 0; i < histogram.typeSizeStacktraceToCount.Length; i++)
        {
            int count = histogram.typeSizeStacktraceToCount[i];
            if (count > 0)
            {
                int[] stacktrace = histogram.readNewLog.stacktraceTable.IndexToStacktrace(i);
                int size = stacktrace[1];
                totalSize += (ulong)size * (ulong)count;
            }
        }

        return FormatNumber(totalSize);
    }
    private string CalculateTotalCount(Histogram histogram)
    {
        double totalCount = 0.0;

        for (int i = 0; i < histogram.typeSizeStacktraceToCount.Length; i++)
        {
            int count = histogram.typeSizeStacktraceToCount[i];
            totalCount += count;
        }

        return FormatNumber(totalCount);
    }
    private Histogram GetFinalHeapHistogram()
    {
        Histogram histogram = new(log);
        LiveObjectTable.LiveObject o;

        for (lastLogResult.liveObjectTable.GetNextObject(0, ulong.MaxValue, out o);
            o.id < ulong.MaxValue && o.id + o.size >= o.id;
            lastLogResult.liveObjectTable.GetNextObject(o.id + o.size, ulong.MaxValue, out o))
        {
            histogram.AddObject(o.typeSizeStacktraceIndex, 1);
        }

        return histogram;
    }
    #endregion


    #region  Commands
    [RelayCommand]
    private void AllocatedHistogram()
    {
        string title = "Histogram by Size for Allocated Objects for: " + _scenario;
        HistogramViewModel viewModel = new HistogramViewModel(lastLogResult!.allocatedHistogram, title);
        HistogramView histogramView = new HistogramView();
        histogramView.DataContext = viewModel;
        histogramView.Show();

    }
    [RelayCommand]
    private void RelocatedHistogram()
    {
        string title = "Histogram by Size for Relocated Objects for: " + _scenario;
        HistogramViewModel viewModel = new HistogramViewModel(lastLogResult!.relocatedHistogram, title);
        HistogramView histogramView = new HistogramView();
        histogramView.DataContext = viewModel;
        histogramView.Show();
    }
    [RelayCommand]
    private void FinalHeapHistogram()
    {
        string title = "Histogram by Size for Final Heap Objects for: " + _scenario;
        HistogramViewModel viewModel = new HistogramViewModel(lastLogResult!.callstackHistogram, title);
        HistogramView histogramView = new HistogramView();
        histogramView.DataContext = viewModel;
        histogramView.Show();
    }
    [RelayCommand]
    private void FinalizedHistogram()
    {
        string title = "Histogram by Size for Final Objects for: " + _scenario;
        HistogramViewModel viewModel = new HistogramViewModel(lastLogResult!.finalizerHistogram, title);
        HistogramView histogramView = new HistogramView();
        histogramView.DataContext = viewModel;
        histogramView.Show();
    }
    [RelayCommand]
    private void CriticalFinalizedHistogram()
    {
        string title = "Histogram by Size for critical Objects finalized for: " + _scenario;
        HistogramViewModel viewModel = new HistogramViewModel(lastLogResult!.criticalFinalizerHistogram, title);
        HistogramView histogramView = new HistogramView();
        histogramView.DataContext = viewModel;
        histogramView.Show();
    }

    [RelayCommand]
    private void AllocationGraph()
    {
        string title = "Histogram by Size for Final Heap Objects for: " + _scenario;
        var viewModel = new GraphViewModel(lastLogResult!.allocatedHistogram.BuildAllocationGraph(new FilterForm()));
        var view = new GraphView();
        view.DataContext = viewModel;
        view.Show();
    }
    #endregion
}
