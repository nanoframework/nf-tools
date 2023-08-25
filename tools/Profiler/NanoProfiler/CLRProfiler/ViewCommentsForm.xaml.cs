using CLRProfiler;
using Microsoft.VisualBasic.Logging;
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

namespace nanoFramework.Tools.NanoProfiler.CLRProfiler
{
    /// <summary>
    /// Interaction logic for ViewCommentsForm.xaml
    /// </summary>
    public partial class ViewCommentsForm : Window
    {
        internal ViewCommentsForm(ReadNewLog log)
        {
            InitializeComponent();

            string[] lines = new string[log.commentEventList.count];
            for (int i = 0; i < log.commentEventList.count; i++)
                lines[i] = string.Format("{0} ({1:f3} secs)", log.commentEventList.eventString[i], log.TickIndexToTime(log.commentEventList.eventTickIndex[i]));
            
            // TODO
            // this.commentTextBox.Text = lines;
        }
    }
}
