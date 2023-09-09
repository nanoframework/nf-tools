using CommunityToolkit.Mvvm.ComponentModel;
using nanoFramework.Tools.NanoProfiler.Models;
using nanoFramework.Tools.NanoProfiler.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System;
using System.Windows.Media;
using LiveCharts;
using System.Linq;
using nanoFramework.Tools.NanoProfiler.Services.API;
using CLRProfiler;
using System.Collections;
using System.Diagnostics.Metrics;
using System.Drawing;
using System.Windows.Controls;
using System.Diagnostics;
using System.Windows.Automation.Peers;
using System.Windows;
using Polly.Bulkhead;
using LiveCharts.Wpf;
using LiveCharts.Defaults;
using LiveCharts.Configurations;
using System.Windows.Markup;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Brushes = System.Windows.Media.Brushes;
using CommunityToolkit.Mvvm.Input;
using nanoFramework.Tools.NanoProfiler.CLRProfiler;
using nanoFramework.Tools.NanoProfiler.Views;
using System.Xml;
using System.Runtime.Serialization;
using System.Configuration;

namespace nanoFramework.Tools.NanoProfiler.ViewModels
{
    public partial class HistogramViewModel: ObservableObject
    {

        #region Observable Properties        

        [ObservableProperty]
        private SeriesCollection _seriesCollection = new SeriesCollection();
        
        [ObservableProperty]
        private ChartValues<BucketDataModel> _bucketsValues = new ChartValues<BucketDataModel>();

        [ObservableProperty]
        private ObservableCollection<double> _verticalScaleList;

        [ObservableProperty]
        private double _verticalScaleSelectedValue;

        [ObservableProperty]
        private ObservableCollection<string> _horizontalScaleList;

        [ObservableProperty]
        private string _horizontalScaleSelectedValue;

        [ObservableProperty]
        private ChartPoint _selectedChartPoint = new();
        #endregion


        #region Properties
        private List<TypeDescModel> listValues = new List<TypeDescModel>();
        private Dictionary<int, List<TypeDescModel>> originalDictionary = new Dictionary<int, List<TypeDescModel>>();
        private Dictionary<int, List<TypeDescModel>> convertedDictionary = new Dictionary<int, List<TypeDescModel>>();

        Bucket[] buckets = new Bucket[] {};
        double currentScaleFactor;
        public Histogram histogram { get; set; }
        private string[] typeName;
        public string _title { get; set; }

        ulong totalSize;
        int totalCount;

        TypeDesc[] typeIndexToTypeDesc;

        ArrayList sortedTypeTable;
        bool initialized = false;

        private int selectedPositionOfTheBucket;

        #endregion

        public HistogramViewModel(Histogram histogram, string title)
        {
            this.histogram = histogram;
            _title = title;
            typeName = this.histogram.readNewLog.typeName;
           
            SetComboValues();

            SetHistogram();
        }

        private void SetComboValues()
        {
            VerticalScaleList = new ObservableCollection<double>() { 1,2,5,10,20,50,100,200,500,1000,2000,5000,10000,20000 };
            VerticalScaleSelectedValue = 10;

            HorizontalScaleList = new ObservableCollection<string>() { "Coarse", "Medium", "Fine", "Very Fine" };
            HorizontalScaleSelectedValue = "Coarse";
        }
              
        public void SetHistogram()
        {
            graphPanel_Paint();

        }

        partial void OnVerticalScaleSelectedValueChanged(double value)
        {
            //scaleFactor = VerticalScaleSelectedValue;
            //SetHistogram();
        }
        partial void OnHorizontalScaleSelectedValueChanged(string value)
        {
            switch (HorizontalScaleSelectedValue)
            {
                case "Coarse":
                    scaleFactor = 2.0;
                    break;
                case "Medium":
                    scaleFactor = Math.Sqrt(2.0);
                    break;
                case "Fine":
                    scaleFactor = Math.Pow(2.0, 0.25);
                    break;
                case "Very Fine":
                    scaleFactor = Math.Pow(2.0, 0.125);
                    break;
            }
            //SetHistogram();
        }

        Bucket bucketClicked;
        #region Commands
        [RelayCommand]
        private void ShowWhoAllocated(object selectedBucket)
        {
            bucketClicked = BucketsValues[selectedPositionOfTheBucket].FullBucket;

            var typesInBucketClicked = bucketClicked.typeDescToSizeCount.Count;

            string title;
            List<TypeDesc> selectedTypes = FindSelectedTypeInSelectedBucket();

            Histogram selectedHistogram = new Histogram(histogram.readNewLog);
            if (selectedTypes != null && selectedTypes.Count > 0)
            {
                foreach (TypeDesc selectedType in selectedTypes)
                {
                    if (selectedType == null)
                    {
                        title = "Allocation Graph";
                        selectedHistogram = histogram;
                    }
                    else
                    {
                        int minSize = 0;
                        int maxSize = int.MaxValue;
                        foreach (Bucket b in buckets)
                        {
                            if (b.selected)
                            {
                                minSize = b.minSize;
                                maxSize = b.maxSize;
                            }
                        }
                        title = string.Format("Allocation Graph for {0} objects", selectedType.typeName);
                        if (minSize > 0)
                        {
                            title += string.Format(" of size between {0:n0} and {1:n0} bytes", minSize, maxSize);
                        }

                        for (int i = 0; i < histogram.typeSizeStacktraceToCount.Length; i++)
                        {
                            int count = histogram.typeSizeStacktraceToCount[i];
                            if (count > 0)
                            {
                                int[] stacktrace = histogram.readNewLog.stacktraceTable.IndexToStacktrace(i);
                                int typeIndex = stacktrace[0];
                                int size = stacktrace[1];

                                if (minSize <= size && size <= maxSize)
                                {
                                    TypeDesc t = (TypeDesc)typeIndexToTypeDesc[typeIndex];

                                    if (t == selectedType)
                                    {
                                        selectedHistogram.AddObject(i, count);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            


            Graph graph = selectedHistogram.BuildAllocationGraph(new FilterForm());

            //WinForms.CLRProfiler.GraphViewForm graphViewForm = new WinForms.CLRProfiler.GraphViewForm(graph, title);
            //graphViewForm.Show();


            GraphViewModel viewModel = new GraphViewModel(graph);
            GraphView graphView = new GraphView();
            graphView.DataContext = viewModel;
            graphView.Show();

        }

        
        [RelayCommand]
        private void DrillDown(ChartPoint chartPointSelected)
        {
            selectedPositionOfTheBucket = chartPointSelected.Key;
        }
        #endregion

        private void graphPanel_Paint()
        {
            initialized = false;

            if (histogram == null || typeName == null)
            {
                return;
            }

            BuildSizeRangesAndTypeTable(histogram.typeSizeStacktraceToCount);
                
            ColorTypes();

            DrawBuckets();
                
            initialized = true;
        }

        int verticalScale = 0;

       
        string FormatSize(ulong size)
        {
            double w = size;
            string byteString = "bytes";
            if (w >= 1024)
            {
                w /= 1024;
                byteString = "kB";
            }
            if (w >= 1024)
            {
                w /= 1024;
                byteString = "MB";
            }
            if (w >= 1024)
            {
                w /= 1024;
                byteString = "GB";
            }
            string format = "{0:f0} {1}";
            if (w < 10)
            {
                format = "{0:f1} {1}";
            }

            return string.Format(format, w, byteString);
        }

        private void DrawBuckets()
        {
            BucketsValues = new ChartValues<BucketDataModel> { };

            StackedColumnSeries columnSeries = new StackedColumnSeries();

            bool noBucketSelected = true;
            foreach (Bucket b in buckets)
            {
                if (b.selected)
                {
                    noBucketSelected = false;
                    break;
                }
            }

            double totalsizeCount = 0;
            using (System.Drawing.Brush blackBrush = new SolidBrush(System.Drawing.Color.Black))
            {
                int bucketPosition = 0;
                //int x = leftMargin;
                foreach (Bucket b in buckets)
                {
                    BucketsValues.Add(new BucketDataModel()
                    {
                        BucketPosition = bucketPosition, FullBucket = b                    
                    });

                    string s = "< " + FormatSize((ulong)b.maxSize + 1) + $"{Environment.NewLine}";

                    s += FormatSize(b.totalSize) + $"{Environment.NewLine}";
                    s += string.Format("({0:f2}%)", 100.0 * b.totalSize / totalSize);


                    System.Drawing.Brush brush = new SolidBrush(System.Drawing.Color.Transparent);

                    listValues = new List<TypeDescModel>();
                    foreach (KeyValuePair<TypeDesc, SizeCount> d in b.typeDescToSizeCount)
                    {
                        TypeDesc t = d.Key;

                        brush = t.brush;
                        if (t.selected && (b.selected || noBucketSelected))
                        {
                            brush = blackBrush;
                        }
                        var buckDet = new BucketDataModel1()
                        {
                            SectionValue = d.Value.size   //t.totalSize
                        };

                        listValues.Add(new TypeDescModel()
                        {
                            TypeDesc = t, ValueSize = d.Value.size, BucketTotalSize = b.totalSize
                        });
                        totalsizeCount += d.Value.size;
                    }
                    originalDictionary.Add(bucketPosition, listValues);

                    System.Drawing.Color drawingColor = ((SolidBrush)brush).Color;
                    System.Windows.Media.Color wpfColor = System.Windows.Media.Color.FromArgb(drawingColor.A, drawingColor.R, drawingColor.G, drawingColor.B);

                    bucketPosition++;

                    //x += bucketWidth + gap;
                }
            }

            //  Now convert to converted dictionary
            int maxLength = originalDictionary.Values.Any() ? originalDictionary.Values.Max(list => list.Count) : 0;

            for (int position = 0; position < maxLength; position++)
            {
                List<TypeDescModel> values = new List<TypeDescModel>();

                foreach (var kvp in originalDictionary)
                {
                    List<TypeDescModel> list = kvp.Value;
                    if (position < list.Count)
                    {
                        values.Add(list[position]);
                    }
                    else
                        values.Add(null);
                }

                convertedDictionary[position] = values;
            }

            foreach (KeyValuePair<int, List<TypeDescModel>> item in convertedDictionary)
            {
                //bucketNumber++;
                IChartValues values = new ChartValues<TypeDescModel>();
                if (item.Value != null && item.Value.Count > 0)
                {
                    foreach (TypeDescModel typeDescModel in item.Value)
                    {
                        if (typeDescModel != null)
                        {
                            values.Add(typeDescModel);
                        }
                        else
                        {
                            values.Add(new TypeDescModel());
                        }
                    }
                }

                var config = new CartesianMapper<TypeDescModel>()
                      .X((value, index) => index)
                      .Y((value, index) => value != null ? Math.Round((100.0 * value.ValueSize / totalsizeCount), 2) : 0d)                      
                      ;

                var stackedColumSeries = new StackedColumnSeries
                {
                    Configuration = config,
                    Values = values, 
                    DataLabels=false
                };

                SeriesCollection.Add(stackedColumSeries);
            }

        }


        static System.Drawing.Color[] colors = new System.Drawing.Color[16];

        static System.Drawing.Color[] firstColors =
{
            System.Drawing.Color.Red,
            System.Drawing.Color.Yellow,
            System.Drawing.Color.Green,
            System.Drawing.Color.Cyan,
            System.Drawing.Color.Blue,
            System.Drawing.Color.Magenta,
        };

        private void ColorTypes()
        {
            int count = 0;
            var res = buckets;
            bool anyTypeSelected = FindSelectedType() != null;

            foreach (TypeDesc t in sortedTypeTable)
            {
                if (count >= colors.Length)
                {
                    GrowColors();
                }

                if (count < firstColors.Length)
                {
                    colors[count] = firstColors[count];
                }
                else
                {
                    colors[count] = MixColor(colors[count - firstColors.Length], colors[count - firstColors.Length + 1]);
                }

                t.color = colors[count];
                if (anyTypeSelected)
                {
                    t.color = MixColor(colors[count], System.Drawing.Color.White);
                }

                t.brush = new SolidBrush(t.color);
                t.pen = new System.Drawing.Pen(t.brush);
                count++;
            }
        }

        System.Drawing.Color MixColor(System.Drawing.Color a, System.Drawing.Color b)
        {
            int R = (a.R + b.R) / 2;
            int G = (a.G + b.G) / 2;
            int B = (a.B + b.B) / 2;

            return System.Drawing.Color.FromArgb(R, G, B);
        }

        static void GrowColors()
        {
            System.Drawing.Color[] newColors = new System.Drawing.Color[2 * colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                newColors[i] = colors[i];
            }

            colors = newColors;
        }

        private List<TypeDesc> FindSelectedTypeInSelectedBucket()
        {
            List<TypeDesc> listReturning = new List<TypeDesc>();

            foreach (KeyValuePair<TypeDesc, SizeCount> item in bucketClicked.typeDescToSizeCount)
            {
                listReturning.Add(item.Key);
            }
            return listReturning;
        }

        private TypeDesc FindSelectedType()
        {
            foreach (TypeDesc t in sortedTypeTable)
            {
                if (t.selected)
                {
                    return t;
                }
            }

            return null;
        }

        void BuildSizeRangesAndTypeTable(int[] typeSizeStacktraceToCount)
        {
            BuildBuckets();

            totalSize = 0;
            totalCount = 0;

            if (typeIndexToTypeDesc == null)
            {
                typeIndexToTypeDesc = new TypeDesc[histogram.readNewLog.typeName.Length];
            }
            else
            {
                foreach (TypeDesc t in typeIndexToTypeDesc)
                {
                    if (t != null)
                    {
                        t.totalSize = 0;
                        t.count = 0;
                    }
                }
            }

            for (int i = 0; i < typeSizeStacktraceToCount.Length; i++)
            {
                int count = typeSizeStacktraceToCount[i];
                if (count == 0)
                {
                    continue;
                }

                int[] stacktrace = histogram.readNewLog.stacktraceTable.IndexToStacktrace(i);
                int typeIndex = stacktrace[0];
                int size = stacktrace[1];

                TypeDesc t = (TypeDesc)typeIndexToTypeDesc[typeIndex];
                if (t == null)
                {
                    t = new TypeDesc(typeName[typeIndex]);
                    typeIndexToTypeDesc[typeIndex] = t;
                }
                t.totalSize += (ulong)size * (ulong)count;
                t.count += count;

                totalSize += (ulong)size * (ulong)count;
                totalCount += count;

                AddToBuckets(t, size, count);
            }

            if (totalSize == 0)
            {
                totalSize = 1;
            }

            if (totalCount == 0)
            {
                totalCount = 1;
            }

            TrimEmptyBuckets();

            sortedTypeTable = new ArrayList();
            foreach (TypeDesc t in typeIndexToTypeDesc)
            {
                if (t != null)
                {
                    sortedTypeTable.Add(t);
                }
            }

            sortedTypeTable.Sort();
        }
        private double scaleFactor;
        void BuildBuckets()
        {


            if (currentScaleFactor == scaleFactor)
            {
                for (int i = 0; i < buckets.Length; i++)
                {
                    buckets[i].typeDescToSizeCount = new Dictionary<TypeDesc, SizeCount>();
                    buckets[i].totalSize = 0;
                }
                return;
            }

            currentScaleFactor = scaleFactor;

            int count = 0;
            int startSize = 8;
            double minSize;
            for (minSize = startSize; minSize < int.MaxValue; minSize *= scaleFactor)
            {
                count++;
            }

            buckets = new Bucket[count - 1];
            minSize = startSize;
            for (int i = 0; i < buckets.Length; i++)
            {
                buckets[i].minSize = (int)Math.Round(minSize);
                minSize *= scaleFactor;
                buckets[i].maxSize = (int)Math.Round(minSize) - 1;
                buckets[i].typeDescToSizeCount = new Dictionary<TypeDesc, SizeCount>();
                buckets[i].selected = false;
            }
        }

        void AddToBuckets(TypeDesc t, int size, int count)
        {
            for (int i = 0; i < buckets.Length; i++)
            {
                if (buckets[i].minSize <= size && size <= buckets[i].maxSize)
                {
                    ulong totalSize = (ulong)size * (ulong)count;
                    buckets[i].totalSize += totalSize;
                    SizeCount sizeCount;
                    if (!buckets[i].typeDescToSizeCount.TryGetValue(t, out sizeCount))
                    {
                        sizeCount = new SizeCount();
                        buckets[i].typeDescToSizeCount[t] = sizeCount;
                    }
                    sizeCount.size += totalSize;
                    sizeCount.count += count;
                    break;
                }
            }
        }

        void TrimEmptyBuckets()
        {
            int lo = 0;
            for (int i = 0; i < buckets.Length - 1; i++)
            {
                if (buckets[i].totalSize != 0 || buckets[i + 1].totalSize != 0)
                {
                    break;
                }

                lo++;
            }
            int hi = buckets.Length - 1;
            for (int i = buckets.Length - 1; i >= 0; i--)
            {
                if (buckets[i].totalSize != 0)
                {
                    break;
                }

                hi--;
            }
            if (lo <= hi)
            {
                Bucket[] newBuckets = new Bucket[hi - lo + 1];
                for (int i = lo; i <= hi; i++)
                {
                    newBuckets[i - lo] = buckets[i];
                }

                buckets = newBuckets;
            }
        }
    }
}