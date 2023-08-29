using CLRProfiler;
using DevExpress.Xpf.Core;
using DevExpress.Xpf.Core.ReflectionExtensions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using Clipboard = System.Windows.Forms.Clipboard;
using Control = System.Windows.Forms.Control;
using MessageBox = System.Windows.Forms.MessageBox;
using Point = System.Drawing.Point;
using RadioButton = System.Windows.Controls.RadioButton;
using ToolTip = System.Windows.Forms.ToolTip;

namespace nanoFramework.Tools.NanoProfiler.CLRProfiler
{
    /// <summary>
    /// Interaction logic for HistogramViewForm.xaml
    /// </summary>
    public partial class GraphViewForm : Window
    {
        private ToolTip toolTip;
        private Graph graph;
        private Font font;
        private int fontHeight;
        private float scale = 1.0f;
        private bool placeVertices = true;
        private bool placeEdges = true;
        private Point lastMouseDownPoint;
        private Point lastMousePoint;
        private ArrayList levelList;
        private ulong totalWeight;
        private Vertex selectedVertex;
        private FilterForm filterForm;
        //private FindRoutineForm findForm;

        public GraphViewForm(Graph graph, string title)
        {
            InitializeComponent();

            toolTip = new ToolTip();
            toolTip.Active = false;
            toolTip.ShowAlways = true;
            toolTip.AutomaticDelay = 70;
            toolTip.ReshowDelay = 1;
            this.graph = graph;

            font = MainForm.instance.font;
            fontHeight = font.Height;

            Title = title;

            EnableDisableMenuItems();

            filterForm = new FilterForm();
            //findForm = new FindRoutineForm();
        }

        void EnableDisableMenuItems()
        {
            bool isHeapGraph = graph.graphType == Graph.GraphType.HeapGraph;
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

        void PaintVertex(Vertex v, Graphics g, Brush penBrush, Pen pen)
        {
            Rectangle r = v.rectangle;
            v.selectionRectangle = r;
            g.DrawRectangle(pen, r);
            if (v.selected)
            {
                using (SolidBrush selectBrush = new SolidBrush(Color.Aqua))
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
                v.selectionRectangle = new Rectangle(r.X, r.Y - vTolerance, r.Width, vTolerance + r.Height + 3 + fontHeight * lineCount);
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
                        Color color = Color.FromArgb(alpha, Color.Red);
                        using (Brush brush = new SolidBrush(color))
                        {
                            g.FillRectangle(brush, r.X, r.Y + height, r.Width, previousHeight - height);
                        }
                    }
                    else
                    {
                        Color color = Color.FromArgb(alpha, Color.Green);
                        using (Brush brush = new SolidBrush(color))
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

        void PlaceEdges(ICollection edgeCollection, bool isIncoming, int x, int y, float scale)
        {
            ArrayList edgeList = new ArrayList(edgeCollection);
            edgeList.Sort();
            float f_y = y;
            foreach (Edge e in edgeList)
            {
                float fwidth = e.weight * scale;
                Point p = new Point(x, (int)(f_y + fwidth / 2));
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

        void PlaceEdges(float scale)
        {
            foreach (Vertex v in graph.vertices.Values)
            {
                PlaceEdges(v.incomingEdges.Values, true, v.rectangle.X, v.rectangle.Y, scale);
                int y = v.rectangle.Y + (int)(v.basicWeight * scale);
                PlaceEdges(v.outgoingEdges.Values, false, v.rectangle.X + v.rectangle.Width, y, scale);
            }
        }

        int totalHeight = 100;
        const int boxWidth = 300;
        int gapWidth = 100;
        float minHeight = 1.0f;
        float minWidth = 1.0f;

        void DrawEdges(Graphics g, float scale)
        {
            Random r = new Random(0);
            Point[] points = new Point[4];
            foreach (Vertex v in graph.vertices.Values)
            {
                foreach (Edge e in v.outgoingEdges.Values)
                {
                    if (e.ToVertex != graph.BottomVertex
                        && !e.fromPoint.IsEmpty && !e.toPoint.IsEmpty)
                    {
                        int colorInt = r.Next(255 * 256 * 256);
                        int red = (colorInt >> 16) & 255;
                        int green = (colorInt >> 8) & 255;
                        int blue = colorInt & 255;
                        Brush brush = null;
                        if (e.selected)
                        {
                            Color foreColor = Color.FromArgb(150, 0, 255, 0);
                            Color backColor = Color.FromArgb(150, 255, 0, 0);
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
                            Color color = Color.FromArgb(100, red, green, blue);
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
                            points[1] = new Point(e.fromPoint.X + deltaX, e.fromPoint.Y + deltaY1);
                            points[2] = new Point(e.toPoint.X - deltaX, e.toPoint.Y + deltaY2);
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

        string formatWeight(ulong weight)
        {
            if (graph.graphType == Graph.GraphType.CallGraph)
            {
                if (weight == 1)
                    return "1 call";
                else
                    return string.Format("{0:n0} calls", weight);
            }
            if (graph.graphType == Graph.GraphType.AssemblyGraph)
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
            else if (graph.graphType == Graph.GraphType.HandleAllocationGraph)
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

        void PlaceVertices(Graphics g)
        {
            graph.AssignLevelsToVertices();
            totalWeight = 0;
            foreach (Vertex v in graph.vertices.Values)
            {
                v.weight = v.incomingWeight;
                if (v.weight < v.outgoingWeight)
                    v.weight = v.outgoingWeight;
                if (graph.graphType == Graph.GraphType.CallGraph)
                {
                    if (totalWeight < v.weight)
                        totalWeight = v.weight;
                }
            }
            if (graph.graphType != Graph.GraphType.CallGraph)
                totalWeight = graph.TopVertex.weight;
            if (totalWeight == 0)
            {
                totalWeight = 1;
            }

            ArrayList al = levelList = BuildLevels(graph);
            scale = (float)totalHeight / totalWeight;
            if (placeVertices)
            {
                int x = 10;
                int maxY = 0;
                for (int level = graph.TopVertex.level;
                    level <= graph.BottomVertex.level;
                    level++)
                {
                    ArrayList all = (ArrayList)al[level];
                    int drawnVertexCount = 0;
                    int maxWidth = 0;
                    foreach (Vertex v in all)
                    {
                        if (graph.graphType == Graph.GraphType.CallGraph)
                        {
                            if (v.incomingWeight > v.outgoingWeight)
                                v.basicWeight = v.incomingWeight - v.outgoingWeight;
                            else
                                v.basicWeight = 0;
                            v.weightString = string.Format("Gets {0}, causes {1}",
                                formatWeight(v.basicWeight),
                                formatWeight(v.outgoingWeight));
                        }
                        else if (graph.graphType == Graph.GraphType.ReferenceGraph)
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
                        if (v.level < graph.BottomVertex.level - 1)
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

        private void graphPanel_Paint(object sender, System.Windows.Forms.PaintEventArgs e)
        {
            EnableDisableMenuItems();

            //outerPanel.AutoScroll = true;
            Graphics g = e.Graphics;
            if (placeVertices || placeEdges)
            {
                PlaceVertices(g);
                placeVertices = placeEdges = false;
            }
            using (SolidBrush penBrush = new SolidBrush(Color.Blue))
            {
                using (Pen pen = new Pen(penBrush, 1))
                {
                    foreach (Vertex v in graph.vertices.Values)
                    {
                        if (v.visible)
                            PaintVertex(v, g, penBrush, pen);
                    }
                }
            }
            DrawEdges(g, scale);
        }

        private void scaleRadioButton_Click(object sender, System.EventArgs e)
        {
            RadioButton clickedRadioButton = (RadioButton)sender;
            string scaleString = clickedRadioButton.Content.ToString().Split(' ')[0];
            totalHeight = gapWidth = Convert.ToInt32(scaleString);
            placeVertices = placeEdges = true;
            graphPanel.InvalidateVisual();
        }

        private void detailRadioButton_Click(object sender, System.EventArgs e)
        {
            RadioButton clickedRadioButton = (RadioButton)sender;
            string detailString = clickedRadioButton.Content.ToString().Split(' ')[0];
            minWidth = minHeight = Convert.ToSingle(detailString, CultureInfo.InvariantCulture);
            placeVertices = placeEdges = true;
            graphPanel.InvalidateVisual();
        }

        private void selectVertex(Vertex v, bool nowSelected)
        {
            v.selected = nowSelected;
            graphPanel.InvalidateVisual();
        }

        private void selectEdges()
        {
            foreach (Vertex v in graph.vertices.Values)
                foreach (Edge e in v.outgoingEdges.Values)
                    e.selected = false;

            foreach (Vertex v in graph.vertices.Values)
            {
                if (v.selected)
                {
                    foreach (Edge e in v.outgoingEdges.Values)
                        e.selected = true;
                    foreach (Edge e in v.incomingEdges.Values)
                        e.selected = true;
                }
            }
        }

        private void selectLevel(Vertex v)
        {
            // Simple semantics for now - select/delect whole level
            bool nowSelected = !v.selected;
            foreach (Vertex vv in graph.vertices.Values)
            {
                if (vv.level == v.level)
                {
                    if (nowSelected != vv.selected)
                        selectVertex(vv, nowSelected);
                }
            }
        }

        private void selectIncoming(Vertex v)
        {
            if (v.selected)
                return;
            v.selected = true;
            foreach (Edge e in v.incomingEdges.Values)
            {
                e.selected = true;
                selectIncoming(e.FromVertex);
            }
        }

        private void selectOutgoing(Vertex v)
        {
            if (v.selected)
                return;
            v.selected = true;
            foreach (Edge e in v.outgoingEdges.Values)
            {
                e.selected = true;
                selectOutgoing(e.ToVertex);
            }
        }

        private void selectRecursive(Vertex v)
        {
            foreach (Vertex vv in graph.vertices.Values)
            {
                vv.selected = false;
                foreach (Edge e in vv.incomingEdges.Values)
                    e.selected = false;
                foreach (Edge e in vv.outgoingEdges.Values)
                    e.selected = false;
            }

            v.selected = true;

            foreach (Edge e in v.incomingEdges.Values)
            {
                e.selected = true;
                selectIncoming(e.FromVertex);
            }
            foreach (Edge e in v.outgoingEdges.Values)
            {
                e.selected = true;
                selectOutgoing(e.ToVertex);
            }
        }

        private void graphPanel_MouseDown(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            Point p = new Point(e.X, e.Y);
            lastMouseDownPoint = p;
            if ((e.Button & MouseButtons.Left) != 0)
            {
                foreach (Vertex v in graph.vertices.Values)
                {
                    bool nowSelected;
                    if ((Control.ModifierKeys & Keys.Control) != 0)
                    {
                        if ((Control.ModifierKeys & Keys.Shift) != 0)
                        {
                            if (v.selectionRectangle.Contains(p))
                            {
                                selectRecursive(v);
                                graphPanel.InvalidateVisual();
                                return;
                            }
                            else
                                continue;
                        }
                        else
                            nowSelected = v.selected != v.selectionRectangle.Contains(p);
                    }
                    else if ((Control.ModifierKeys & Keys.Shift) != 0)
                    {
                        if (v.selectionRectangle.Contains(p))
                        {
                            selectLevel(v);
                            break;
                        }
                        else
                            nowSelected = v.selected;
                    }
                    else
                        nowSelected = v.selectionRectangle.Contains(p);
                    if (nowSelected != v.selected)
                        selectVertex(v, nowSelected);
                }
                selectEdges();
            }
            else if ((e.Button & MouseButtons.Right) != 0)
            {
                selectedVertex = null;
                foreach (Vertex v in graph.vertices.Values)
                {
                    if (v.selectionRectangle.Contains(p))
                    {
                        selectedVertex = v;
                        break;
                    }
                }
                //contextMenu.Show(graphPanel, p);
            }
        }

        private void graphPanel_MouseMove(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            Point p = new Point(e.X, e.Y);
            if (distance(p, lastMousePoint) >= 10)
            {
                toolTip.Active = false;
            }
            lastMousePoint = p;
            if ((e.Button & MouseButtons.Left) != 0)
            {
                foreach (Vertex v in graph.vertices.Values)
                {
                    if (v.selected)
                    {
                        v.rectangle.X += p.X - lastMouseDownPoint.X;
                        v.rectangle.Y += p.Y - lastMouseDownPoint.Y;
                        lastMouseDownPoint = p;
                        placeEdges = true;
                        graphPanel.InvalidateVisual();
                    }
                }
            }
            else if (e.Button == MouseButtons.None)
            {
                //if (Form.ActiveForm != this)
                //{
                //    toolTip.Active = false;
                //    return;
                //}
                foreach (Vertex v in graph.vertices.Values)
                {
                    if (v.selectionRectangle.Contains(lastMousePoint))
                    {
                        string caption;
                        caption = v.name;
                        if (v.signature != null)
                            caption += Environment.NewLine + v.signature;
                        if (v.moduleName != null)
                            caption += Environment.NewLine + v.moduleName;
                        toolTip.Active = true;
                        //toolTip.SetToolTip(graphPanel, caption);
                        break;
                    }
                    foreach (Edge edge in v.incomingEdges.Values)
                    {
                        if (distance(lastMousePoint, edge.toPoint) < edge.width)
                        {
                            edgePopup(edge, false);
                            return;
                        }
                    }

                    foreach (Edge edge in v.outgoingEdges.Values)
                    {
                        if (distance(lastMousePoint, edge.fromPoint) < edge.width)
                        {
                            edgePopup(edge, true);
                            return;
                        }
                    }
                }
            }
        }

        private bool NonEmptyWeightHistory(Vertex v)
        {
            if (v.weightHistory == null)
                return false;
            for (int i = 0; i < v.weightHistory.Length; i++)
                if (v.weightHistory[i] != 0)
                    return true;
            return false;
        }

        private void AppendVertexHeader(StringBuilder sb, Vertex v, int indent)
        {
            for (int i = 0; i < indent; i++)
                sb.Append(" ");
            string signature = v.signature != null ? v.signature : "";
            sb.AppendFormat("{0} {1}:\t{2}\r\n", v.name, signature, v.weightString);
            if (NonEmptyWeightHistory(v))
            {
                for (int i = 0; i < indent; i++)
                    sb.Append(" ");
                sb.Append("  Previous allocations (newest to oldest): ");
                for (int i = 0; i < v.weightHistory.Length; i++)
                {
                    sb.Append(formatWeight(v.weightHistory[i]));
                    sb.Append(",  ");
                }
                sb.Append("\r\n");
            }
        }

        private int SelectedVertexCount(out Vertex selectedVertex)
        {
            int selectedCount = 0;
            selectedVertex = null;
            foreach (Vertex v in graph.vertices.Values)
            {
                if (v.selected)
                {
                    selectedCount++;
                    selectedVertex = v;
                }
            }
            return selectedCount;
        }

        private int SelectedVertexCount()
        {
            Vertex selectedVertex;
            return SelectedVertexCount(out selectedVertex);
        }

        private void copyMenuItem_Click(object sender, System.EventArgs e)
        {
            StringBuilder sb = new StringBuilder();
            Vertex selectedVertex;
            int selectedVertexCount = SelectedVertexCount(out selectedVertex);

            if (selectedVertexCount == 1)
            {
                Vertex v = selectedVertex;
                AppendVertexHeader(sb, v, 0);

                if (graph.graphType == Graph.GraphType.HeapGraph)
                    sb.Append("\r\nReferred to by:\r\n");
                else
                    sb.Append("\r\nContributions from callers:\r\n");

                ArrayList callers = new ArrayList();
                foreach (Edge edge in v.incomingEdges.Values)
                    callers.Add(edge);
                callers.Sort();
                foreach (Edge edge in callers)
                {
                    Vertex vv = edge.FromVertex;
                    string signature1 = vv.signature != null ? vv.signature : "";
                    string explain = "from";
                    if (graph.graphType == Graph.GraphType.CallGraph)
                        explain = "caused by";
                    sb.AppendFormat("\t{0} {1}\t{2}\t{3}\r\n", formatWeight(edge.weight), explain, vv.name, signature1);
                }

                if (graph.graphType == Graph.GraphType.HeapGraph)
                    sb.Append("\r\nReferring to:\r\n");
                else
                    sb.Append("\r\nContributions to callees:\r\n");

                ArrayList callees = new ArrayList();
                foreach (Edge edge in v.outgoingEdges.Values)
                    callees.Add(edge);
                callees.Sort();
                foreach (Edge edge in callees)
                {
                    Vertex vv = edge.ToVertex;
                    string signature2 = vv.signature != null ? vv.signature : "";
                    string explain = "to";
                    if (graph.graphType == Graph.GraphType.CallGraph)
                        explain = "caused by";
                    sb.AppendFormat("\t{0} {1}\t{2}\t{3}\r\n", formatWeight(edge.weight), explain, vv.name, signature2);
                }
            }
            else
            {
                foreach (ArrayList al in levelList)
                {
                    foreach (Vertex v in al)
                    {
                        if (v.selected || (selectedVertexCount == 0 && v.visible))
                        {
                            AppendVertexHeader(sb, v, v.level);
                        }
                    }
                }
            }
            Clipboard.SetDataObject(sb.ToString());
        }

        private void selectAllMenuItem_Click(object sender, System.EventArgs e)
        {
            foreach (Vertex v in graph.vertices.Values)
            {
                if (!v.selected)
                {
                    //graphPanel.Invalidate(v.rectangle);
                    graphPanel.InvalidateVisual();
                    v.selected = true;
                }
            }
        }
        private double distance(Point p1, Point p2)
        {
            int deltaX = p1.X - p2.X;
            int deltaY = p1.Y - p2.Y;
            return Math.Sqrt(deltaX * deltaX + 4.0 * deltaY * deltaY);
        }

        private void edgePopup(Edge e, bool isOutgoingEdge)
        {
            Vertex v;
            Point p;
            if (isOutgoingEdge)
            {
                p = e.fromPoint;
                v = e.ToVertex;
            }
            else
            {
                p = e.toPoint;
                v = e.FromVertex;
            }
            string caption = v.basicName + ": " + formatWeight(e.weight);
            Rectangle r = new Rectangle(p.X, p.Y, 1, 1);
            //r = graphPanel.RectangleToScreen(r);
            Point screenPoint = new Point(r.X, r.Y - 20);
            toolTip.Active = true;
            //toolTip.SetToolTip(graphPanel, caption);
        }

        private void activateIncoming(Vertex v)
        {
            if (v.active)
                return;
            v.active = true;
            foreach (Edge e in v.incomingEdges.Values)
                activateIncoming(e.FromVertex);
        }

        private void activateOutgoing(Vertex v)
        {
            if (v.active)
                return;
            v.active = true;
            foreach (Edge e in v.outgoingEdges.Values)
                activateOutgoing(e.ToVertex);
        }

        private void prune()
        {
            int selectedVerticesCount = 0;
            foreach (Vertex v in graph.vertices.Values)
                if (v.selected)
                    selectedVerticesCount += 1;

            if (selectedVerticesCount == 0)
                return;

            foreach (Vertex v in graph.vertices.Values)
                v.active = false;

            foreach (Vertex v in graph.vertices.Values)
            {
                if (v.selected && !v.active)
                {
                    v.active = true;
                    foreach (Edge edge in v.incomingEdges.Values)
                        activateIncoming(edge.FromVertex);
                    foreach (Edge edge in v.outgoingEdges.Values)
                        activateOutgoing(edge.ToVertex);
                }
            }
            graph.BottomVertex.active = false;
            graphPanel.InvalidateVisual();
            placeVertices = placeEdges = true;
        }

        private void pruneMenuItem_Click(object sender, System.EventArgs e)
        {
            prune();
        }

        private void RefreshGraph()
        {
            Graph orgGraph = graph;
            if (orgGraph.graphSource is Graph)
            {
                orgGraph = (Graph)orgGraph.graphSource;
            }

            switch (orgGraph.graphType)
            {
                case Graph.GraphType.CallGraph:
                    graph = ((Histogram)orgGraph.graphSource).BuildCallGraph(filterForm);
                    graph.graphType = Graph.GraphType.CallGraph;
                    break;

                case Graph.GraphType.AllocationGraph:
                    graph = ((Histogram)orgGraph.graphSource).BuildAllocationGraph(filterForm);
                    graph.graphType = Graph.GraphType.AllocationGraph;
                    break;

                case Graph.GraphType.AssemblyGraph:
                    graph = ((Histogram)orgGraph.graphSource).BuildAssemblyGraph(filterForm);
                    graph.graphType = Graph.GraphType.AssemblyGraph;
                    break;

                case Graph.GraphType.HeapGraph:
                    graph = ((ObjectGraph)orgGraph.graphSource).BuildTypeGraph(orgGraph.allocatedAfterTickIndex, orgGraph.allocatedBeforeTickIndex, orgGraph.typeGraphOptions, filterForm);
                    graph.graphType = Graph.GraphType.HeapGraph;
                    break;

                case Graph.GraphType.HandleAllocationGraph:
                    graph = ((Histogram)orgGraph.graphSource).BuildHandleAllocationGraph(filterForm);
                    graph.graphType = Graph.GraphType.HandleAllocationGraph;
                    break;
            }
            placeVertices = placeEdges = true;
            graphPanel.InvalidateVisual();
        }

        private void filterMenuItem_Click(object sender, System.EventArgs e)
        {
            if (filterForm.ShowDialog().Value)
            {
                RefreshGraph();
            }
        }

        private void pruneContextMenuItem_Click(object sender, System.EventArgs e)
        {
            if (selectedVertex != null)
            {
                selectedVertex.selected = true;
                prune();
                graphPanel.InvalidateVisual();
            }
        }

        private void selectRecursiveMenuItem_Click(object sender, System.EventArgs e)
        {
            if (selectedVertex != null)
            {
                selectRecursive(selectedVertex);
                graphPanel.InvalidateVisual();
            }
        }

        private void FindVertex(string name, string signature, bool again)
        {
            ArrayList foundVertices = new ArrayList();
            foreach (Vertex v in graph.vertices.Values)
            {
                if (v.name.IndexOf(name) >= 0
                    && (v.signature == null
                      || v.signature.IndexOf(signature) >= 0))
                {
                    foundVertices.Add(v);
                }
            }
            foundVertices.Sort();

            Vertex foundVertex = null;
            foreach (Vertex v in foundVertices)
            {
                if (again)
                {
                    if (v.selected)
                        again = false;
                    continue;
                }
                foundVertex = v;
                break;
            }

            if (foundVertex != null)
            {
                foreach (Vertex v in graph.vertices.Values)
                {
                    v.selected = false;
                }
                foundVertex.selected = true;
                if (foundVertex.visible)
                {
                    //outerPanel.AutoScrollPosition = new Point(foundVertex.rectangle.X - 100, foundVertex.rectangle.Y - 100);
                    //selectEdges();
                    graphPanel.InvalidateVisual();
                }
                else
                {
                    if (MessageBox.Show("Routine found but not visible with current settings - zoom to this Routine?", "Not Visible", MessageBoxButtons.OKCancel) == System.Windows.Forms.DialogResult.OK)
                    {
                        ZoomVertex(foundVertex, "");
                    }
                }
            }
            else
            {
                MessageBox.Show("No Routine found");
            }
        }

        private void findMenuItem_Click(object sender, System.EventArgs e)
        {
            //if (findForm.ShowDialog() == DialogResult.OK)
            //{
            //    FindVertex(findForm.nameTextBox.Text, findForm.signatureTextBox.Text, false);
            //}
        }

        private void versionTimer_Tick(object sender, System.EventArgs e)
        {
            if (font != MainForm.instance.font)
            {
                font = MainForm.instance.font;
                fontHeight = font.Height;
                placeVertices = placeEdges = true;
                graphPanel.InvalidateVisual();
            }
        }

        private double Diversity(Dictionary<Vertex, Edge> d)
        {
            double sum = 0.0;
            double sumSq = 0.0;
            foreach (Edge e in d.Values)
            {
                double dWeight = e.weight;
                sum += dWeight;
                sumSq += dWeight * dWeight;
            }
            if (sumSq <= 0.0)
                return 0.0;
            else
                return sum * sum / sumSq;
        }

        private double Score(Vertex v)
        {
            return v.weight * (Diversity(v.incomingEdges) + Diversity(v.outgoingEdges));
        }

        class CompareVerticesByScore : IComparer
        {
            Dictionary<Vertex, double> scoreOfVertex;

            internal CompareVerticesByScore(Dictionary<Vertex, double> scoreOfVertex)
            {
                this.scoreOfVertex = scoreOfVertex;
            }

            int IComparer.Compare(object x, object y)
            {
                double scoreX = scoreOfVertex[(Vertex)x];
                double scoreY = scoreOfVertex[(Vertex)y];
                if (scoreX < scoreY)
                    return 1;
                else if (scoreX > scoreY)
                    return -1;
                else
                    return 0;
            }
        }

        private void findInterestingNodesMenuItem_Click(object sender, System.EventArgs e)
        {
            Dictionary<Vertex, double> scoreOfVertex = new Dictionary<Vertex, double>();
            ArrayList verticesSortedByScore = new ArrayList();
            foreach (Vertex v in graph.vertices.Values)
            {
                double score = Score(v);
                scoreOfVertex[v] = score;
                verticesSortedByScore.Add(v);
            }
            verticesSortedByScore.Sort(new CompareVerticesByScore(scoreOfVertex));

            for (int i = Math.Min(5, verticesSortedByScore.Count); i > 0; i--)
            {
                ZoomVertex((Vertex)verticesSortedByScore[i - 1], string.Format("Interesting Node (Rank {0}): ", i));
            }

        }

        private Vertex CloneVertex(Graph g, Vertex v)
        {
            Vertex vn = g.FindOrCreateVertex(v.name, v.signature, v.moduleName);
            vn.basicName = v.basicName;
            vn.basicSignature = v.basicSignature;
            vn.active = true;
            return vn;
        }

        private void ZoomVertex(Vertex v, string titlePrefix)
        {
            toolTip.Active = false;
            Graph g;
            if (graph.graphSource is Graph)
            {
                Graph orgGraph = (Graph)graph.graphSource;
                g = new Graph(orgGraph);
                v = orgGraph.FindOrCreateVertex(v.name, v.signature, v.moduleName);
            }
            else
                g = new Graph(graph);
            g.allocatedAfterTickIndex = graph.allocatedAfterTickIndex;
            g.allocatedBeforeTickIndex = graph.allocatedBeforeTickIndex;
            Vertex vn = CloneVertex(g, v);
            vn.count = v.count;
            if (v.incomingEdges.Count == 0)
            {
                if (v != graph.TopVertex)
                    g.FindOrCreateEdge(g.TopVertex, vn).AddWeight(v.weight);
            }
            else
            {
                foreach (Edge e in v.incomingEdges.Values)
                {
                    Vertex vin = CloneVertex(g, e.FromVertex);
                    g.FindOrCreateEdge(vin, vn).AddWeight(e.weight);
                    if (vin != g.TopVertex)
                        g.FindOrCreateEdge(g.TopVertex, vin).AddWeight(e.weight);
                }
            }
            if (v.outgoingEdges.Count == 0)
            {
                if (v != graph.BottomVertex)
                    g.FindOrCreateEdge(vn, g.BottomVertex).AddWeight(v.weight);
            }
            else
            {
                foreach (Edge e in v.outgoingEdges.Values)
                {
                    Vertex von = CloneVertex(g, e.ToVertex);
                    g.FindOrCreateEdge(vn, von).AddWeight(e.weight);
                    if (von != g.BottomVertex)
                        g.FindOrCreateEdge(von, g.BottomVertex).AddWeight(e.weight);
                }
            }
            g.BottomVertex.active = false;
            g.graphType = graph.graphType;
            g.typeGraphOptions = graph.typeGraphOptions;
            if (titlePrefix == null)
                titlePrefix = "Zoom to: ";
            string title = titlePrefix + v.name + " " + (v.signature != null ? v.signature : "");
            GraphViewForm graphViewForm = new GraphViewForm(g, title);
            graphViewForm.Visibility = Visibility.Visible;
        }

        private void graphPanel_DoubleClick(object sender, System.EventArgs e)
        {
            Point p = lastMouseDownPoint;
            foreach (Vertex v in graph.vertices.Values)
            {
                if (v.selectionRectangle.Contains(p))
                {
                    ZoomVertex(v, null);
                    return;
                }
            }
        }

        private Graph GetOriginalGraph()
        {
            if (graph.graphSource is Graph)
            {
                // this is the case when we are in a zoom window
                return (Graph)graph.graphSource;
            }
            else
                return graph;
        }

        private ObjectGraph GetObjectGraph()
        {
            return (ObjectGraph)GetOriginalGraph().graphSource;
        }

        private Histogram MakeHistogram(int allocatedAfterTickIndex, int allocatedBeforeTickIndex)
        {
            // Build a histogram of types, sizes, allocation stacks from the object graph,
            // using only the objects whose vertices are selected

            // First of all, limit the interval to the one of the underlying graph
            if (allocatedAfterTickIndex < graph.allocatedAfterTickIndex)
                allocatedAfterTickIndex = graph.allocatedAfterTickIndex;
            if (allocatedBeforeTickIndex > graph.allocatedBeforeTickIndex)
                allocatedBeforeTickIndex = graph.allocatedBeforeTickIndex;

            Graph originalGraph = GetOriginalGraph();
            ObjectGraph objectGraph = GetObjectGraph();
            Histogram histogram = new Histogram(objectGraph.readNewLog);
            bool anyVertexSelected = SelectedVertexCount() != 0;
            foreach (KeyValuePair<ulong, ObjectGraph.GcObject> keyValuePair in objectGraph.idToObject)
            {
                ulong id = keyValuePair.Key;
                ObjectGraph.GcObject gcObject = keyValuePair.Value;
                if (gcObject.TypeSizeStackTraceId > 0 &&
                    gcObject.AllocTickIndex > allocatedAfterTickIndex &&
                    gcObject.AllocTickIndex < allocatedBeforeTickIndex &&
                    gcObject.InterestLevel != InterestLevel.Ignore)
                {
                    if (anyVertexSelected || originalGraph != graph)
                    {
                        gcObject.vertex = null;
                        Vertex v = objectGraph.FindVertex(id, gcObject, originalGraph, ObjectGraph.BuildTypeGraphOptions.LumpBySignature);
                        if (originalGraph != graph)
                        {
                            v = graph.FindVertex(v.name, v.signature, v.moduleName);
                            if (v == null)
                                continue;
                        }
                        if (anyVertexSelected && !v.selected)
                            continue;
                    }
                    histogram.AddObject(gcObject.TypeSizeStackTraceId, 1);
                }
            }
            return histogram;
        }

        private void ShowWhoAllocated(string title, int allocatedAfterTickIndex, int allocatedBeforeTickIndex)
        {
            Histogram histogram = MakeHistogram(allocatedAfterTickIndex, allocatedBeforeTickIndex);

            // Build the real graph from the histogram

            Graph g = histogram.BuildAllocationGraph(filterForm);

            GraphViewForm graphViewForm = new GraphViewForm(g, title);
            graphViewForm.Visibility = Visibility.Visible;
        }

        private void showWhoAllocatedMenuItem_Click(object sender, System.EventArgs e)
        {
            if (graph.graphType == Graph.GraphType.HeapGraph)
            {
                string title = "Allocation Graph for Live Objects";
                ShowWhoAllocated(title, -1, int.MaxValue);
            }
        }

        private void showWhoAllocatedNewMenuItem_Click(object sender, System.EventArgs e)
        {
            if (graph.graphType == Graph.GraphType.HeapGraph)
            {
                string title = "Allocation Graph for New Live Objects";
                ShowWhoAllocated(title, graph.previousGraphTickIndex, int.MaxValue);
            }
        }

        private void showNewObjectsMenuItem_Click(object sender, System.EventArgs e)
        {
            ObjectGraph objectGraph = (ObjectGraph)graph.graphSource;
            Graph g = objectGraph.BuildTypeGraph(graph.previousGraphTickIndex, int.MaxValue, ObjectGraph.BuildTypeGraphOptions.LumpBySignature, filterForm);

            string title = "New Live Objects";
            GraphViewForm graphViewForm = new GraphViewForm(g, title);
            graphViewForm.Visibility = Visibility.Visible;
        }

        private void zoomToNodeMenuItem_Click(object sender, System.EventArgs e)
        {
            int selectedVertexCount = SelectedVertexCount();
            if (selectedVertexCount == 0)
                MessageBox.Show("Please select a node first by clicking on it");
            else if (selectedVertexCount > 10)
                MessageBox.Show("Too many vertices selected");
            else
            {
                foreach (Vertex v in graph.vertices.Values)
                {
                    if (v.selected)
                        ZoomVertex(v, null);
                }
            }
        }

        private void showObjectsAllocatedBetween_Click(object sender, System.EventArgs e)
        {
            CommentRangeForm commentRangeForm = new CommentRangeForm();
            if (commentRangeForm.ShowDialog().Value)
            {
                ObjectGraph objectGraph = (ObjectGraph)graph.graphSource;
                //Graph g = objectGraph.BuildTypeGraph(commentRangeForm.startTickIndex, commentRangeForm.endTickIndex, ObjectGraph.BuildTypeGraphOptions.LumpBySignature, filterForm);

                //string title = string.Format("Live Objects Allocated Between {0} and {1}", commentRangeForm.startComment, commentRangeForm.endComment);
                //GraphViewForm graphViewForm = new GraphViewForm(g, title);
                //graphViewForm.Visible = true;
            }
        }

        private void showWhoAllocatedObjectsBetweenMenuItem_Click(object sender, System.EventArgs e)
        {
            CommentRangeForm commentRangeForm = new CommentRangeForm();
            if (commentRangeForm.ShowDialog().Value)
            {
                //string title = string.Format("Allocation Graph for Live Objects Allocated Between {0} and {1}", commentRangeForm.startComment, commentRangeForm.endComment);
                //ShowWhoAllocated(title, commentRangeForm.startTickIndex, commentRangeForm.endTickIndex);
            }
        }

        private void showInstancesMenuItem_Click(object sender, System.EventArgs e)
        {
            Graph orgGraph = graph;
            if (orgGraph.graphSource is Graph)
                orgGraph = (Graph)orgGraph.graphSource;
            ObjectGraph objectGraph = (ObjectGraph)orgGraph.graphSource;
            ObjectGraph.BuildTypeGraphOptions options;
            if (graph.typeGraphOptions == ObjectGraph.BuildTypeGraphOptions.LumpBySignature)
            {
                options = ObjectGraph.BuildTypeGraphOptions.IndividualObjects;
            }
            else
            {
                options = ObjectGraph.BuildTypeGraphOptions.LumpBySignature;
            }
            graph = objectGraph.BuildTypeGraph(graph.allocatedAfterTickIndex, graph.allocatedBeforeTickIndex, options, filterForm);

            placeVertices = placeEdges = true;
            graphPanel.InvalidateVisual();
            EnableDisableMenuItems();
        }

        private void showHistogramMenuItem_Click(object sender, System.EventArgs e)
        {
            if (graph.graphType == Graph.GraphType.HeapGraph)
            {
                Histogram histogram = MakeHistogram(-1, int.MaxValue);

                ObjectGraph objectGraph = GetObjectGraph();
                string title = string.Format("Histogram by Size for Live Objects at {0}", objectGraph.readNewLog.TickIndexToTime(objectGraph.tickIndex));
                HistogramViewForm histogramViewForm = new HistogramViewForm(histogram, title);
                histogramViewForm.Show();
            }
        }

        private void showReferencesMenuItem_Click(object sender, System.EventArgs e)
        {
            if (graph.graphType == Graph.GraphType.HeapGraph && SelectedVertexCount() != 0)
            {
                ObjectGraph objectGraph = GetObjectGraph();
                Graph g = objectGraph.BuildReferenceGraph(graph);
                string title = "References to selected objects";
                GraphViewForm graphViewForm = new GraphViewForm(g, title);
                graphViewForm.Visibility = Visibility.Visible;
            }
        }

        private void FilterToSelectedVertex(bool ancestors, bool descendants)
        {
            StringBuilder types = new StringBuilder();
            StringBuilder methods = new StringBuilder();
            StringBuilder signatures = new StringBuilder();
            StringBuilder addresses = new StringBuilder();

            ArrayList selectedVertices = new ArrayList();
            foreach (Vertex v in graph.vertices.Values)
            {
                if (v.selected)
                {
                    selectedVertices.Add(v);
                    if (v.signature == null || graph.graphType == Graph.GraphType.HeapGraph)
                    {
                        if (types.Length != 0)
                            types.Append(';');
                        types.Append(v.name);
                    }
                    else
                    {
                        if (methods.Length != 0)
                            methods.Append(';');
                        methods.Append(Vertex.RemoveRecursionCount(v.name));
                    }
                    if (v.signature != null)
                    {
                        if (graph.graphType == Graph.GraphType.HeapGraph &&
                            graph.typeGraphOptions == ObjectGraph.BuildTypeGraphOptions.IndividualObjects)
                        {
                            if (addresses.Length != 0)
                                addresses.Append(';');
                            string[] pieces = v.signature.Split('=', ',');
                            Debug.Assert(pieces.Length == 4 && pieces[0] == "Address ");
                            addresses.Append(pieces[1].Trim());
                        }
                        else
                        {
                            if (signatures.Length != 0)
                                signatures.Append(';');
                            signatures.Append(v.signature);
                        }
                    }
                }
            }

            string typeFilter = types.ToString();
            string methodFilter = methods.ToString();
            string signatureFilter = signatures.ToString();
            string addressFilter = addresses.ToString();

            filterForm.SetFilterForm(typeFilter, methodFilter, signatureFilter, addressFilter,
                ancestors, descendants, false, false);

            RefreshGraph();

            foreach (Vertex v in selectedVertices)
            {
                Vertex newV = graph.FindOrCreateVertex(v.name, v.signature, v.moduleName);
                selectVertex(newV, true);
            }
            selectEdges();
        }

        private void filterToCallersCalleesMenuItem_Click(object sender, System.EventArgs e)
        {
            FilterToSelectedVertex(true, true);
        }

        private void resetFilterMenuItem_Click(object sender, System.EventArgs e)
        {
            filterForm.SetFilterForm("", "", "", "",
                true, true, false, false);

            RefreshGraph();
        }

        private void findAgainMenuItem_Click(object sender, System.EventArgs e)
        {
            //if (findForm.nameTextBox.Text == "" && findForm.signatureTextBox.Text == "")
            //{
            //    if (findForm.ShowDialog() != DialogResult.OK)
            //        return;
            //}
            //FindVertex(findForm.nameTextBox.Text, findForm.signatureTextBox.Text, true);
        }
    }
}
