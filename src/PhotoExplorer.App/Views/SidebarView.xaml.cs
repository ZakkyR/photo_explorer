using PhotoExplorer.App.ViewModels;
using PhotoExplorer.Core.Models;
using PhotoExplorer.Core.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;


namespace PhotoExplorer.App.Views;

public partial class SidebarView : UserControl
{
    public SidebarView() => InitializeComponent();

    private MainViewModel Vm => (MainViewModel)DataContext;

    private async void FolderItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FolderInfo folderInfo)
            await Vm.SelectFolderAsync(folderInfo.Path);
    }

    private async void AlbumItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Album album)
            await Vm.SelectAlbumAsync(album);
    }

    private async void ManageFolders_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is Album album)
        {
            var albumService = App.Services.GetService(typeof(IAlbumService)) as IAlbumService;
            var folderService = App.Services.GetService(typeof(IFolderService)) as IFolderService;
            if (albumService == null || folderService == null) return;
            var dialog = new AlbumFolderDialog(album, albumService, folderService)
            {
                Owner = Window.GetWindow(this)
            };
            dialog.ShowDialog();
            // Refresh album in viewmodel after folder changes
            await Vm.RefreshAlbumAsync(album.Id);
        }
    }
}
