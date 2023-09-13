////
// Copyright (c) .NET Foundation and Contributors.
// See LICENSE file in the project root for full license information.
////

using LiveCharts;
using LiveCharts.Wpf;
using System.ComponentModel;

namespace nanoFramework.Tools.NanoProfiler.Views.Controls
{
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
