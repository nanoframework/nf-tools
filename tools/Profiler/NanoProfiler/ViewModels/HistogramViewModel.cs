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

namespace nanoFramework.Tools.NanoProfiler.ViewModels
{
    public partial class HistogramViewModel: ObservableObject
    {
        //[ObservableProperty]
        //private SeriesCollection _seriesCollection = new SeriesCollection();
        [ObservableProperty]
        private SeriesCollection _seriesCollection11 = new SeriesCollection();
        public string[] Labels { get; set; }
        public Func<double, string> Formatter { get; set; }
        private void SetHistogramDifferentColors()
        {

            //SeriesCollection11.Add(new StackedColumnSeries
            //{
            //    Values = new ChartValues<BucketDataModel1>
            //    {
            //        new BucketDataModel1() {
            //                Pesma="neka pesma",
            //                Broj = 1
            //        },
            //        new BucketDataModel1() {
            //                Pesma="neka pesma",
            //                Broj = 2
            //        },
            //        new BucketDataModel1() {
            //                Pesma="neka pesma",
            //                Broj = 3
            //        }
            //    },
            //    StackMode = StackMode.Values, // this is not necessary, values is the default stack mode
            //    DataLabels = true
            //});

            //SeriesCollection = new SeriesCollection
            //{
            //    new StackedColumnSeries
            //    {
            //        Values = new ChartValues<double> {4, 5, 6, 8},
            //        StackMode = StackMode.Values, // this is not necessary, values is the default stack mode
            //        DataLabels = true
            //    },
            //    new StackedColumnSeries
            //    {
            //        Values = new ChartValues<double> {2, 5, 6, 7},
            //        StackMode = StackMode.Values,
            //        DataLabels = true
            //    }
            //};

            //SeriesCollection.Add(new StackedColumnSeries
            //{
            //    Values = new ChartValues<double> { 4, 5, 6, 8 },
            //    //StackMode = StackMode.Values, // this is not necessary, values is the default stack mode
            //    DataLabels = true
            //});
            //SeriesCollection.Add(new StackedColumnSeries
            //{
            //    Values = new ChartValues<double> { 2, 5, 6, 7 },
            //    //StackMode = StackMode.Values,
            //    DataLabels = true
            //});
            //SeriesCollection.Add(new StackedColumnSeries
            //{
            //    Values = new ChartValues<double> { 6, 2, 7 },
            //    //StackMode = StackMode.Values,
            //    DataLabels = true
            //});


            //adding series updates and animates the chart
            //SeriesCollection11.Add(new StackedColumnSeries
            //{
            //    Values = new ChartValues<BucketDataModel1>
            //    {
            //        new BucketDataModel1() {
            //                Pesma="neka pesma", 
            //                Broj = 1
            //        },
            //        new BucketDataModel1() {
            //                Pesma="neka pesma",
            //                Broj = 2
            //        },
            //        new BucketDataModel1() {
            //                Pesma="neka pesma",
            //                Broj = 3
            //        }
            //    },
            //    StackMode = StackMode.Values, // this is not necessary, values is the default stack mode
            //    DataLabels = true
            //});
            //SeriesCollection11.Add(new StackedColumnSeries
            //{
            //    Values = new ChartValues<BucketDataModel1>
            //    {
            //        new BucketDataModel1() {
            //                Pesma="neka pesma",
            //                Broj = 4
            //        },
            //        new BucketDataModel1() {
            //                Pesma="neka pesma",
            //                Broj = 5
            //        },
            //        new BucketDataModel1() {
            //                Pesma="neka pesma",
            //                Broj = 6
            //        }
            //    },
            //    StackMode = StackMode.Values, // this is not necessary, values is the default stack mode
            //    DataLabels = true
            //});
            //SeriesCollection11.Add(new StackedColumnSeries
            //{
            //    Values = new ChartValues<BucketDataModel1>
            //    {
            //         new BucketDataModel1() {
            //                Pesma="neka pesma",
            //                Broj = 7
            //        },
            //        new BucketDataModel1() {
            //                Pesma="neka pesma",
            //                Broj = 8
            //        },
            //        new BucketDataModel1() {
            //                Pesma="neka pesma",
            //                Broj = 9
            //        }
            //    },
            //    StackMode = StackMode.Values, // this is not necessary, values is the default stack mode
            //    DataLabels = true
            //});

            ////adding values also updates and animates
            //SeriesCollection[2].Values.Add(4d);

            var config = new CartesianMapper<BucketDataModel1>()
                .X((value, index) => index)
                .Y((value, index) => Math.Round((100.0 * value.SectionValue / totalSize), 2))
                //.Y((value, index) => value.SectionValue)
                //.Fill(value => System.Windows.Media.Colors.Green)
                ;
                //.X((value, index) => index)
                //.Y((value, index) => Math.Round((100.0 * value.FullBucket.totalSize / totalSize), 2))


            SeriesCollection11.Configuration = config;

            //Labels = new[] { "Chrome", "Mozilla", "Opera", "IE" };
            //Formatter = value => value + " Mill";
        }


        


        #region Observable Properties
        [ObservableProperty]
        private SeriesCollection _seriesCollectionFinal = new SeriesCollection();

        [ObservableProperty]
        private ChartValues<StackedColumnSeries> _bucketsValuesFULL1 = new ChartValues<StackedColumnSeries>();

        [ObservableProperty]
        private ChartValues<List<BucketDataModel1>> _bucketsValuesFULL = new ChartValues<List<BucketDataModel1>>();
        [ObservableProperty]
        private ChartValues<BucketDataModel> _bucketsValues = new ChartValues<BucketDataModel>();

        [ObservableProperty]
        private ObservableCollection<string> _bucketsLabels = new ObservableCollection<string>();
        [ObservableProperty]
        private CartesianMapper<BucketDataModel> _bucketsConfiguration;
        [ObservableProperty]
        private CartesianMapper<List<BucketDataModel1>> _bucketsConfigurationFULL;
        [ObservableProperty]
        private CartesianMapper<StackedColumnSeries> _bucketsConfigurationFULL1;

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
            SetHistogramDifferentColors();

            //SetHistogram();
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

            BucketsConfiguration = new CartesianMapper<BucketDataModel>()
                .X((value, index) => index)
                .Y((value, index) => Math.Round((100.0 * value.FullBucket.totalSize / totalSize), 2))
                .Fill(SetColumnFill());

            BucketsConfigurationFULL = new CartesianMapper<List<BucketDataModel1>>()
                .X((value, index) =>
                {
                    return index;
                })
                .Y((value, index) =>
                {
                    foreach (var itemS in value)
                    {
                        return Math.Round((100.0 * itemS.SectionValue / totalSize), 2);
                    };
                    return 0;
                });
            
            
            BucketsConfigurationFULL1 = new CartesianMapper<StackedColumnSeries>()
                .X((value, index) =>
                {
                    return index;
                })
                .Y((value, index) =>
                {
                    foreach (var itemS in value.Values)
                    {
                        BucketDataModel1 res = (BucketDataModel1)itemS;
                        return Math.Round((100.0 * res.SectionValue / totalSize), 2);
                    };
                    return 0;
                })
                ;

            //BucketsConfigurationFULL = new CartesianMapper<List<BucketDataModel>>()
            //    .X((value, index) => index)
            //    .Y((value, index) => value.inde)
            //    ;
        }

       
        private Func<BucketDataModel, object> SetColumnFill()
        {
            //  Trying to provide different coloring for buckets
            //  Colors are there, I need to find the way to color points separately
            return (value =>
            {
                System.Drawing.Color drawingColor;
                if (value.FullBucket.typeDescToSizeCount.Count > 0)
                {
                    if (value.FullBucket.typeDescToSizeCount.Keys.Count > 1)
                    {
                        drawingColor = value.FullBucket.typeDescToSizeCount.Keys.ToList()[1].color;
                    }
                    else
                    {
                        drawingColor = value.FullBucket.typeDescToSizeCount.Keys.First().color;
                    }
                }
                else
                    drawingColor = System.Drawing.Color.Red;

                SolidColorBrush finalColor = new SolidColorBrush(System.Windows.Media.Color.FromArgb(drawingColor.A, drawingColor.R, drawingColor.G, drawingColor.B));

                return finalColor;
            });
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


        private void showWhoAllocatedMenuItem_Click(object sender, System.EventArgs e)
        {
            Console.WriteLine();
            Histogram selectedHistogram;
            string title;
            TypeDesc selectedType = FindSelectedType();
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

                selectedHistogram = new Histogram(histogram.readNewLog);
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



            Graph graph = selectedHistogram.BuildAllocationGraph(new FilterForm());

            WinForms.CLRProfiler.GraphViewForm graphViewForm = new WinForms.CLRProfiler.GraphViewForm(graph, title);
            graphViewForm.Show();
        }

        private void graphPanel_Paint()
        {
            initialized = false;

            if (histogram == null || typeName == null)
            {
                return;
            }

            //if (e != null)
            //{
                //Graphics g = e.Graphics;

                //bucketWidth = BucketWidth(g);
                //bottomMargin = BottomMargin();

                BuildSizeRangesAndTypeTable(histogram.typeSizeStacktraceToCount);
                
                ColorTypes();

                //ulong maxTotalSize = 0;
                //foreach (Bucket b in buckets)
                //{
                //    if (maxTotalSize < b.totalSize)
                //    {
                //        maxTotalSize = b.totalSize;
                //    }
                //}

                //Here some vetical changes should take effect
                //verticalScale = VerticalScale((int)graphPanel.Height - topMargin - bottomMargin, maxTotalSize, verticalScale == 0);

                //int maxBucketHeight = (int)(maxTotalSize / (ulong)verticalScale);
                //int height = topMargin + maxBucketHeight + bottomMargin;
                //if (height < minHeight)
                //{
                //    height = minHeight;
                //}

                //graphPanel.Height = height;

                //int width = leftMargin + buckets.Length * bucketWidth + (buckets.Length - 1) * gap + rightMargin;
                //graphPanel.Width = width;

                DrawBuckets();
                
                initialized = true;
            //}


        }

        int verticalScale = 0;

        //int VerticalScale(int pixelsAvailable, ulong rangeNeeded, bool firstTime)
        //{
        //    return Scale(pixelsAvailable, (int)(rangeNeeded / 1024), firstTime) * 1024;
        //}

        //int Scale(int pixelsAvailable, int rangeNeeded, bool firstTime)
        //{
        //    if (!firstTime)
        //    {
        //        foreach (RadioButton rb in groupBox.Controls)
        //        {
        //            if (rb.Checked)
        //                return Int32.Parse(rb.Text);
        //        }
        //    }
        //    // No radio button was checked - let's come up with a suitable default
        //    RadioButton maxLowScaleRB = null;
        //    int maxLowRange = 0;
        //    RadioButton minHighScaleRB = null;
        //    int minHighRange = Int32.MaxValue;
        //    foreach (RadioButton rb in groupBox.Controls)
        //    {
        //        int range = pixelsAvailable * Int32.Parse(rb.Text);
        //        if (range < rangeNeeded)
        //        {
        //            if (maxLowRange < range)
        //            {
        //                maxLowRange = range;
        //                maxLowScaleRB = rb;
        //            }
        //        }
        //        else
        //        {
        //            if (minHighRange > range)
        //            {
        //                minHighRange = range;
        //                minHighScaleRB = rb;
        //            }
        //        }
        //    }
        //    if (minHighScaleRB != null)
        //    {
        //        minHighScaleRB.Checked = true;
        //        return Int32.Parse(minHighScaleRB.Text);
        //    }
        //    else
        //    {
        //        maxLowScaleRB.Checked = true;
        //        return Int32.Parse(maxLowScaleRB.Text);
        //    }
        //}

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
            //Clear all data
            //BucketsValues = new ChartValues<List<BucketDataModel>> { };
            BucketsValues = new ChartValues<BucketDataModel> { };
            BucketsLabels = new ObservableCollection<string>();

            //SeriesCollection = new SeriesCollection();

            //SeriesCollection.Add(new StackedColumnSeries
            //{
            //    Values = new ChartValues<double> { 6, 2, 7 },
            //    StackMode = StackMode.Values
            //});

            //Values = new ChartValues<double> { 4, 5, 6, 8 },
            //        StackMode = StackMode.Values, // this is not necessary, values is the default stack mode
            //        DataLabels = true

            StackedColumnSeries columnSeries = new StackedColumnSeries();
            List<BucketDataModel1> listFull = new List<BucketDataModel1>();

            //Debug.Assert(verticalScale != 0);
            bool noBucketSelected = true;
            foreach (Bucket b in buckets)
            {
                if (b.selected)
                {
                    noBucketSelected = false;
                    break;
                }
            }
            IChartValues valuesF = new ChartValues<BucketDataModel1>();
            ChartValues<BucketDataModel1> chartValuesF = new ChartValues<BucketDataModel1>();


            using (System.Drawing.Brush blackBrush = new SolidBrush(System.Drawing.Color.Black))
            {
                List<BucketDataModel> listBucketDataModels = new List<BucketDataModel>();
                int bucketPosition = 0;
                //int x = leftMargin;
                foreach (Bucket b in buckets)
                {
                    string s = "< " + FormatSize((ulong)b.maxSize + 1) + $"{Environment.NewLine}";

                    //int y = (int)(graphPanel.Height - bottomMargin);

                    //g.DrawString(s, font, blackBrush, x, y + 3);
                    s += FormatSize(b.totalSize) + $"{Environment.NewLine}";
                    //g.DrawString(s, font, blackBrush, x, y + 3 + font.Height);
                    s += string.Format("({0:f2}%)", 100.0 * b.totalSize / totalSize);


                    System.Drawing.Brush brush = new SolidBrush(System.Drawing.Color.Transparent);
                    //g.DrawString(s, font, blackBrush, x, y + 3 + font.Height * 2);
                        
                    
                    chartValuesF = new ChartValues<BucketDataModel1>();

                    listFull = new List<BucketDataModel1>();

                    foreach (KeyValuePair<TypeDesc, SizeCount> d in b.typeDescToSizeCount)
                    {
                        TypeDesc t = d.Key;
                        //SizeCount sizeCount = d.Value;
                        //ulong size = sizeCount.size;
                        //int height = (int)(size / (ulong)verticalScale);

                        //y -= height;

                        brush = t.brush;
                        if (t.selected && (b.selected || noBucketSelected))
                        {
                            brush = blackBrush;
                        }
                        var buckDet = new BucketDataModel1()
                        {
                            SectionValue = t.totalSize
                        };
                        listFull.Add(buckDet);
                        //g.FillRectangle(brush, x, y, bucketWidth, height);

                        chartValuesF.Add(new BucketDataModel1()
                        {
                            SectionValue = t.totalSize
                        });

                    }
                    columnSeries = new StackedColumnSeries
                    {
                        Values = chartValuesF,
                        DataLabels = false
                    };
                    SeriesCollection11.Add(columnSeries);

                    //SeriesCollection.Add(new StackedColumnSeries
                    //{
                    //    Values = new ChartValues<double> { 4, 5, 6, 8 },
                    //    //StackMode = StackMode.Values, // this is not necessary, values is the default stack mode
                    //    DataLabels = true
                    //});

                    System.Drawing.Color drawingColor = ((SolidBrush)brush).Color;
                    System.Windows.Media.Color wpfColor = System.Windows.Media.Color.FromArgb(drawingColor.A, drawingColor.R, drawingColor.G, drawingColor.B);
                    //Math.Round((100.0 * value.FullBucket.totalSize / totalSize), 2))
                    //BucketsLabels.Add(s);
                    //Math.Round((100.0 * b.)

                    BucketsValuesFULL.Add(listFull);


                    BucketsValuesFULL1.Add(columnSeries);


                    BucketsValues.Add(new BucketDataModel()
                    {
                        BucketColor = new SolidColorBrush(wpfColor), 
                        FullBucket = b, 
                        BucketPosition = bucketPosition
                    });
                    bucketPosition++;

                    //x += bucketWidth + gap;
                }

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


        //struct Bucket
        //{
        //    internal int minSize;
        //    internal int maxSize;
        //    internal ulong totalSize;
        //    internal Dictionary<TypeDesc, SizeCount> typeDescToSizeCount;
        //    internal bool selected;
        //    internal string label;
        //};

        //private class SizeCount
        //{
        //    internal ulong size;
        //    internal int count;
        //}

        //class TypeDesc : IComparable
        //{
        //    internal string typeName;
        //    internal ulong totalSize;
        //    internal int count;
        //    internal System.Drawing.Color color;
        //    internal System.Drawing.Brush brush;
        //    internal System.Drawing.Pen pen;
        //    internal bool selected;
        //    internal System.Drawing.Rectangle rect;

        //    internal TypeDesc(string typeName)
        //    {
        //        this.typeName = typeName;
        //    }

        //    public int CompareTo(object o)
        //    {
        //        TypeDesc t = (TypeDesc)o;
        //        if (t.totalSize < this.totalSize)
        //        {
        //            return -1;
        //        }
        //        else if (t.totalSize > this.totalSize)
        //        {
        //            return 1;
        //        }
        //        else
        //        {
        //            return 0;
        //        }
        //    }
        //}



    }
}



//public async Task Load()
//{





//    //countries = await _coronavirusCountryService.GetTopCasesAsync(_numberOfBuckets);

//    //CoronavirusCountryCaseCounts = new ChartValues<int>(countries.Select(c => c.Cases));

//    //BucketLabels.Clear();


//    //foreach (string countryName in countries.Select(c => c.Country))
//    //{
//    //    BucketLabels.Add(countryName);
//    //}














//    //SeriesCollection = new SeriesCollection()
//    //    {
//    //        new LineSeries() {  Values = CoronavirusCountryCaseCounts }
//    //    };

//    // Initialize the binding source



//    //.X(point => point.X)
//    //.Y(point => point.Y)
//    //.Stroke(point => point.Y > 0.3 ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.LightGreen)
//    //.Fill(point => point.Y > 0.3 ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.LightGreen);

//    //SeriesCollection = new LiveCharts.SeriesCollection();

//    //List<ChartValues<int>> seriesList = new List<ChartValues<int>>();
//    //CoronavirusCountryCaseCounts = new ChartValues<int>(countries.Select(c => c.Cases));




//    //for (int i = 0; i < countries.Count(); ++i)
//    //{
//    //    SeriesCollection.Add(new ColumnSeries
//    //    {
//    //        Values = countries[i],
//    //        Name = CoronavirusCountryCaseCounts[i].ToString(),
//    //        Fill = System.Windows.Media.Brushes.Transparent,
//    //        StrokeThickness = 1,
//    //        PointGeometry = null,
//    //    });
//    //}

//    //this.BlueSeries = new ColumnSeries()
//    //{
//    //    Title = "Population of Bodrum",
//    //    Values = new ChartValues<double> { 1500, 2500, 3700, 2000, 1000 },
//    //    Fill = System.Windows.Media.Brushes.Blue
//    //};
//    //this.SeriesCollection = new SeriesCollection() { this.BlueSeries };



//    ////graphPanel_Paint();


//    //BucketLabels.Clear();


//    //foreach (string countryName in countries.Select(c => c.Country))
//    //{
//    //    BucketLabels.Add(countryName);
//    //}


//    //foreach (var bucket in buckets)
//    //{
//    //    BucketLabels.Add(bucket.label);
//    //}
//    //ChangeThirdChartPointColorToRed();
//}

////private void ChangeThirdChartPointColorToRed()
////{
////    CartesianMapper<double> mapper = Mappers.Xy<double>()
////      .X((value, index) => index)
////      .Y(value => value)
////      .Fill((value, index) => index == 2 ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.Blue);

////    // Dynamically set the third chart point color to red
////    this.BlueSeries.Configuration = mapper;
////}
///
//var res = _numberOfBuckets;
//var res1 = BucketLabels;

//countries = new List<CoronavirusCountry>()
//{
//    new CoronavirusCountry() {Country="AAAA", Cases=10},
//    new CoronavirusCountry() {Country="BBBB", Cases=20},
//    new CoronavirusCountry() {Country="CCCC", Cases=30},
//    new CoronavirusCountry() {Country="DDDD", Cases=40},
//    new CoronavirusCountry() {Country="EEEE", Cases=50},
//    new CoronavirusCountry() {Country="FFFF", Cases=60},
//    new CoronavirusCountry() {Country="GGGG", Cases=70},
//};





//values.Add(new DataModel() { Label = $"Column 1", Value = 11 });
//values.Add(new DataModel() { Label = $"Column 2", Value = 22 });
//values.Add(new DataModel() { Label = $"Column 3", Value = 33 });
//values.Add(new DataModel() { Label = $"Column 4", Value = 44 });
//values.Add(new DataModel() { Label = $"Column 5", Value = 55 });

//for (double value = 0; value < 4; value++)
//{
//    values.Add(new DataModel() { Label = $"Column {value + 1}", Value = value + 10 });
//}

// Create a labels collection from the DataModel items
//ColumnLabels = new ObservableCollection<string>(values.Select(dataModel => dataModel.Label));

// Define a data mapper, which tells the Chart how to extract data from the model
// and how to map it to the corresponding axis. The mapper also allows 
// to define a predicate which will be applied to color each data item (Fill, Stroke)
//CartesianMapper<DataModel> dataMapper = new CartesianMapper<DataModel>()

//  //.X(dataModel => dataModel.Value)
//  //.Y(dataModel => dataModel.Value)
//  .Fill(dataModel => dataModel.Value > 5.0 ? Brushes.Red : Brushes.Green);




//ChartDataSets = new SeriesCollection
//{
//    new ColumnSeries
//    {
//        Values = values,
//        Configuration = dataMapper,
//        DataLabels=true,
//        ColumnPadding = 0,
//        StrokeThickness = 2
//    }
//};


//CoronavirusCountryCaseCounts = new ChartValues<int>(countries.Select(c => c.Cases));

//BucketLabels.Clear();

//foreach (string countryName in countries.Select(c => c.Country))
//{
//    BucketLabels.Add(countryName);
//}







//SineGraphValues = new ChartValues<ObservablePoint>();

//// Plot a sine graph
//for (double x = 0; x <= 360; x++)
//{
//    var point = new ObservablePoint()
//    {
//        X = x,
//        Y = Math.Sin(x * Math.PI / 180)
//    };

//    SineGraphValues.Add(point);
//}


//DataMapper = new CartesianMapper<ObservablePoint>()
//  .X(point => point.X)
//  .Y(point => point.Y)
//  .Stroke(point => point.Y > 0.3 ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.LightGreen)
//  .Fill(point => point.Y > 0.3 ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.LightGreen);


//CoronavirusCountryCaseCounts = new ChartValues<int>()
//{
//    500,1000,1500,2000
//};

//SeriesCollection = new SeriesCollection()
//    {
//        new LineSeries() {  Values = CoronavirusCountryCaseCounts }
//    };



//_random = new Random();
//_ys = new ChartValues<double>();


////for (int i = 0; i < 100; ++i)
////{
////    _ys.Add(_random.NextDouble() * 100);
////}

////SeriesCollection = new SeriesCollection()
////    {
////        new LineSeries() {  Values = _ys }
////    };
//Console.WriteLine(  );



// Initialize the binding source
//this.SineGraphValues = new ChartValues<ObservablePoint>();

//// Plot a sine graph
//for (double x = 0; x <= 360; x++)
//{
//    var point = new ObservablePoint()
//    {
//        X = x,
//        Y = Math.Sin(x * Math.PI / 180)
//    };

//    this.SineGraphValues.Add(point);
//}

//// Setup the data mapper
//this.DataMapper = new CartesianMapper<ObservablePoint>()
//  .X(point => point.X)
//  .Y(point => point.Y)
//  .Stroke(point => point.Y > 0.3 ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.LightGreen)
//  .Fill(point => point.Y > 0.3 ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.LightGreen);

//
//this.BlueSeries = new ColumnSeries()
//{
//    Title = "Population of Bodrum",
//    Values = new ChartValues<double> { 1500, 2500, 3700, 2000, 1000 },
//    Fill = System.Windows.Media.Brushes.Blue
//};
//this.SeriesCollection = new SeriesCollection() { this.BlueSeries };