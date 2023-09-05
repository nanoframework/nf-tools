using LiveCharts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace nanoFramework.Tools.NanoProfiler.Models
{
    public class FruitPieChartDataModel
    {
        public FruitPieChartDataModel(Fruit entityFrameworkDataModel)
        {
            this.AppleDataSeries = new ChartValues<double>() { entityFrameworkDataModel.Apples };
            this.OrangeDataSeries = new ChartValues<double>() { entityFrameworkDataModel.Oranges };
            this.GrapeDataSeries = new ChartValues<double>() { entityFrameworkDataModel.Grapes };
            this.BananaDataSeries = new ChartValues<double>() { entityFrameworkDataModel.Bananas };
        }

        public ChartValues<double> AppleDataSeries { get; set; }
        public ChartValues<double> OrangeDataSeries { get; set; }
        public ChartValues<double> GrapeDataSeries { get; set; }
        public ChartValues<double> BananaDataSeries { get; set; }
    }


    public partial class Fruit
    {

        public double Apples { get; set; }
        public double Oranges { get; set; }
        public double Grapes { get; set; }
        public double Bananas { get; set; }
    }
}
