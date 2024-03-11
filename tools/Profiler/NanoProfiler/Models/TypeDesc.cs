using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace nanoFramework.Tools.NanoProfiler.Models
{
    public class TypeDesc : IComparable
    {
        internal string TypeName { get; set; }
        internal ulong TotalSize { get; set; }
        internal int Count { get; set; }
        internal System.Drawing.Color Color { get; set; }
        internal System.Drawing.Brush? Brush { get; set; }
        internal System.Drawing.Pen? Pen { get; set; }
        internal bool Selected { get; set; }
        internal System.Drawing.Rectangle Rect { get; set; }

        internal TypeDesc(string typeName)
        {
            TypeName = typeName;
        }

        public int CompareTo(object? o)
        {
            if (o is not TypeDesc t)
            {
                return -1;
            }
            if (t.TotalSize < TotalSize)
            {
                return -1;
            }
            else if (t.TotalSize > TotalSize)
            {
                return 1;
            }
            else
            {
                return 0;
            }
        }
    }
}
