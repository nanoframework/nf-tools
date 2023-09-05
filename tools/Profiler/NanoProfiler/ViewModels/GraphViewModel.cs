using CLRProfiler;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveCharts;
using LiveCharts.Configurations;
using LiveCharts.Wpf;
using nanoFramework.Tools.NanoProfiler.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using WinForms.CLRProfiler;

namespace nanoFramework.Tools.NanoProfiler.ViewModels
{
    public partial class GraphViewModel: ObservableObject
    {
        #region Observarbe properties

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


        //public Func<ChartPoint, string> PointLabel { get; set; }


        #region Properties

        private Graph graph;
        public GraphViewForm graphViewForm { get; set; }
        #endregion

        public Func<ChartPoint, string> PointLabel { get; set; }

        #region Constructor
        public GraphViewModel()
        {

            SetComboValues();

            //for (int i = 0; i < 10; i++)
            //{
            //    GraphValues.Add(new GraphDataModel()
            //    {
            //        GraphValue = 10 +i,
            //        GraphColor = i < 5 ? Brushes.Green : Brushes.Red,
            //    });

            //}
            //GraphConfiguration = new CartesianMapper<GraphDataModel>()
            //    .X((value, index) => index)
            //    .Y((value, index) => value.GraphValue)
            //    .Fill(value => value.GraphColor)
            //    .Stroke(value => value.GraphColor);

            //PointLabel = chartPoint =>
            //    string.Format("{0} ({1:P})", chartPoint.Y, chartPoint.Participation);




            GraphConfiguration = new CartesianMapper<GraphDataModel>()
                          .X((value, index) => index)
                          .Y((value, index) => value.GraphValue)
                          .Fill(value => value.GraphColor)
                          .Stroke(value => value.GraphColor);

            ChartValues<GraphDataModel> values = new ChartValues<GraphDataModel>();

            for (int i = 0; i < 10; i++)
            {
                values.Add(new GraphDataModel()
                {
                    GraphValue = 10 + i,
                    GraphColor = i < 5 ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Red,
                });

            }
            foreach (var item in values)
            {
                PieSeriesCollection.Add(new PieSeries { 
                    Values = new ChartValues<GraphDataModel> 
                    { 
                        item 
                    }, 
                    Configuration = GraphConfiguration 
                });
            }

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
