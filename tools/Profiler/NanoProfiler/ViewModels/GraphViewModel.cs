using CLRProfiler;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveCharts;
using LiveCharts.Configurations;
using LiveCharts.Wpf;
using nanoFramework.Tools.NanoProfiler.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using WinForms.CLRProfiler;

namespace nanoFramework.Tools.NanoProfiler.ViewModels
{
    public partial class GraphViewModel: ObservableObject
    {

        #region Commands
        [RelayCommand]
        private void ShowWhoAllocated()
        {
            //showWhoAllocatedMenuItem_Click(null, null);

            //GraphViewModel viewModel = new GraphViewModel();
            //GraphView histogramView = new GraphView();
            //histogramView.DataContext = viewModel;
            //histogramView.Show();
        }

        #endregion


        #region Observable properties

        [ObservableProperty]
        private ChartValues<GraphDataModel> _graphValues = new ChartValues<GraphDataModel>();

        [ObservableProperty]
        private ObservableCollection<string> _graphLabels = new ObservableCollection<string>();
        [ObservableProperty]
        private CartesianMapper<GraphDataModel> _graphConfiguration;

        [ObservableProperty]
        private SeriesCollection _pieSeriesCollection = new SeriesCollection();


        [ObservableProperty]
        private ObservableCollection<int> _scaleList;

        [ObservableProperty]
        private int _scaleSelectedValue;

        [ObservableProperty]
        private ObservableCollection<double> _detailsList;

        [ObservableProperty]
        private double _detailsSelectedValue;

        #endregion



        #region Properties

        private readonly Graph _graph;


        public GraphViewForm graphViewForm { get; set; }
        #endregion

        public Func<ChartPoint, string> PointLabel { get; set; }

        #region Constructor
        public GraphViewModel(Graph graph)
        {
            _graph = graph;

            font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((System.Byte)(204))); ;
            SetComboValues();
            GetValues();
            SetPieValues();

        }

        private void SetPieValues()
        {
            var configuration = new CartesianMapper<GraphDataModel>()
              .X((value, index) => index)
              .Y((value, index) => value.GraphValue)
              .Fill(value => value.GraphColor)
              .Stroke(value => value.GraphColor);


            foreach (var item in values)
            {
                PieSeriesCollection.Add(new PieSeries
                {
                    Values = new ChartValues<GraphDataModel>
                    {
                        item
                    },
                    Configuration = configuration,
                    DataLabels = true,
                    LabelPoint = (value => $"{item.GraphValue}%"),
                    Title = item.Title
                });
            }
        }

        private Font font;
        private float scale = 1.0f; 
        private bool placeVertices = true;
        private bool placeEdges = true;
        ChartValues<GraphDataModel> values = new ChartValues<GraphDataModel>();

        private void GetValues()
        {

            //EnableDisableMenuItems();

            //outerPanel.AutoScroll = true;
            //Graphics g = e.Graphics;
            if (placeVertices || placeEdges)
            {
                PlaceVertices();
                placeVertices = placeEdges = false;
            }
            //using (SolidBrush penBrush = new SolidBrush(Color.Blue))
            //{
            //    using (Pen pen = new Pen(penBrush, 1))
            //    {
            //        foreach (Vertex v in graph.vertices.Values)
            //        {
            //            if (v.visible)
            //                PaintVertex(v, g, penBrush, pen);
            //        }
            //    }
            //}

            string pattern = @"\((\d+\.\d+)%\)";

            Random r = new Random(0);
            foreach (Vertex v in _graph.vertices.Values)
            {
                foreach (Edge e in v.outgoingEdges.Values)
                {
                    if (e.ToVertex != _graph.BottomVertex
                        && !e.fromPoint.IsEmpty && !e.toPoint.IsEmpty)
                    {
                        //" 4.2 kB    (2.31%)"
                        double valueArrived = -1d;
                        Match match = Regex.Match(e.ToVertex.weightString, pattern);
                        if (match.Success)
                        {
                            string percentageValue = match.Groups[1].Value;

                            // Convert the extracted string to a double
                            if (double.TryParse(percentageValue, out double result))
                            {
                                valueArrived = result;
                                Console.WriteLine(result); // This will print "2.31"
                            }
                            else
                            {
                                Console.WriteLine("Invalid percentage value");
                            }
                        }
                        else
                        {
                            Console.WriteLine("No percentage value found in the input string");
                        }


                        


                        int colorInt = r.Next(255 * 256 * 256);
                        int red = (colorInt >> 16) & 255;
                        int green = (colorInt >> 8) & 255;
                        int blue = colorInt & 255;
                        System.Drawing.Brush brush = null;
                        if (e.selected)
                        {
                            System.Drawing.Color foreColor = System.Drawing.Color.FromArgb(150, 0, 255, 0);
                            System.Drawing.Color backColor = System.Drawing.Color.FromArgb(150, 255, 0, 0);
                            brush = new HatchBrush(HatchStyle.DiagonalCross, foreColor, backColor);
                        }
                        else if (e.brush != null)
                        {
                            brush = e.brush;
                        }
                        else
                        {
                            if (red <= green && red <= blue)
                                red = 0;
                            else if (green <= blue && green <= red)
                                green = 0;
                            else if (blue <= red && blue <= green)
                                blue = 0;
                            System.Drawing.Color color = System.Drawing.Color.FromArgb(100, red, green, blue);
                            Debug.Assert(!color.IsEmpty);
                            brush = new SolidBrush(color);
                            e.brush = brush;
                        }
                        System.Drawing.Color drawingColor = ((SolidBrush)brush).Color;
                        System.Windows.Media.Color wpfColor = System.Windows.Media.Color.FromArgb(drawingColor.A, drawingColor.R, drawingColor.G, drawingColor.B);
                        
                        
                        values.Add(new GraphDataModel()
                        {
                            GraphValue = valueArrived,
                            GraphColor = new SolidColorBrush(wpfColor),
                            Title = e.ToVertex.name
                        });


                        Debug.Assert(brush != null);
                        float fWidth = e.weight * scale;
                        if (fWidth > minWidth && e.FromVertex.active && e.ToVertex.active)
                        {
                            int iWidth = (int)fWidth;
                            if (iWidth < 1)
                                iWidth = 1;
                            e.width = iWidth;
                            System.Drawing.Pen pen = e.pen;
                            if (pen == null || pen.Width != iWidth || e.selected)
                            {
                                pen = new System.Drawing.Pen(brush, iWidth);
                                if (!e.selected)
                                    e.pen = pen;
                            }
                            Debug.Assert(pen != null);
                            int deltaX = e.toPoint.X - e.fromPoint.X;
                            int deltaY = e.toPoint.Y - e.fromPoint.Y;
                            deltaX = deltaX / 4;
                            deltaY = deltaY / 9;
                            int deltaY1 = deltaY;
                            int deltaY2 = -deltaY;
                            if (deltaX < 0)
                            {
                                deltaX = 20;
                                if (Math.Abs(deltaY) * 5 < iWidth * 2)
                                {
                                    deltaY1 = deltaY2 = iWidth * 2;
                                    deltaX = iWidth;
                                }
                            }
                            //points[0] = e.fromPoint;
                            //points[1] = new Point(e.fromPoint.X + deltaX, e.fromPoint.Y + deltaY1);
                            //points[2] = new Point(e.toPoint.X - deltaX, e.toPoint.Y + deltaY2);
                            //points[3] = e.toPoint;
                            //g.DrawCurve(pen, points);
                            red = (red + 17) % 256;
                            green = (green + 101) % 256;
                            blue = (blue + 29) % 256;
                        }
                    }
                }
            }
        }
        private int fontHeight;

        private ArrayList levelList;
        private ulong totalWeight;
        int totalHeight = 100;
        const int boxWidth = 300;
        int gapWidth = 100;
        float minHeight = 0.1f;
        float minWidth = 0.1f;
        void PlaceVertices()
        {
            _graph.AssignLevelsToVertices();
            totalWeight = 0;
            foreach (Vertex v in _graph.vertices.Values)
            {
                v.weight = v.incomingWeight;
                if (v.weight < v.outgoingWeight)
                    v.weight = v.outgoingWeight;
                if (_graph.graphType == Graph.GraphType.CallGraph)
                {
                    if (totalWeight < v.weight)
                        totalWeight = v.weight;
                }
            }
            if (_graph.graphType != Graph.GraphType.CallGraph)
                totalWeight = _graph.TopVertex.weight;
            if (totalWeight == 0)
            {
                totalWeight = 1;
            }

            ArrayList al = levelList = BuildLevels(_graph);
            scale = (float)totalHeight / totalWeight;
            if (placeVertices)
            {
                int x = 10;
                int maxY = 0;
                for (int level = _graph.TopVertex.level;
                    level <= _graph.BottomVertex.level;
                    level++)
                {
                    ArrayList all = (ArrayList)al[level];
                    int drawnVertexCount = 0;
                    int maxWidth = 0;
                    foreach (Vertex v in all)
                    {
                        if (_graph.graphType == Graph.GraphType.CallGraph)
                        {
                            if (v.incomingWeight > v.outgoingWeight)
                                v.basicWeight = v.incomingWeight - v.outgoingWeight;
                            else
                                v.basicWeight = 0;
                            v.weightString = string.Format("Gets {0}, causes {1}",
                                formatWeight(v.basicWeight),
                                formatWeight(v.outgoingWeight));
                        }
                        else if (_graph.graphType == Graph.GraphType.ReferenceGraph)
                        {
                            if (v.weight == 1)
                                v.weightString = "1 reference";
                            else
                                v.weightString = string.Format("{0} references", v.weight);
                            if (v.count > 0)
                            {
                                if (v.count == 1)
                                    v.weightString += " (1 object)";
                                else
                                    v.weightString += string.Format(" ({0} objects)", v.count);
                            }
                        }
                        else
                        {
                            if (v.count == 0)
                                v.weightString = formatWeight(v.weight);
                            else if (v.count == 1)
                                v.weightString = string.Format("{0}  (1 object, {1})", formatWeight(v.weight), formatWeight(v.basicWeight));
                            else
                                v.weightString = string.Format("{0}  ({1} objects, {2})", formatWeight(v.weight), v.count, formatWeight(v.basicWeight));
                        }
                        //if (v.weight * scale > minHeight)
                        //{
                        //    int width = (int)g.MeasureString(v.basicName, font).Width;
                        //    if (maxWidth < width)
                        //        maxWidth = width;

                        //    width = (int)g.MeasureString(v.weightString, font).Width;
                        //    if (maxWidth < width)
                        //        maxWidth = width;
                        //}
                    }
                    int y = 10;
                    ulong levelWeight = 0;
                    foreach (Vertex v in all)
                        levelWeight += v.weight;
                    float levelHeight = levelWeight * scale;
                    if (levelHeight < totalHeight * 0.5)
                        y += (int)((totalHeight - levelHeight) * 2);
                    foreach (Vertex v in all)
                    {
                        // For the in-between vertices, sometimes it's good
                        // to shift them down a little to line them up with
                        // whatever is going into them. Unless of course
                        // we would need to shift too much...
                        if (v.level < _graph.BottomVertex.level - 1)
                        {
                            ulong highestWeight = 0;
                            int bestY = 0;
                            foreach (Edge e in v.incomingEdges.Values)
                            {
                                if (e.weight > highestWeight && e.FromVertex.level < level)
                                {
                                    highestWeight = e.weight;
                                    bestY = e.fromPoint.Y - (int)(e.weight * scale * 0.5);
                                }
                            }
                            if (y < bestY && bestY < totalHeight * 5)
                                y = bestY;
                        }
                        float fHeight = v.weight * scale;
                        int iHeight = (int)fHeight;
                        if (iHeight < 1)
                            iHeight = 1;
                        v.rectangle = new Rectangle(x, y, maxWidth + 5, iHeight);
                        if (fHeight <= minHeight || !v.active)
                        {
                            v.visible = false;
                            v.rectangle = v.selectionRectangle = new Rectangle(0, 0, 0, 0);
                        }
                        else
                        {
                            v.visible = true;
                            y += iHeight;
                            int lines = 2;
                            if (v.signature != null)
                                lines = 3;
                            if (iHeight <= fontHeight * lines)
                                y += fontHeight * lines + 3;
                            y += 30;
                            drawnVertexCount++;
                        }
                    }
                    if (drawnVertexCount > 0)
                    {
                        x += maxWidth + gapWidth;
                        if (maxY < y)
                            maxY = y;
                    }
                }
                //if (x < Size.Width)
                //    x = Size.Width;
                //if (maxY < Size.Height)
                //    maxY = Size.Height;
                //graphPanel.Size = new System.Drawing.Size(x, maxY);
            }
            if (placeEdges)
                PlaceEdges(scale);
        }
        void PlaceEdges(float scale)
        {
            foreach (Vertex v in _graph.vertices.Values)
            {
                PlaceEdges(v.incomingEdges.Values, true, v.rectangle.X, v.rectangle.Y, scale);
                int y = v.rectangle.Y + (int)(v.basicWeight * scale);
                PlaceEdges(v.outgoingEdges.Values, false, v.rectangle.X + v.rectangle.Width, y, scale);
            }
        }
        void PlaceEdges(ICollection edgeCollection, bool isIncoming, int x, int y, float scale)
        {
            ArrayList edgeList = new ArrayList(edgeCollection);
            edgeList.Sort();
            float f_y = y;
            foreach (Edge e in edgeList)
            {
                float fwidth = e.weight * scale;
                System.Drawing.Point p = new System.Drawing.Point(x, (int)(f_y + fwidth / 2));
                if (isIncoming)
                {
                    e.toPoint = p;
                }
                else
                {
                    e.fromPoint = p;
                }
                f_y += fwidth;
            }
        }

        string formatWeight(ulong weight)
        {
            if (_graph.graphType == Graph.GraphType.CallGraph)
            {
                if (weight == 1)
                    return "1 call";
                else
                    return string.Format("{0:n0} calls", weight);
            }
            if (_graph.graphType == Graph.GraphType.AssemblyGraph)
            {
                if (weight == 1)
                {
                    return "1 assembly";
                }
                else
                {
                    return weight + " assemblies";
                }
            }
            else if (_graph.graphType == Graph.GraphType.HandleAllocationGraph)
            {
                if (weight == 1)
                    return "1 handle";
                else
                    return string.Format("{0:n0} handles", weight);
            }
            else
            {
                double w = weight;
                string byteString = "bytes";
                if (w >= 1024)
                {
                    w /= 1024;
                    byteString = "kB   ";
                }
                if (w >= 1024)
                {
                    w /= 1024;
                    byteString = "MB   ";
                }
                if (w >= 1024)
                {
                    w /= 1024;
                    byteString = "GB   ";
                }
                string format = "{0,4:f0} {1} ({2:f2}%)";
                if (w < 10)
                    format = "{0,4:f1} {1} ({2:f2}%)";
                var res = string.Format(format, w, byteString, weight * 100.0 / totalWeight);
                return string.Format(format, w, byteString, weight * 100.0 / totalWeight);
            }
        }
        ArrayList BuildLevels(Graph g)
        {
            ArrayList al = new ArrayList();
            for (int level = 0; level <= g.BottomVertex.level; level++)
            {
                al.Add(new ArrayList());
            }
            foreach (Vertex v in g.vertices.Values)
            {
                if (v.level <= g.BottomVertex.level)
                {
                    ArrayList all = (ArrayList)al[v.level];
                    all.Add(v);
                }
                else
                {
                    Debug.Assert(v.level == int.MaxValue);
                }
            }
            foreach (ArrayList all in al)
            {
                all.Sort();
            }
            return al;
        }

        #endregion

        #region Funcs
        private void SetComboValues()
        {
            ScaleList = new ObservableCollection<int>() { 10, 20, 50, 100, 200, 500, 1000 };
            ScaleSelectedValue = 100;

            DetailsList = new ObservableCollection<double>() { 0.1, 0.2, 0.5, 1, 2, 5, 10, 20 };
            DetailsSelectedValue = 1;
        }
        #endregion


    }
}
