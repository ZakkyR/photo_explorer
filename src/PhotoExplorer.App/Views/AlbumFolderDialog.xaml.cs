using PhotoExplorer.Core.Models;
using PhotoExplorer.Core.Services;
using System.Collections.ObjectModel;
using System.Windows;

namespace PhotoExplorer.App.Views;

public partial class AlbumFolderDialog : Window
{
    private readonly Album _album;
    private readonly IAlbumService _albumService;
    private readonly IFolderService _folderService;

    public new string Title => $"フォルダを管理 — {_album.Name}";
    public ObservableCollection<string> AlbumFolders { get; } = new();
    public ObservableCollection<string> AvailableFolders { get; } = new();

    public AlbumFolderDialog(Album album, IAlbumService albumService, IFolderService folderService)
    {
        _album = album;
        _albumService = albumService;
        _folderService = folderService;
        DataContext = this;
        InitializeComponent();
        this.Loaded += AlbumFolderDialog_Loaded;
    }

    private async void AlbumFolderDialog_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadFoldersAsync(_folderService);
    }

    private async Task LoadFoldersAsync(IFolderService folderService)
    {
        var registered = await folderService.GetRegisteredFoldersAsync();
        foreach (var f in _album.FolderPaths)
            AlbumFolders.Add(f);
        foreach (var f in registered)
        {
            if (!_album.FolderPaths.Contains(f.Path))
                AvailableFolders.Add(f.Path);
        }
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (AvailableFolderList.SelectedItem is not string path) return;
        await _albumService.AddFolderToAlbumAsync(_album.Id, path);
        AvailableFolders.Remove(path);
        AlbumFolders.Add(path);
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (AlbumFolderList.SelectedItem is not string path) return;
        await _albumService.RemoveFolderFromAlbumAsync(_album.Id, path);
        AlbumFolders.Remove(path);
        AvailableFolders.Add(path);
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
