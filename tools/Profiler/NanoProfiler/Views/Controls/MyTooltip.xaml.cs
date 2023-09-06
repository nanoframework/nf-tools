using LiveCharts.Wpf;
using LiveCharts;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace nanoFramework.Tools.NanoProfiler.Views.Controls
{
    public class MyTooltipContent
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }
    /// <summary>
    /// Interaction logic for MyTooltip.xaml
    /// </summary>
    public partial class MyTooltip : IChartTooltip
    {
        //public string Name { get; set; } = "ime";
        //public string Value { get; set; } = "vrednost";
        private TooltipData _data;
        public MyTooltip()
        {
            InitializeComponent();
            DataContext = this;
        }
        public event PropertyChangedEventHandler PropertyChanged;

        public TooltipData Data
        {
            get { return _data; }
            set
            {
                _data = value;
                OnPropertyChanged("Data");
            }
        }

        public TooltipSelectionMode? SelectionMode { get; set; }

        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            if (PropertyChanged != null)
                PropertyChanged.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
