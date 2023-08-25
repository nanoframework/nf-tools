using CLRProfiler;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
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
    /// Interaction logic for ViewByAddressForm.xaml
    /// </summary>
    public partial class ViewByAddressForm : Window
    {
        private bool autoUpdate;
        private System.Windows.Forms.RadioButton threetwoRadioButton;
        private System.Windows.Forms.RadioButton sixfourradioButton;
        private string baseTitle;
        private RadioButton radioButton1;
        private RadioButton radioButton2;
        private RadioButton radioButton4;
        private RadioButton radioButton3;

        Font font;

        bool initialized = false;

        LiveObjectTable liveObjectTable;
        TypeDesc[] typeIndexToTypeDesc;
        string[] typeName;

        ArrayList sortedTypeTable;
        public ViewByAddressForm()
        {
            InitializeComponent();

            //toolTip = new ToolTip();
            //toolTip.Active = false;
            //toolTip.ShowAlways = true;
            //toolTip.AutomaticDelay = 70;
            //toolTip.ReshowDelay = 1;

            autoUpdate = true;
            baseTitle = "View Objects by Address";

            ReadLogResult logResult = MainForm.instance.lastLogResult;
            if (logResult != null)
            {
                liveObjectTable = logResult.liveObjectTable;
                typeName = liveObjectTable.readNewLog.typeName;
            }

            font = MainForm.instance.font;
        }

        internal ViewByAddressForm(LiveObjectTable liveObjectTable, string title) : this()
        {
            this.liveObjectTable = liveObjectTable;
            typeName = liveObjectTable.readNewLog.typeName;
            autoUpdate = false;
            baseTitle = title;

            // TODO
           // this.Text = title;
        }

        class TypeDesc : IComparable
        {
            internal string typeName;
            internal int typeIndex;
            internal ulong totalSize;
            internal ulong selectedSize;
            internal double percentage;
            internal double selectedPercentage;
            internal System.Drawing.Color[] colors;
            internal System.Drawing.Brush[] brushes;
            internal System.Drawing.Pen[] pens;
            internal int selected;
            internal System.Drawing.Rectangle rect;

            internal TypeDesc(int typeIndex, string typeName)
            {
                this.typeIndex = typeIndex;
                this.typeName = typeName;
            }

            public int CompareTo(Object o)
            {
                TypeDesc t = (TypeDesc)o;
                if (t.selectedSize != this.selectedSize)
                {
                    if (t.selectedSize < this.selectedSize)
                        return -1;
                    else
                        return 1;
                }
                if (t.totalSize < this.totalSize)
                    return -1;
                else if (t.totalSize > this.totalSize)
                    return 1;
                else
                    return 0;
            }
        }

    }
}
