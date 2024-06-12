////
// Copyright (c) .NET Foundation and Contributors.
// See LICENSE file in the project root for full license information.
////

using System;
using System.Collections.Generic;

namespace nanoFramework.Tools.NanoProfiler.Models
{
    public struct Bucket
    {
        internal int MinSize { get; set; }
        internal int MaxSize { get; set; }
        internal ulong TotalSize { get; set; }
        internal Dictionary<TypeDesc, SizeCount> TypeDescToSizeCount { get; set; }
        internal bool Selected { get; set; }
    }
}
