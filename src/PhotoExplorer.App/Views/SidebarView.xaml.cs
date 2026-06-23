using PhotoExplorer.App.ViewModels;
using PhotoExplorer.Core.Models;
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
        if (sender is FrameworkElement fe && fe.DataContext is string path)
            await Vm.SelectFolderAsync(path);
    }

    private async void AlbumItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Album album)
            await Vm.SelectAlbumAsync(album);
    }
}
