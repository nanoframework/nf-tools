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
using nanoFramework.Tools.NanoProfiler.CLRProfiler;
using static System.Net.Mime.MediaTypeNames;
using System.Globalization;
using nanoFramework.Tools.NanoProfiler.Models;
using LiveCharts.Configurations;

namespace nanoFramework.Tools.NanoProfiler.Views
{
    /// <summary>
    /// Interaction logic for GraphView.xaml
    /// </summary>
    public partial class GraphView : Window
    {
        public GraphView()
        {
            InitializeComponent();
        }
    }
}
