////
// Copyright (c) .NET Foundation and Contributors.
// See LICENSE file in the project root for full license information.
////

using CommunityToolkit.Mvvm.ComponentModel;

namespace nanoFramework.Tools.NanoProfiler.Models
{
    public partial class TypeDescViewModel : ObservableObject
    {
        [ObservableProperty]
        private string? _title;

        [ObservableProperty]
        private TypeDesc? _typeDesc;

        [ObservableProperty]
        private double _valueSize;

        [ObservableProperty]
        private double _bucketTotalSize;
    }
}
