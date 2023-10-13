////
// Copyright (c) .NET Foundation and Contributors.
// See LICENSE file in the project root for full license information.
////

using nanoFramework.Tools.NanoProfiler.ViewModels;
using nanoFramework.Tools.NanoProfiler.Views.Controls;
using System;
using System.Windows;
using System.Windows.Input;

namespace nanoFramework.Tools.NanoProfiler.Views;

/// <summary>
/// Interaction logic for SummaryView.xaml
/// </summary>
public partial class SummaryView : ChildWindow
{
    public SummaryView() => InitializeComponent();



    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        // Begin dragging the window
        this.DragMove();
    }
}