﻿////
// Copyright (c) .NET Foundation and Contributors.
// See LICENSE file in the project root for full license information.
////

using nanoFramework.Tools.NanoProfiler.ViewModels;
using System;
using System.Windows;
using System.Windows.Input;

namespace nanoFramework.Tools.NanoProfiler.Views;

/// <summary>
/// Interaction logic for SummaryView.xaml
/// </summary>
public partial class SummaryView : Window
{
    public SummaryView()
    {
        this.CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, OnCloseWindow));
        this.CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand, OnMaximizeWindow, OnCanResizeWindow));
        this.CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, OnMinimizeWindow, OnCanMinimizeWindow));
        this.CommandBindings.Add(new CommandBinding(SystemCommands.RestoreWindowCommand, OnRestoreWindow, OnCanResizeWindow));

        InitializeComponent();
    }
 

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        // Begin dragging the window
        this.DragMove();
    }
    /// <summary>
    /// Raises the System.Windows.Window.Closed event.
    /// </summary>
    /// <param name="e"></param>
    protected override void OnClosed(EventArgs e) => base.OnClosed(e);
    private void OnCanResizeWindow(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = this.ResizeMode == ResizeMode.CanResize || this.ResizeMode == ResizeMode.CanResizeWithGrip;
    private void OnCanMinimizeWindow(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = this.ResizeMode != ResizeMode.NoResize;
    private void OnMaximizeWindow(object target, ExecutedRoutedEventArgs e)
    {

        SystemCommands.MaximizeWindow(this);
        this.RestoreButton.Visibility=Visibility.Visible;
        this.MaximizeButton.Visibility=Visibility.Collapsed;
        this.WindowBorder.Margin= new(0, 0, 0, 17);
    }
    private void OnMinimizeWindow(object target, ExecutedRoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void OnRestoreWindow(object target, ExecutedRoutedEventArgs e)
    {
        SystemCommands.RestoreWindow(this);
        this.RestoreButton.Visibility=Visibility.Collapsed;
        this.MaximizeButton.Visibility=Visibility.Visible;
    }
    private void OnCloseWindow(object target, ExecutedRoutedEventArgs e) => SystemCommands.CloseWindow(this);


}