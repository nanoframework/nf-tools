using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace nanoFramework.Tools.NanoProfiler.Models
{
    public struct Bucket
    {
        internal int minSize;
        internal int maxSize;
        internal ulong totalSize;
        internal Dictionary<TypeDesc, SizeCount> typeDescToSizeCount;
        internal bool selected;
    }

    public class TypeDesc : IComparable
    {
        internal string typeName;
        internal ulong totalSize;
        internal int count;
        internal System.Drawing.Color color;
        internal System.Drawing.Brush brush;
        internal System.Drawing.Pen pen;
        internal bool selected;
        internal System.Drawing.Rectangle rect;

        internal TypeDesc(string typeName)
        {
            this.typeName = typeName;
        }

        public int CompareTo(object o)
        {
            TypeDesc t = (TypeDesc)o;
            if (t.totalSize < this.totalSize)
            {
                return -1;
            }
            else if (t.totalSize > this.totalSize)
            {
                return 1;
            }
            else
            {
                return 0;
            }
        }
    }

    public class SizeCount
    {
        internal ulong size;
        internal int count;
    }
}
