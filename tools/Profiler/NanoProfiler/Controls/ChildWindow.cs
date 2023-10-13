 
using System.Windows;
using System.Windows.Input;

namespace nanoFramework.Tools.NanoProfiler.Controls;

public class ChildWindow : Window
{
    public ChildWindow()
    {
        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, OnCloseWindow));
        CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand, OnMaximizeWindow, OnCanResizeWindow));
        CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, OnMinimizeWindow, OnCanMinimizeWindow));
        CommandBindings.Add(new CommandBinding(SystemCommands.RestoreWindowCommand, OnRestoreWindow, OnCanResizeWindow));
    }
   
    private void OnCanResizeWindow(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = this.ResizeMode == ResizeMode.CanResize || this.ResizeMode == ResizeMode.CanResizeWithGrip;
    private void OnCanMinimizeWindow(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = this.ResizeMode != ResizeMode.NoResize;
    private void OnMaximizeWindow(object target, ExecutedRoutedEventArgs e)=> SystemCommands.MaximizeWindow(this);    
    private void OnMinimizeWindow(object target, ExecutedRoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void OnRestoreWindow(object target, ExecutedRoutedEventArgs e) =>   SystemCommands.RestoreWindow(this);  
    private void OnCloseWindow(object target, ExecutedRoutedEventArgs e) => SystemCommands.CloseWindow(this);
}
