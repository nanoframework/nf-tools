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
    /// Interaction logic for CommentRangeForm.xaml
    /// </summary>
    public partial class CommentRangeForm : Window
    {
        internal const string startCommentString = "Start of Application";
        internal const string shutdownCommentString = "Shutdown of Application";

        public CommentRangeForm()
        {
            InitializeComponent();
        }
    }
}
