﻿////
// Copyright (c) .NET Foundation and Contributors.
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
////

using CLRProfiler;
using nanoFramework.Tools.NanoProfiler.Models;
using nanoFramework.Tools.NanoProfiler.ViewModels;
using nanoFramework.Tools.NanoProfiler.Views;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;

namespace nanoFramework.Tools.NanoProfiler.CLRProfiler
{
    /// <summary>
    /// Interaction logic for HistogramViewForm.xaml
    /// </summary>
    public partial class HistogramViewForm : Window
    {
        private System.Windows.Controls.ToolTip toolTip;
        private System.Windows.Forms.Panel graphPanel;
        private Font font;
        ReadLogResult lastLogResult;
        private bool autoUpdate;
        private Histogram histogram;
        private string[] typeName;
        bool initialized = false;

        public HistogramViewForm()
        {
            InitializeComponent();

            toolTip = new System.Windows.Controls.ToolTip();
            //toolTip.Active = false;
            //toolTip.ShowAlways = true;
            //toolTip.AutomaticDelay = 70;
            //toolTip.ReshowDelay = 1;
            graphPanel = new System.Windows.Forms.Panel();
            //graphPanel.Width = 508;
            //graphPanel.Height = 209;
            graphPanel.Paint += graphPanel_Paint;
            //graphPanel.MouseDown += graphPanel_MouseDown;
            windowsFormsHost.Child = graphPanel;
            autoUpdate = true;
            lastLogResult = MainForm.instance.lastLogResult;

            if (lastLogResult != null)
            {
                histogram = lastLogResult.allocatedHistogram;
                typeName = histogram.readNewLog.typeName;
            }

            Title = "Histogram by Size for Allocated Objects";

            font = MainForm.instance.font;
        }

        internal HistogramViewForm(Histogram histogram, string title) : this()
        {
            this.histogram = histogram;
            autoUpdate = false;
            Title = title;

            Dispatcher.Invoke(() =>
            {
                graphPanel_Paint(null, null);
            });

        }


        TypeDesc[] typeIndexToTypeDesc;

        ArrayList sortedTypeTable;

        Bucket[] buckets;
        double currentScaleFactor;

        void BuildBuckets()
        {
            double scaleFactor = 2.0;

            switch (VerticalScaleOption.SelectedValue?.ToString())
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

        //private class SizeCount
        //{
        //    internal ulong size;
        //    internal int count;
        //}

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

        ulong totalSize;
        int totalCount;

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
                var buck = buckets;
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
            var res = buckets;
            TrimEmptyBuckets();

            var res1 = buckets;
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

        static System.Drawing.Color[] firstColors =
        {
            System.Drawing.Color.Red,
            System.Drawing.Color.Yellow,
            System.Drawing.Color.Green,
            System.Drawing.Color.Cyan,
            System.Drawing.Color.Blue,
            System.Drawing.Color.Magenta,
        };

        static System.Drawing.Color[] colors = new System.Drawing.Color[16];

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
                if (t.Selected)
                {
                    return t;
                }
            }

            return null;
        }

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

        int Scale(ItemCollection items, int pixelsAvailable, int rangeNeeded, bool firstTime)
        {
            if (!firstTime)
            {
                foreach (ComboBoxItem comboItem in items)
                {
                    if (comboItem.IsSelected)
                    {
                        return int.Parse(comboItem.Content.ToString());
                    }
                }
            }

            // No radio button was checked - let's come up with a suitable default
            ComboBoxItem maxLowScaleRB = null;
            int maxLowRange = 0;

            ComboBoxItem minHighScaleRB = null;
            int minHighRange = int.MaxValue;

            foreach (ComboBoxItem comboItem in items)
            {
                int range = pixelsAvailable * int.Parse(comboItem.Content.ToString());

                if (range < rangeNeeded)
                {
                    if (maxLowRange < range)
                    {
                        maxLowRange = range;
                        maxLowScaleRB = comboItem;
                    }
                }
                else
                {
                    if (minHighRange > range)
                    {
                        minHighRange = range;
                        minHighScaleRB = comboItem;
                    }
                }
            }

            if (minHighScaleRB != null)
            {
                minHighScaleRB.IsSelected = true;
                return int.Parse(minHighScaleRB.Content.ToString());
            }
            else
            {
                if (maxLowScaleRB != null)
                {
                    maxLowScaleRB.IsSelected = true;
                    return int.Parse(maxLowScaleRB.Content.ToString());
                }

                return 1;
            }
        }

        int verticalScale = 0;

        int VerticalScale(int pixelsAvailable, ulong rangeNeeded, bool firstTime)
        {
            return Scale(VerticalScaleOption.Items, pixelsAvailable, (int)(rangeNeeded / 1024), firstTime) * 1024;
        }

        const int leftMargin = 30;
        int bottomMargin = 50;
        const int gap = 20;
        int bucketWidth = 50;
        const int topMargin = 30;
        const int rightMargin = 30;
        const int minHeight = 400;
        int dotSize = 8;

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

        private void DrawBuckets(Graphics g)
        {
            Debug.Assert(verticalScale != 0);
            bool noBucketSelected = true;
            foreach (Bucket b in buckets)
            {
                if (b.Selected)
                {
                    noBucketSelected = false;
                    break;
                }
            }

            using (System.Drawing.Brush blackBrush = new SolidBrush(System.Drawing.Color.Black))
            {
                int x = leftMargin;
                foreach (Bucket b in buckets)
                {
                    string s = "< " + FormatSize((ulong)b.MaxSize + 1);

                    int y = (int)(graphPanel.Height - bottomMargin);

                    g.DrawString(s, font, blackBrush, x, y + 3);
                    s = FormatSize(b.TotalSize);
                    g.DrawString(s, font, blackBrush, x, y + 3 + font.Height);
                    s = string.Format("({0:f2}%)", 100.0 * b.TotalSize / totalSize);
                    g.DrawString(s, font, blackBrush, x, y + 3 + font.Height * 2);
                    foreach (KeyValuePair<TypeDesc, SizeCount> d in b.TypeDescToSizeCount)
                    {
                        TypeDesc t = d.Key;
                        SizeCount sizeCount = d.Value;
                        ulong size = sizeCount.Size;
                        int height = (int)(size / (ulong)verticalScale);

                        y -= height;

                        System.Drawing.Brush brush = t.Brush;
                        if (t.Selected && (b.Selected || noBucketSelected))
                        {
                            brush = blackBrush;
                        }

                        g.FillRectangle(brush, x, y, bucketWidth, height);
                    }

                    x += bucketWidth + gap;
                }
            }
        }

        const int typeLegendSpacing = 3;

        private void DrawTypeLegend(Graphics g)
        {
            dotSize = (int)g.MeasureString("0", font).Width;
            int maxWidth = 0;
            int x = leftMargin;
            int y = topMargin + font.Height + typeLegendSpacing * 2;
            foreach (TypeDesc t in sortedTypeTable)
            {
                int typeNameWidth = (int)g.MeasureString(t.TypeName, font).Width;
                int sizeWidth = (int)g.MeasureString(" (999,999,999 bytes, 100.00% - 999,999 instances, 999 bytes average size)", font).Width;
                t.Rect = new System.Drawing.Rectangle(x, y, Math.Max(typeNameWidth, sizeWidth) + dotSize * 2, font.Height * 2);
                if (maxWidth < t.Rect.Width)
                {
                    maxWidth = t.Rect.Width;
                }

                y = t.Rect.Bottom + typeLegendSpacing;
            }
            int height = y + bottomMargin;
            //typeLegendPanel.Height = height;

            int width = leftMargin + maxWidth + rightMargin;
            //typeLegendPanel.Width = width;

            x = leftMargin;
            y = topMargin;

            System.Drawing.Brush blackBrush = new SolidBrush(System.Drawing.Color.Black);

            string s = string.Format("Grand total: {0:n0} bytes - {1:n0} instances, {2:n0} bytes average size", totalSize, totalCount, totalSize / (ulong)totalCount);
            g.DrawString(s, font, blackBrush, x, y);

            y += font.Height + typeLegendSpacing * 2;

            int dotOffset = (font.Height - dotSize) / 2;
            foreach (TypeDesc t in sortedTypeTable)
            {
                System.Drawing.Brush brush = t.Brush;
                if (t.Selected)
                {
                    brush = blackBrush;
                }

                g.FillRectangle(brush, t.Rect.Left, t.Rect.Top + dotOffset, dotSize, dotSize);
                g.DrawString(t.TypeName, font, blackBrush, t.Rect.Left + dotSize * 2, t.Rect.Top);
                s = string.Format(" ({0:n0} bytes, {1:f2}% - {2:n0} instances, {3:n0} bytes average size)", t.TotalSize, (double)t.TotalSize / totalSize * 100.0, t.Count, t.TotalSize / (ulong)t.Count);
                g.DrawString(s, font, blackBrush, t.Rect.Left + dotSize * 2, t.Rect.Top + font.Height);
                y = t.Rect.Bottom + typeLegendSpacing;
            }
        }

        private int BucketWidth(Graphics g)
        {
            int width1 = (int)g.MeasureString("< 999.9 sec", font).Width;
            int width2 = (int)g.MeasureString("999 MB", font).Width;
            width1 = Math.Max(width1, width2);
            var res = Math.Max(width1, bucketWidth);
            return Math.Max(width1, bucketWidth);
        }

        private int BottomMargin()
        {
            return font.Height * 3 + 10;
        }

        private void graphPanel_Paint(object sender, System.Windows.Forms.PaintEventArgs e)
        {
            initialized = false;

            if (histogram == null || typeName == null)
            {
                return;
            }

            if (e != null)
            {
                Graphics g = e.Graphics;

                bucketWidth = BucketWidth(g);
                bottomMargin = BottomMargin();
                var res = buckets;

                BuildSizeRangesAndTypeTable(histogram.typeSizeStacktraceToCount);
                ColorTypes();

                ulong maxTotalSize = 0;
                foreach (Bucket b in buckets)
                {
                    if (maxTotalSize < b.TotalSize)
                    {
                        maxTotalSize = b.TotalSize;
                    }
                }

                verticalScale = VerticalScale((int)graphPanel.Height - topMargin - bottomMargin, maxTotalSize, verticalScale == 0);

                int maxBucketHeight = (int)(maxTotalSize / (ulong)verticalScale);
                int height = topMargin + maxBucketHeight + bottomMargin;
                if (height < minHeight)
                {
                    height = minHeight;
                }

                graphPanel.Height = height;

                int width = leftMargin + buckets.Length * bucketWidth + (buckets.Length - 1) * gap + rightMargin;
                graphPanel.Width = width;

                DrawBuckets(g);

                initialized = true;
            }


        }

        private void typeLegendPanel_Paint(object sender, System.Windows.Forms.PaintEventArgs e)
        {
            initialized = false;

            if (histogram == null || typeName == null)
            {
                return;
            }

            BuildSizeRangesAndTypeTable(histogram.typeSizeStacktraceToCount);
            ColorTypes();

            Graphics g = e.Graphics;

            DrawTypeLegend(g);

            initialized = true;
        }

        private void Refresh(object sender, System.EventArgs e)
        {
            // TODO
            //graphPanel.Invalidate();
        }

        private void typeLegendPanel_MouseDown(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if ((e.Button & MouseButtons.Left) != MouseButtons.None)
            {
                for (int i = 0; i < buckets.Length; i++)
                {
                    if (buckets[i].Selected)
                    {
                        // TODO
                        //graphPanel.Invalidate();
                        //typeLegendPanel.Invalidate();
                        buckets[i].Selected = false;
                    }
                }
                if (sortedTypeTable != null)
                {
                    foreach (TypeDesc t in sortedTypeTable)
                    {
                        if (t.Rect.Contains(e.X, e.Y) != t.Selected)
                        {
                            t.Selected = !t.Selected;

                            // TODO
                            //graphPanel.Invalidate();
                            //typeLegendPanel.Invalidate();
                        }
                    }
                }
            }
            else if ((e.Button & MouseButtons.Right) != MouseButtons.None)
            {
                System.Drawing.Point p = new System.Drawing.Point(e.X, e.Y);

                // TODO
                //contextMenu.Show(typeLegendPanel, p);
            }
        }

        private void graphPanel_MouseDown(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            var res1 = sortedTypeTable;     //12
            var res2 = buckets;             //13


            if (!initialized || verticalScale == 0)
            {
                return;
            }

            if ((e.Button & MouseButtons.Left) != MouseButtons.None)
            {
                if (sortedTypeTable != null)
                {
                    foreach (TypeDesc t in sortedTypeTable)
                    {
                        t.Selected = false;
                    }
                }

                int x = leftMargin;
                for (int i = 0; i < buckets.Length; i++)
                {
                    buckets[i].Selected = false;

                    // TODO
                    //    int y = graphPanel.Height - bottomMargin;

                    //    foreach (TypeDesc t in buckets[i].typeDescToSizeCount.Keys)
                    //    {
                    //        SizeCount sizeCount = buckets[i].typeDescToSizeCount[t];
                    //        ulong size = sizeCount.size;
                    //        int height = (int)(size / (ulong)verticalScale);

                    //        y -= height;

                    //        System.Drawing.Rectangle r = new System.Drawing.Rectangle(x, y, bucketWidth, height);
                    //        if (r.Contains(e.X, e.Y))
                    //        {
                    //            t.selected = true;
                    //            buckets[i].selected = true;
                    //        }
                    //    }

                    //    x += bucketWidth + gap;
                }
                //graphPanel.Invalidate();
                //typeLegendPanel.Invalidate();
            }
            else if ((e.Button & MouseButtons.Right) != MouseButtons.None)
            {
                System.Drawing.Point p = new System.Drawing.Point(e.X, e.Y);
                // TODO
                //contextMenu.Show(graphPanel, p);
            }
        }

        //private void graphPanel_MouseMove(object sender, System.Windows.Forms.MouseEventArgs e)
        //{
        //    if (!initialized || verticalScale == 0)
        //    {
        //        return;
        //    }

        //    if (Form.ActiveForm == this)
        //    {
        //        int x = leftMargin;
        //        foreach (Bucket b in buckets)
        //        {
        //            int y = graphPanel.Height - bottomMargin;
        //            foreach (KeyValuePair<TypeDesc, SizeCount> d in b.typeDescToSizeCount)
        //            {
        //                TypeDesc t = d.Key;
        //                SizeCount sizeCount = d.Value;
        //                ulong size = sizeCount.size;
        //                int height = (int)(size / (ulong)verticalScale);

        //                y -= height;

        //                System.Drawing.Rectangle bucketRect = new System.Drawing.Rectangle(x, y, bucketWidth, height);
        //                if (bucketRect.Contains(e.X, e.Y))
        //                {
        //                    string caption = string.Format("{0} {1} ({2:f2}%) - {3:n0} instances, {4} average size", t.typeName, FormatSize(size), 100.0 * size / totalSize, sizeCount.count, FormatSize(sizeCount.size / (ulong)sizeCount.count));
        //                    //toolTip.Active = true;
        //                    //toolTip.SetToolTip(graphPanel, caption);
        //                    return;
        //                }
        //            }
        //            x += bucketWidth + gap;
        //        }
        //    }
        //    //toolTip.Active = false;
        //    //toolTip.SetToolTip(graphPanel, "");
        //}

        //private void versionTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        //{
        //    if (font != MainForm.instance.font)
        //    {
        //        font = MainForm.instance.font;
        //        graphPanel.Invalidate();
        //        typeLegendPanel.Invalidate();
        //    }

        //    ReadLogResult readLogResult = MainForm.instance.lastLogResult;

        //    if (autoUpdate && readLogResult != null && readLogResult.allocatedHistogram != histogram)
        //    {
        //        histogram = readLogResult.allocatedHistogram;
        //        typeName = histogram.readNewLog.typeName;
        //        graphPanel.Invalidate();
        //        typeLegendPanel.Invalidate();
        //    }
        //}

        //private void exportMenuItem_Click(object sender, System.EventArgs e)
        // {
        //exportSaveFileDialog.FileName = "HistogramBySize.csv";
        //exportSaveFileDialog.Filter = "Comma separated files | *.csv";
        //if (exportSaveFileDialog.ShowDialog() == DialogResult.OK)
        //{
        //    StreamWriter w = new StreamWriter(exportSaveFileDialog.FileName);

        //    TypeDesc selectedType = FindSelectedType();

        //    string title = "Histogram by Size";
        //    if (selectedType != null)
        //    {
        //        title += " of " + selectedType.typeName + " objects";
        //    }

        //    w.WriteLine(title);
        //    w.WriteLine();

        //    w.WriteLine("{0},{1},{2},{3},{4}", "Min Size", "Max Size", "# Instances", "Total Size", "Type");

        //    bool noBucketSelected = true;
        //    int minSize = 0;
        //    int maxSize = int.MaxValue;
        //    foreach (Bucket b in buckets)
        //    {
        //        if (b.selected)
        //        {
        //            noBucketSelected = false;
        //            minSize = b.minSize;
        //            maxSize = b.maxSize;
        //        }
        //    }
        //    foreach (Bucket b in buckets)
        //    {
        //        if (noBucketSelected || b.selected)
        //        {
        //            foreach (KeyValuePair<TypeDesc, SizeCount> d in b.typeDescToSizeCount)
        //            {
        //                TypeDesc t = d.Key;
        //                SizeCount sizeCount = d.Value;

        //                if (selectedType == null || t == selectedType)
        //                {
        //                    w.WriteLine("{0},{1},{2},{3},{4}", b.minSize, b.maxSize, sizeCount.count, sizeCount.size, t.typeName);
        //                }
        //            }
        //        }
        //    }

        //    w.WriteLine();
        //    w.WriteLine();
        //    w.WriteLine("Raw data:");
        //    w.WriteLine();

        //    w.WriteLine("{0},{1},{2},{3}", "Instance Size", "# Instances", "Total Size", "Type");
        //    for (int i = 0; i < histogram.typeSizeStacktraceToCount.Length; i++)
        //    {
        //        int count = histogram.typeSizeStacktraceToCount[i];
        //        if (count == 0)
        //        {
        //            continue;
        //        }

        //        int[] stacktrace = histogram.readNewLog.stacktraceTable.IndexToStacktrace(i);
        //        int typeIndex = stacktrace[0];
        //        int size = stacktrace[1];

        //        if (minSize <= size && size <= maxSize)
        //        {
        //            TypeDesc t = (TypeDesc)typeIndexToTypeDesc[typeIndex];

        //            if (selectedType == null || t == selectedType)
        //            {
        //                w.WriteLine("{0},{1},{2},{3}", size, count, size * count, t.typeName);
        //            }
        //        }
        //    }

        //    w.Close();
        //}
        //   }

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
            //WinForms.CLRProfiler.GraphViewForm graphViewForm = new WinForms.CLRProfiler.GraphViewForm(graph, title);
            //graphViewForm.Show();

            GraphViewModel viewModel = new GraphViewModel(graph);
            GraphView graphView = new GraphView();
            graphView.DataContext = viewModel;
            graphView.Show();

            //GraphViewForm graphViewForm = new GraphViewForm(graph, title);
            //graphViewForm.Show();
        }
    }
}
