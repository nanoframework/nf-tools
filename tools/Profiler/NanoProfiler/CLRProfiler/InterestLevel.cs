using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CLRProfiler
{
    internal enum InterestLevel
    {
        Ignore = 0,
        Display = 1 << 0,
        Interesting = 1 << 1,
        Parents = 1 << 2,
        Children = 1 << 3,
        InterestingParents = Interesting | Parents,
        InterestingChildren = Interesting | Children,
        ParentsChildren = Parents | Children,
    }
}
