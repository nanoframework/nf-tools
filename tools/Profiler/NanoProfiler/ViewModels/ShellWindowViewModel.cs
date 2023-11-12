using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using nanoFramework.Tools.NanoProfiler.Services;
using nanoFramework.Tools.NanoProfiler.Settings;
using nanoFramework.Tools.NanoProfiler.Views;
using System.Linq;


namespace nanoFramework.Tools.NanoProfiler.ViewModels;

internal partial class ShellWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableObject _selectedItem;
    public ShellWindowViewModel()
    {
        this.SelectedItem = this.Items.First();
    }
    [ObservableProperty]
    private ObservableObject[] _items = new ObservableObject[] { new ProfilerLauncherViewModel(new ReadLogResultService()), new SettingsViewModel() };


    [RelayCommand]
    private void CloseApp(object obj)
    {
        if (obj is not ShellWindow shellWindow)
        {
            return;
        }

        var profilerLauncherViewModel = this.Items.OfType<ProfilerLauncherViewModel>().FirstOrDefault();
        profilerLauncherViewModel?.ViewLoadedCommand.Execute(true);
        shellWindow.Close();


    }
}
