using CommunityToolkit.Mvvm.ComponentModel;
using nanoFramework.Tools.NanoProfiler.Launcher;
using nanoFramework.Tools.NanoProfiler.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
     private ObservableObject [] _items = new ObservableObject [] { new ProfilerLauncherViewModel(), new SettingsViewModel() };
}
