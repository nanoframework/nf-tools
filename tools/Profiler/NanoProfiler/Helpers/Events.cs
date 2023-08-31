using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace nanoFramework.Tools.NanoProfiler.Helpers
{
    public delegate void NotifyDelegate<in T>(T t);
}
