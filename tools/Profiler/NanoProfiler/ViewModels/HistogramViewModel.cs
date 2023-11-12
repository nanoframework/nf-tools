////
// Copyright (c) .NET Foundation and Contributors.
// See LICENSE file in the project root for full license information.
////

using CLRProfiler;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveCharts;
using LiveCharts.Configurations;
using LiveCharts.Wpf;
using nanoFramework.Tools.NanoProfiler.CLRProfiler;
using nanoFramework.Tools.NanoProfiler.Models;
using nanoFramework.Tools.NanoProfiler.Views;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;

namespace nanoFramework.Tools.NanoProfiler.ViewModels
{
    public partial class HistogramViewModel : ObservableObject
    {
        #region Observable Properties     

        [ObservableProperty]
        private ObservableCollection<string> _bucketsLabels = new();

        [ObservableProperty]
        private SeriesCollection _seriesCollection = new SeriesCollection();

        [ObservableProperty]
        private ChartValues<BucketViewModel> _bucketsValues = new ChartValues<BucketViewModel>();

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

        [ObservableProperty]
        private string _title;
        #endregion


        #region Properties
        private List<TypeDescViewModel> listValues = new List<TypeDescViewModel>();
        private Dictionary<int, List<TypeDescViewModel>> originalDictionary = new Dictionary<int, List<TypeDescViewModel>>();
        private Dictionary<int, List<TypeDescViewModel>> convertedDictionary = new Dictionary<int, List<TypeDescViewModel>>();

        Bucket[] buckets = new Bucket[] { };
        double currentScaleFactor;
        public Histogram histogram { get; set; }
        private string[] typeName;
    

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
            Title = title;
            typeName = this.histogram.readNewLog.typeName;

            SetComboValues();

            SetHistogram();
        }



        private void SetComboValues()
        {
            VerticalScaleList = new ObservableCollection<double>() { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000 };
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
            SetHistogram();
        }

        Bucket bucketClicked;
        #region Commands
        [RelayCommand]
        private void ShowWhoAllocated(object selectedBucket)
        {
            bucketClicked = BucketsValues[selectedPositionOfTheBucket].FullBucket;

            var typesInBucketClicked = bucketClicked.TypeDescToSizeCount.Count;

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
                            if (b.Selected)
                            {
                                minSize = b.MinSize;
                                maxSize = b.MaxSize;
                            }
                        }
                        title = string.Format("Allocation Graph for {0} objects", selectedType.TypeName);
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
            originalDictionary = new Dictionary<int, List<TypeDescViewModel>>();
            convertedDictionary = new Dictionary<int, List<TypeDescViewModel>>();
            BucketsValues = new ChartValues<BucketViewModel> { };
            BucketsLabels = new ObservableCollection<string>();
            SeriesCollection = new();

            StackedColumnSeries columnSeries = new StackedColumnSeries();

            bool noBucketSelected = true;
            foreach (Bucket b in buckets)
            {
                if (b.Selected)
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
                    BucketsValues.Add(new BucketViewModel()
                    {
                        BucketPosition = bucketPosition,
                        FullBucket = b
                    });
                    string label = string.Empty;
                    if (HorizontalScaleSelectedValue.Equals("Coarse"))
                    {
                        label = "< " + FormatSize((ulong)b.MaxSize + 1) + $"{Environment.NewLine}";
                        label += FormatSize(b.TotalSize) + $"{Environment.NewLine}";
                        label += "(";
                        label += string.Format("{0:f2}%", 100.0 * b.TotalSize / totalSize);
                        label += ")";
                    }
                    else
                    {
                        double res = Math.Round(100.0 * b.TotalSize / totalSize, 2);
                        label += $"{res}{Environment.NewLine}";
                        label += $"%";
                    }

                    System.Drawing.Brush brush = new SolidBrush(System.Drawing.Color.Transparent);

                    listValues = new List<TypeDescViewModel>();
                    foreach (KeyValuePair<TypeDesc, SizeCount> d in b.TypeDescToSizeCount)
                    {
                        TypeDesc t = d.Key;

                        brush = t.Brush;
                        if (t.Selected && (b.Selected || noBucketSelected))
                        {
                            brush = blackBrush;
                        }
                        //var buckDet = new BucketDataModel1()
                        //{
                        //    SectionValue = d.Value.size   //t.totalSize
                        //};

                        listValues.Add(new TypeDescViewModel()
                        {
                            TypeDesc = t,
                            ValueSize = d.Value.Size,
                            BucketTotalSize = b.TotalSize
                        });
                        totalsizeCount += d.Value.Size;
                    }
                    originalDictionary.Add(bucketPosition, listValues);

                    System.Drawing.Color drawingColor = ((SolidBrush)brush).Color;
                    System.Windows.Media.Color wpfColor = System.Windows.Media.Color.FromArgb(drawingColor.A, drawingColor.R, drawingColor.G, drawingColor.B);

                    BucketsLabels.Add(label);

                    //x += bucketWidth + gap;
                    bucketPosition++;
                }
            }

            //  Now convert to converted dictionary
            int maxLength = originalDictionary.Values.Any() ? originalDictionary.Values.Max(list => list.Count) : 0;

            for (int position = 0; position < maxLength; position++)
            {
                List<TypeDescViewModel> values = new List<TypeDescViewModel>();

                foreach (var kvp in originalDictionary)
                {
                    List<TypeDescViewModel> list = kvp.Value;
                    if (position < list.Count)
                    {
                        values.Add(list[position]);
                    }
                    else
                        values.Add(null);
                }

                convertedDictionary[position] = values;
            }

            foreach (KeyValuePair<int, List<TypeDescViewModel>> item in convertedDictionary)
            {
                IChartValues values = new ChartValues<TypeDescViewModel>();
                if (item.Value != null && item.Value.Count > 0)
                {
                    foreach (TypeDescViewModel typeDescModel in item.Value)
                    {
                        if (typeDescModel != null)
                        {
                            values.Add(typeDescModel);
                        }
                        else
                        {
                            values.Add(new TypeDescViewModel());
                        }
                    }
                }

                var config = new CartesianMapper<TypeDescViewModel>()
                      .X((value, index) => index)
                      .Y((value, index) => value != null ? Math.Round((100.0 * value.ValueSize / totalsizeCount), 2) : 0d)
                      ;

                var stackedColumSeries = new StackedColumnSeries
                {
                    Configuration = config,
                    Values = values,
                    DataLabels = false
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

                t.Color = colors[count];
                if (anyTypeSelected)
                {
                    t.Color = MixColor(colors[count], System.Drawing.Color.White);
                }

                t.Brush = new SolidBrush(t.Color);
                t.Pen = new System.Drawing.Pen(t.Brush);
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

            foreach (KeyValuePair<TypeDesc, SizeCount> item in bucketClicked.TypeDescToSizeCount)
            {
                listReturning.Add(item.Key);
            }
            return listReturning;
        }

        private TypeDesc FindSelectedType()
        {
            foreach (TypeDesc t in sortedTypeTable)
            {
                if (t.Selected)
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
                        t.TotalSize = 0;
                        t.Count = 0;
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
                t.TotalSize += (ulong)size * (ulong)count;
                t.Count += count;

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
                    buckets[i].TypeDescToSizeCount = new Dictionary<TypeDesc, SizeCount>();
                    buckets[i].TotalSize = 0;
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
                buckets[i].MinSize = (int)Math.Round(minSize);
                minSize *= scaleFactor;
                buckets[i].MaxSize = (int)Math.Round(minSize) - 1;
                buckets[i].TypeDescToSizeCount = new Dictionary<TypeDesc, SizeCount>();
                buckets[i].Selected = false;
            }
        }

        void AddToBuckets(TypeDesc t, int size, int count)
        {
            for (int i = 0; i < buckets.Length; i++)
            {
                if (buckets[i].MinSize <= size && size <= buckets[i].MaxSize)
                {
                    ulong totalSize = (ulong)size * (ulong)count;
                    buckets[i].TotalSize += totalSize;
                    SizeCount sizeCount;
                    if (!buckets[i].TypeDescToSizeCount.TryGetValue(t, out sizeCount))
                    {
                        sizeCount = new SizeCount();
                        buckets[i].TypeDescToSizeCount[t] = sizeCount;
                    }
                    sizeCount.Size += totalSize;
                    sizeCount.Count += count;
                    break;
                }
            }
        }

        void TrimEmptyBuckets()
        {
            int lo = 0;
            for (int i = 0; i < buckets.Length - 1; i++)
            {
                if (buckets[i].TotalSize != 0 || buckets[i + 1].TotalSize != 0)
                {
                    break;
                }

                lo++;
            }
            int hi = buckets.Length - 1;
            for (int i = buckets.Length - 1; i >= 0; i--)
            {
                if (buckets[i].TotalSize != 0)
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