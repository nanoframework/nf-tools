using CLRProfiler;
using LiveCharts;
using LiveCharts.Configurations;
using LiveCharts.Wpf;
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

namespace nanoFramework.Tools.NanoProfiler.Views
{
    /// <summary>
    /// Interaction logic for HistogramView.xaml
    /// </summary>
    public partial class HistogramView : Window
    {
        public HistogramView()
        {
            InitializeComponent();
            //cartesianChart.Series.Add(new ColumnSeries
            //{
            //    Title = "Column Chart: ",
            //    Fill = Brushes.CadetBlue,
            //    Values = new ChartValues<double> { 1500, 2500, 3700, 2000, 1000 },
            //    DataLabels = true,
            //    LabelPoint = point => (point.Y).ToString(),
            //});
            //this.BlueSeries = new ColumnSeries()
            //{
            //    Title = "Population of Bodrum",
            //    Values = new ChartValues<double> { 1500, 2500, 3700, 2000, 1000 },
            //    Fill = Brushes.Blue
            //};
            //this.SeriesCollection = new SeriesCollection() { this.BlueSeries };
        }

        private void ChangeThirdChartPointColorToRed()
        {
            CartesianMapper<double> mapper = Mappers.Xy<double>()
              .X((value, index) => index)
              .Y(value => value)
              .Fill((value, index) => index == 2 ? Brushes.Red : Brushes.Blue);

            // Dynamically set the third chart point color to red
            this.BlueSeries.Configuration = mapper;
        }

        // The actual chart data source
        public SeriesCollection SeriesCollection { get; set; }

        private ColumnSeries BlueSeries { get; set; }

        public HistogramView(Histogram allocatedHistogram, string title)
        {
            AllocatedHistogram = allocatedHistogram;
            Title = title;
        }

        public Histogram AllocatedHistogram { get; }
    }
}
