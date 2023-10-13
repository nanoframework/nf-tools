////
// Copyright (c) .NET Foundation and Contributors.
// See LICENSE file in the project root for full license information.
////

using nanoFramework.Tools.NanoProfiler.Controls;
using System.Windows.Input;

namespace nanoFramework.Tools.NanoProfiler.Views;

/// <summary>
/// Interaction logic for HistogramView.xaml
/// </summary>
public partial class HistogramView : ChildWindow
{

    public HistogramView() => InitializeComponent();
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        // Begin dragging the window
        this.DragMove();
    }

}
