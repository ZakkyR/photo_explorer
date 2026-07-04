using Microsoft.Extensions.DependencyInjection;
using PhotoExplorer.App.ViewModels;
using System.Windows;

namespace PhotoExplorer.App;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainViewModel(
            App.Services.GetRequiredService<PhotoExplorer.Core.Services.IFolderService>(),
            App.Services.GetRequiredService<PhotoExplorer.Core.Services.IAlbumService>(),
            App.Services.GetRequiredService<PhotoExplorer.Core.Services.IImageService>(),
            App.Services.GetRequiredService<PhotoExplorer.Core.Services.ITagService>(),
            App.Services.GetRequiredService<PhotoExplorer.Core.Services.ISidecarService>());
        DataContext = ViewModel;
        RestoreWindowState();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
        => await ViewModel.InitializeAsync();

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        => SaveWindowState();

    private void RestoreWindowState()
    {
        var s = App.AppSettings;
        Left = s.WindowLeft; Top = s.WindowTop;
        Width = s.WindowWidth; Height = s.WindowHeight;
    }

    private void SaveWindowState()
    {
        App.AppSettings.WindowLeft = Left; App.AppSettings.WindowTop = Top;
        App.AppSettings.WindowWidth = Width; App.AppSettings.WindowHeight = Height;
    }
}
