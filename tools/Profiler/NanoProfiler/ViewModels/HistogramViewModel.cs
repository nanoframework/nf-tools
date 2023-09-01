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

namespace nanoFramework.Tools.NanoProfiler.ViewModels
{
    public partial class HistogramViewModel: ObservableObject
    {
        #region Observable Properties
        [ObservableProperty]
        private SolidColorBrush _histogramBackground = System.Windows.Media.Brushes.Green;

        [ObservableProperty]
        private ChartValues<int> _coronavirusCountryCaseCounts;

        [ObservableProperty]
        private ObservableCollection<string> _bucketLabels = new ObservableCollection<string>();

        #endregion


        #region Properties
        private const int AMOUNT_OF_COUNTRIES = 20;
        private readonly ICoronavirusCountryService _coronavirusCountryService;

        Bucket[] buckets;
        double currentScaleFactor;
        public Histogram _histogram { get; set; }
        private string[] typeName;
        public string _title { get; set; }
        private int _numberOfBuckets = 10;

        #endregion

        public HistogramViewModel(Histogram histogram, string title)
        {
            _histogram = histogram;
            _title = title;
            typeName = _histogram.readNewLog.typeName;
            _coronavirusCountryService = new CoronavirusCountryService();
            Task.Run(() => Load());
        }


        IEnumerable<CoronavirusCountry> countries;
        public async Task Load()
        {
            graphPanel_Paint();


            BucketLabels.Clear();

            countries = await _coronavirusCountryService.GetTopCases(_numberOfBuckets);

            CoronavirusCountryCaseCounts = new ChartValues<int>(countries.Select(c => c.Cases));
            //foreach (string countryName in countries.Select(c => c.Country))
            //{
            //    BucketLabels.Add(countryName);
            //}

            foreach (var bucket in buckets)
            {
                BucketLabels.Add(bucket.label);
            }

        }

        ulong totalSize;
        int totalCount;

        TypeDesc[] typeIndexToTypeDesc;

        ArrayList sortedTypeTable;
        bool initialized = false;


        private void graphPanel_Paint()
        {
            initialized = false;

            if (_histogram == null || typeName == null)
            {
                return;
            }

            //if (e != null)
            //{
                //Graphics g = e.Graphics;

                //bucketWidth = BucketWidth(g);
                //bottomMargin = BottomMargin();

                BuildSizeRangesAndTypeTable(_histogram.typeSizeStacktraceToCount);
                ColorTypes();

                ulong maxTotalSize = 0;
                foreach (Bucket b in buckets)
                {
                    if (maxTotalSize < b.totalSize)
                    {
                        maxTotalSize = b.totalSize;
                    }
                }

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

            SetBucketsLabels();

                DrawBuckets();
                
                initialized = true;
            //}


        }

        private void SetBucketsLabels()
        {
            for (int i = 0; i < buckets.Length; i++)
            {
                string s = "< " + FormatSize((ulong)buckets[i].maxSize + 1) + $"{Environment.NewLine}";
                s += FormatSize(buckets[i].totalSize) + $"{Environment.NewLine}";
                s += string.Format("({0:f2}%)", 100.0 * buckets[i].totalSize / totalSize);

                buckets[i].label = s;
                //BucketLabels.Add(buckets[i].label);
            }


            _numberOfBuckets = buckets.Length;



        }

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
            //Debug.Assert(verticalScale != 0);
            //bool noBucketSelected = true;
            //foreach (Bucket b in buckets)
            //{
            //    if (b.selected)
            //    {
            //        noBucketSelected = false;
            //        break;
            //    }
            //}

            //using (System.Drawing.Brush blackBrush = new SolidBrush(System.Drawing.Color.Black))
            //{
            //    int x = leftMargin;
            //    foreach (Bucket b in buckets)
            //    {
            //        string s = "< " + FormatSize((ulong)b.maxSize + 1);

            //        int y = (int)(graphPanel.Height - bottomMargin);

            //        g.DrawString(s, font, blackBrush, x, y + 3);
            //        s = FormatSize(b.totalSize);
            //        g.DrawString(s, font, blackBrush, x, y + 3 + font.Height);
            //        s = string.Format("({0:f2}%)", 100.0 * b.totalSize / totalSize);
            //        g.DrawString(s, font, blackBrush, x, y + 3 + font.Height * 2);
            //        foreach (KeyValuePair<TypeDesc, SizeCount> d in b.typeDescToSizeCount)
            //        {
            //            TypeDesc t = d.Key;
            //            SizeCount sizeCount = d.Value;
            //            ulong size = sizeCount.size;
            //            int height = (int)(size / (ulong)verticalScale);

            //            y -= height;

            //            System.Drawing.Brush brush = t.brush;
            //            if (t.selected && (b.selected || noBucketSelected))
            //            {
            //                brush = blackBrush;
            //            }

            //            g.FillRectangle(brush, x, y, bucketWidth, height);
            //        }

            //        x += bucketWidth + gap;
            //    }
            //}
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
                typeIndexToTypeDesc = new TypeDesc[_histogram.readNewLog.typeName.Length];
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

                int[] stacktrace = _histogram.readNewLog.stacktraceTable.IndexToStacktrace(i);
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

        void BuildBuckets()
        {
            double scaleFactor = 2.0;

            //TO DO: Combo
            //switch (VerticalScaleOption.SelectedValue?.ToString())
            //{
            //    case "Coarse":
            //        scaleFactor = 2.0;
            //        break;
            //    case "Medium":
            //        scaleFactor = Math.Sqrt(2.0);
            //        break;
            //    case "Fine":
            //        scaleFactor = Math.Pow(2.0, 0.25);
            //        break;
            //    case "Very Fine":
            //        scaleFactor = Math.Pow(2.0, 0.125);
            //        break;
            //}

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


        struct Bucket
        {
            internal int minSize;
            internal int maxSize;
            internal ulong totalSize;
            internal Dictionary<TypeDesc, SizeCount> typeDescToSizeCount;
            internal bool selected;
            internal string label;
        };

        private class SizeCount
        {
            internal ulong size;
            internal int count;
        }

        class TypeDesc : IComparable
        {
            internal string typeName;
            internal ulong totalSize;
            internal int count;
            internal System.Drawing.Color color;
            internal System.Drawing.Brush brush;
            internal System.Drawing.Pen pen;
            internal bool selected;
            internal System.Drawing.Rectangle rect;

            internal TypeDesc(string typeName)
            {
                this.typeName = typeName;
            }

            public int CompareTo(object o)
            {
                TypeDesc t = (TypeDesc)o;
                if (t.totalSize < this.totalSize)
                {
                    return -1;
                }
                else if (t.totalSize > this.totalSize)
                {
                    return 1;
                }
                else
                {
                    return 0;
                }
            }
        }



    }
}
