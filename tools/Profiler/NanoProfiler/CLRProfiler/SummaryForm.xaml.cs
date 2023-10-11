////
// Copyright (c) .NET Foundation and Contributors.
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
////

using CLRProfiler;
using nanoFramework.Tools.NanoProfiler.ViewModels;
using nanoFramework.Tools.NanoProfiler.Views;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace nanoFramework.Tools.NanoProfiler.CLRProfiler
{
    /// <summary>
    /// Interaction logic for SummaryForm.xaml
    /// </summary>
    public partial class SummaryForm : Window
    {
        private ReadNewLog _log;
        private ReadLogResult _logResult;
        private string _scenario = "";
        internal SummaryForm()
        {
            InitializeComponent();
        }
        internal SummaryForm(
            ReadNewLog log,
            ReadLogResult logResult,
            string scenario)
        {
           

            _log = log;
            _logResult = logResult;
            _scenario = scenario;
            Title = "Summary for " + scenario;

            FillInNumbers();
        }

        private void FillInNumbers()
        {
            AllocatedBytesValueLabel.Content = CalculateTotalSize(_logResult.allocatedHistogram);
            RelocatedBytesValueLabel.Content = CalculateTotalSize(_logResult.relocatedHistogram);
            FinalHeapBytesValueLabel.Content = CalculateTotalSize(GetFinalHeapHistogram());

            Gen0CollectionsValueLabel.Content = FormatNumber(_logResult.liveObjectTable.lastGcGen0Count);
            Gen1CollectionsValueLabel.Content = FormatNumber(_logResult.liveObjectTable.lastGcGen1Count);
            Gen2CollectionsValueLabel.Content = FormatNumber(_logResult.liveObjectTable.lastGcGen2Count);
            InducedCollectionsValueLabel.Content = "Unknown";

            Gen0HeapBytesValueLabel.Content = "Unknown";
            Gen1HeapBytesValueLabel.Content = "Unknown";
            Gen2HeapBytesValueLabel.Content = "Unknown";
            ObjectsFinalizedValueLabel.Content = "Unknown";
            CriticalObjectsFinalizedValueLabel.Content = "Unknown";
            LargeObjectHeapBytesValueLabel.Content = "Unknown";

            if (_log.gcCount[0] > 0)
            {
                ObjectsFinalizedValueLabel.Content = CalculateTotalCount(_logResult.finalizerHistogram);
                CriticalObjectsFinalizedValueLabel.Content = CalculateTotalCount(_logResult.criticalFinalizerHistogram);
                InducedCollectionsValueLabel.Content = FormatNumber(_log.inducedGcCount[0]);
                Gen0HeapBytesValueLabel.Content = FormatNumber(_log.cumulativeGenerationSize[0] / (uint)_log.gcCount[0]);

                if (_log.gcCount[1] > 0)
                {
                    Gen1HeapBytesValueLabel.Content = FormatNumber(_log.cumulativeGenerationSize[1] / (uint)_log.gcCount[1]);
                }
                else
                {
                    Gen1HeapBytesValueLabel.Content = FormatNumber(_log.generationSize[1]);
                }

                if (_log.gcCount[2] > 0)
                {
                    Gen2HeapBytesValueLabel.Content = FormatNumber(_log.cumulativeGenerationSize[2] / (uint)_log.gcCount[2]);
                }
                else
                {
                    Gen2HeapBytesValueLabel.Content = FormatNumber(_log.generationSize[2]);
                }

                if (_log.gcCount[3] > 0)
                {
                    LargeObjectHeapBytesValueLabel.Content = FormatNumber(_log.cumulativeGenerationSize[3] / (uint)_log.gcCount[3]);
                }
                else
                {
                    LargeObjectHeapBytesValueLabel.Content = FormatNumber(_log.generationSize[3]);
                }
            }
            else if (!_logResult.createdHandlesHistogram.Empty)
            {
                // we know this is a new format log file
                // log.gcCount[0] was zero because there were no collections
                // in that case we know there were no induced collections and no finalized objects
                InducedCollectionsValueLabel.Content = "0";
                ObjectsFinalizedValueLabel.Content = "0";
                CriticalObjectsFinalizedValueLabel.Content = "0";
            }

            if (_logResult.createdHandlesHistogram.Empty)
            {
                HandlesCreatedValueLabel.Content = "Unknown";
                HandlesDestroyedValueLabel.Content = "Unknown";
                HandlesSurvivingValueLabel.Content = "Unknown";
            }
            else
            {
                HandlesCreatedValueLabel.Content = CalculateTotalCount(_logResult.createdHandlesHistogram);
                HandlesDestroyedValueLabel.Content = CalculateTotalCount(_logResult.destroyedHandlesHistogram);

                int count = 0;

                foreach (HandleInfo handleInfo in _logResult.handleHash.Values)
                {
                    count++;
                }

                HandlesSurvivingValueLabel.Content = FormatNumber(count);
            }

            CommentsValueLabel.Content = FormatNumber(_log.commentEventList.count);
            HeapDumpsValueLabel.Content = FormatNumber(_log.heapDumpEventList.count);
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
            Histogram histogram = new(_log);
            LiveObjectTable.LiveObject o;

            for (_logResult.liveObjectTable.GetNextObject(0, ulong.MaxValue, out o);
                o.id < ulong.MaxValue && o.id + o.size >= o.id;
                _logResult.liveObjectTable.GetNextObject(o.id + o.size, ulong.MaxValue, out o))
            {
                histogram.AddObject(o.typeSizeStacktraceIndex, 1);
            }

            return histogram;
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            Label[] copyLabel = new Label[]
                       {
                AllocatedBytesLabel,           AllocatedBytesValueLabel,
                RelocatedBytesLabel,           RelocatedBytesValueLabel,
                FinalHeapBytesLabel,           FinalHeapBytesValueLabel,
                ObjectsFinalizedLabel,         ObjectsFinalizedValueLabel,
                CriticalObjectsFinalizedLabel, CriticalObjectsFinalizedValueLabel,
                Gen0CollectionsLabel,          Gen0CollectionsValueLabel,
                Gen1CollectionsLabel,          Gen1CollectionsValueLabel,
                Gen2CollectionsLabel,          Gen2CollectionsValueLabel,
                InducedCollectionsLabel,       InducedCollectionsValueLabel,
                Gen0HeapBytesLabel,            Gen0HeapBytesValueLabel,
                Gen1HeapBytesLabel,            Gen1HeapBytesValueLabel,
                Gen2HeapBytesLabel,            Gen2HeapBytesValueLabel,
                LargeObjectHeapBytesLabel,     LargeObjectHeapBytesValueLabel,
                HandlesCreatedLabel,           HandlesCreatedValueLabel,
                HandlesDestroyedLabel,         HandlesDestroyedValueLabel,
                HandlesSurvivingLabel,         HandlesSurvivingValueLabel,
                HeapDumpsLabel,                HeapDumpsValueLabel,
                CommentsLabel,                 CommentsValueLabel,
                       };

            StringBuilder sb = new();
            sb.AppendFormat("Summary for {0}\r\n", _scenario);

            for (int i = 0; i < copyLabel.Length; i += 2)
            {
                sb.AppendFormat("{0,-30}{1,13}\r\n", copyLabel[i].Content, copyLabel[i + 1].Content);
            }

            Clipboard.SetDataObject(sb.ToString());
        }

        private void allocationGraphButton_Click(object sender, System.EventArgs e)
        {
            Graph graph = _logResult.allocatedHistogram.BuildAllocationGraph(new FilterForm());

            //WinForms.CLRProfiler.GraphViewForm graphViewForm = new WinForms.CLRProfiler.GraphViewForm(graph, "Some title");
            //graphViewForm.Show();


            GraphViewModel viewModel = new GraphViewModel(graph);
            GraphView graphView = new GraphView();
            graphView.DataContext = viewModel;
            graphView.Show();


            //GraphViewWinForm graphViewWinForm = new GraphViewWinForm(graph);
            //graphViewWinForm.Show();


            //Graph graph = _logResult.allocatedHistogram.BuildAllocationGraph(new FilterForm());
            //graph.graphType = Graph.GraphType.AllocationGraph;
            //string title = "Allocation Graph for: " + _scenario;


            //GraphViewForm graphViewForm = new GraphViewForm(graph, title);
            //graphViewForm.Show();

        }
        //  Here start histogram
        private void AllocatedHistogramButton_Click(object sender, RoutedEventArgs e)
        {
           

        }

        private void allocationGraphButton_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
