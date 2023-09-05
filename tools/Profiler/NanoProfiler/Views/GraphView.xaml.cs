using LiveCharts.Wpf;
using LiveCharts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CLRProfiler;
using System.Drawing;
using Pen = System.Drawing.Pen;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Collections;
using Size = System.Windows.Size;
using System.ComponentModel;
using System.Collections.ObjectModel;

namespace nanoFramework.Tools.NanoProfiler.Views
{
    /// <summary>
    /// Interaction logic for GraphView.xaml
    /// </summary>
    public partial class GraphView : Window, INotifyPropertyChanged
    {
        #region Observable Properties
        private string _test = "Test1233";
        public string Test
        {
            get { return _test; }
            set
            {
                _test = value;
                OnPropertyChanged(nameof(Test));
            }
        }
        private List<int> _scaleList;
        public List<int> ScaleList
        {
            get { return _scaleList; }
            set
            {
                _scaleList = value;
                OnPropertyChanged(nameof(ScaleList));
            }
        }
        private List<double> _detailsList;
        public List<double> DetailsList
        {
            get { return _detailsList; }
            set
            {
                _detailsList = value;
                OnPropertyChanged(nameof(DetailsList));
            }
        }
        private double _detailsSelectedValue;
        public double DetailsSelectedValue
        {
            get { return _detailsSelectedValue; }
            set
            {
                _detailsSelectedValue = value;
                OnPropertyChanged(nameof(DetailsSelectedValue));
            }
        }
        private int _scaleSelectedValue;
        public int ScaleSelectedValue
        {
            get { return _scaleSelectedValue; }
            set
            {
                _scaleSelectedValue = value;
                OnPropertyChanged(nameof(ScaleSelectedValue));
            }
        }

        #endregion



        private System.Windows.Forms.Panel outerPanel = new System.Windows.Forms.Panel();
        private Graph _graph;
        private System.Windows.Forms.Panel graphPanel = new System.Windows.Forms.Panel();
        public GraphView(Graph graph)
        {
            InitializeComponent();
            SetComboBoxes();
            DataContext = this;
            font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((System.Byte)(204))); ;
            _graph = graph;
            this.graphPanel.Paint += new System.Windows.Forms.PaintEventHandler(this.graphPanel_Paint);
            windowsFormsHost.Child = graphPanel;
        }

        private void SetComboBoxes()
        {
            ScaleList = new List<int>() { 10, 20, 50, 100, 200, 500, 1000 };
            ScaleSelectedValue = 10;

            DetailsList = new List<double>() { 0.1, 0.2, 0.5, 1, 2,5,10,20 };
            DetailsSelectedValue = 1;
        }

        private bool placeVertices = true;
        private bool placeEdges = true;
        private float scale = 1.0f;
        int totalHeight = 100;
        const int boxWidth = 300;
        int gapWidth = 100;
        float minHeight = 1.0f;
        float minWidth = 1.0f;
        private ulong totalWeight; 
        private ArrayList levelList;
        private Font font;
        private int fontHeight;
        private void graphPanel_Paint(object sender, System.Windows.Forms.PaintEventArgs e)
        {
            EnableDisableMenuItems();

            outerPanel.AutoScroll = true;
            Graphics g = e.Graphics;
            if (placeVertices || placeEdges)
            {
                PlaceVertices(g);
                placeVertices = placeEdges = false;
            }
            using (SolidBrush penBrush = new SolidBrush(System.Drawing.Color.Blue))
            {
                using (Pen pen = new Pen(penBrush, 1))
                {
                    foreach (Vertex v in _graph.vertices.Values)
                    {
                        if (v.visible)
                            PaintVertex(v, g, penBrush, pen);
                    }
                }
            }
            DrawEdges(g, scale);
        }

        void DrawEdges(Graphics g, float scale)
        {
            Random r = new Random(0);
            System.Drawing.Point[] points = new System.Drawing.Point[4];
            foreach (Vertex v in _graph.vertices.Values)
            {
                foreach (Edge e in v.outgoingEdges.Values)
                {
                    if (e.ToVertex != _graph.BottomVertex
                        && !e.fromPoint.IsEmpty && !e.toPoint.IsEmpty)
                    {
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
                        Debug.Assert(brush != null);
                        float fWidth = e.weight * scale;
                        if (fWidth > minWidth && e.FromVertex.active && e.ToVertex.active)
                        {
                            int iWidth = (int)fWidth;
                            if (iWidth < 1)
                                iWidth = 1;
                            e.width = iWidth;
                            Pen pen = e.pen;
                            if (pen == null || pen.Width != iWidth || e.selected)
                            {
                                pen = new Pen(brush, iWidth);
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
                            points[0] = e.fromPoint;
                            points[1] = new System.Drawing.Point(e.fromPoint.X + deltaX, e.fromPoint.Y + deltaY1);
                            points[2] = new System.Drawing.Point(e.toPoint.X - deltaX, e.toPoint.Y + deltaY2);
                            points[3] = e.toPoint;
                            g.DrawCurve(pen, points);
                            red = (red + 17) % 256;
                            green = (green + 101) % 256;
                            blue = (blue + 29) % 256;
                        }
                    }
                }
            }
        }


        void PlaceVertices(Graphics g)
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
                        if (v.weight * scale > minHeight)
                        {
                            int width = (int)g.MeasureString(v.basicName, font).Width;
                            if (maxWidth < width)
                                maxWidth = width;

                            width = (int)g.MeasureString(v.weightString, font).Width;
                            if (maxWidth < width)
                                maxWidth = width;
                        }
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
                        v.rectangle = new System.Drawing.Rectangle(x, y, maxWidth + 5, iHeight);
                        if (fHeight <= minHeight || !v.active)
                        {
                            v.visible = false;
                            v.rectangle = v.selectionRectangle = new System.Drawing.Rectangle(0, 0, 0, 0);
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
                graphPanel.Size = new System.Drawing.Size(x, maxY);
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
                return string.Format(format, w, byteString, weight * 100.0 / totalWeight);
            }
        }

        void PaintVertex(Vertex v, Graphics g, System.Drawing.Brush penBrush, Pen pen)
        {
            System.Drawing.Rectangle r = v.rectangle;
            v.selectionRectangle = r;
            g.DrawRectangle(pen, r);
            if (v.selected)
            {
                using (SolidBrush selectBrush = new SolidBrush(System.Drawing.Color.Aqua))
                    g.FillRectangle(selectBrush, r);
            }

            RectangleF stringRect;
            int lineCount = 2;
            if (v.signature != null)
                lineCount = 3;
            if (r.Height > fontHeight * lineCount)
                stringRect = new RectangleF(r.X, r.Y, r.Width, fontHeight);
            else
            {
                stringRect = new RectangleF(r.X, r.Y + r.Height + 3, r.Width, fontHeight);
                // for these very narrow rectangle, start the selection rectangle 5 pixels above
                // the actual rectangle, so people can hit it more easily. Even though they could click
                // on the text below, which not everybody tries...
                const int vTolerance = 5;
                v.selectionRectangle = new System.Drawing.Rectangle(r.X, r.Y - vTolerance, r.Width, vTolerance + r.Height + 3 + fontHeight * lineCount);
            }

            if (v.weightHistory != null)
            {
                int alpha = 200;
                int previousHeight = r.Height;
                for (int i = 0; i < v.weightHistory.Length; i++)
                {
                    alpha = alpha * 2 / 3;
                    ulong weight = v.weightHistory[i];
                    int height = (int)((float)r.Height / v.weight * weight);
                    if (height < previousHeight)
                    {
                        System.Drawing.Color color = System.Drawing.Color.FromArgb(alpha, System.Drawing.Color.Red);
                        using (System.Drawing.Brush brush = new SolidBrush(color))
                        {
                            g.FillRectangle(brush, r.X, r.Y + height, r.Width, previousHeight - height);
                        }
                    }
                    else
                    {
                        System.Drawing.Color color = System.Drawing.Color.FromArgb(alpha, System.Drawing.Color.Green);
                        using (System.Drawing.Brush brush = new SolidBrush(color))
                        {
                            g.FillRectangle(brush, r.X, r.Y + previousHeight, r.Width, height - previousHeight);
                        }
                    }
                    previousHeight = height;
                }
            }

            g.DrawString(v.basicName, font, penBrush, stringRect);
            stringRect.Y += fontHeight;
            if (v.signature != null)
            {
                g.DrawString(v.basicSignature, font, penBrush, stringRect);
                stringRect.Y += fontHeight;
                int width = (int)g.MeasureString(v.basicSignature, font).Width;
                if (stringRect.Width < width)
                    v.signatureCurtated = true;
            }

            g.DrawString(v.weightString, font, penBrush, stringRect);
        }

        void EnableDisableMenuItems()
        {
            bool isHeapGraph = _graph.graphType == Graph.GraphType.HeapGraph;
            //showWhoAllocatedMenuItem.Enabled = isHeapGraph;
            //showWhoAllocatedNewMenuItem.Enabled = isHeapGraph;
            //showNewObjectsMenuItem.Enabled = isHeapGraph;
            //showObjectsAllocatedBetween.Enabled = isHeapGraph;
            //showWhoAllocatedObjectsBetweenMenuItem.Enabled = isHeapGraph;
            //showInstancesMenuItem.Enabled = isHeapGraph;
            //showHistogramMenuItem.Enabled = isHeapGraph;
            //showReferencesMenuItem.Enabled = isHeapGraph && SelectedVertexCount() != 0;

            //if (isHeapGraph && graph.typeGraphOptions == ObjectGraph.BuildTypeGraphOptions.IndividualObjects)
            //{
            //    showInstancesMenuItem.Text = "Show Groups of Instances";
            //}
            //else
            //{
            //    showInstancesMenuItem.Text = "Show Individual Instances";
            //}

        }



        #region INotify
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }
}
