////
// Copyright (c) .NET Foundation and Contributors.
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
////

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
