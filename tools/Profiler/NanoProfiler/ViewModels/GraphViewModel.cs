using CLRProfiler;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveCharts;
using LiveCharts.Wpf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using WinForms.CLRProfiler;

namespace nanoFramework.Tools.NanoProfiler.ViewModels
{
    public partial class GraphViewModel: ObservableObject
    {
        #region Observarbe properties
        [ObservableProperty]
        private ObservableCollection<double> _verticalScaleList;

        [ObservableProperty]
        private double _verticalScaleSelectedValue;

        [ObservableProperty]
        private ObservableCollection<string> _horizontalScaleList;

        [ObservableProperty]
        private string _horizontalScaleSelectedValue;

        #endregion


        //public Func<ChartPoint, string> PointLabel { get; set; }


        #region Properties

        private Graph graph;
        public GraphViewForm graphViewForm { get; set; }
        #endregion


        #region Constructor
        public GraphViewModel()
        {
            //PointLabel = chartPoint =>
            //    string.Format("{0} ({1:P})", chartPoint.Y, chartPoint.Participation);

            SetComboValues();
            //graphViewForm = TryFindResource("graphViewForm") as GraphViewForm;
            //windowsFormsHost.Child = graphPanel;

        }

        #endregion

        #region Funcs
        private void SetComboValues()
        {
            VerticalScaleList = new ObservableCollection<double>() { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000 };
            VerticalScaleSelectedValue = 10;

            HorizontalScaleList = new ObservableCollection<string>() { "Coarse", "Medium", "Fine", "Very Fine" };
            HorizontalScaleSelectedValue = "Coarse";
        }
        #endregion


    }
}
