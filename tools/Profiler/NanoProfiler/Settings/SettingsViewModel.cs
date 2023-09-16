using CommunityToolkit.Mvvm.ComponentModel;
using MaterialDesignThemes.Wpf;
using System;

namespace nanoFramework.Tools.NanoProfiler.Settings;

public partial class SettingsViewModel: ObservableObject
{
    [ObservableProperty]
    private string _header = "Settings";
    [ObservableProperty]
    private string _iconName = "Settings";
    [ObservableProperty]
    private bool _isDarkTheme =false;

    public SettingsViewModel()
    {
        var paletteHelper = new PaletteHelper();
        var theme = paletteHelper.GetTheme();
        var baseTheme = theme.GetBaseTheme();

        _isDarkTheme = baseTheme == BaseTheme.Dark;          
       // ModifyTheme();
     }

    partial void OnIsDarkThemeChanged(bool value) => this.ModifyTheme();
   
    private   void ModifyTheme( )
    {
        var paletteHelper = new PaletteHelper();
        ITheme theme = paletteHelper.GetTheme();
        IBaseTheme baseTheme = this.IsDarkTheme ? new MaterialDesignDarkTheme() : new MaterialDesignLightTheme();
        theme.SetBaseTheme(baseTheme);
        paletteHelper.SetTheme(theme);
    }
}
