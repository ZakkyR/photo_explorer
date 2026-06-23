using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PhotoExplorer.Core.Models;
using PhotoExplorer.Core.Services;
using System.Collections.ObjectModel;
using System.Windows;

namespace PhotoExplorer.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IFolderService _folderService;
    private readonly IAlbumService _albumService;
    private readonly IImageService _imageService;
    private readonly ITagService _tagService;

    public ObservableCollection<string> Folders { get; } = new();
    public ObservableCollection<Album> Albums { get; } = new();
    public ObservableCollection<ImageItemViewModel> AllImages { get; } = new();
    public ObservableCollection<ImageItemViewModel> FilteredImages { get; } = new();
    public ObservableCollection<TagFilterItem> TagFilters { get; } = new();

    [ObservableProperty]
    private double _thumbnailSize = App.AppSettings.ThumbnailSize;

    [ObservableProperty]
    private string? _selectedFolder;

    [ObservableProperty]
    private Album? _selectedAlbum;

    [ObservableProperty]
    private bool _isLoading;

    public MainViewModel(
        IFolderService folderService,
        IAlbumService albumService,
        IImageService imageService,
        ITagService tagService)
    {
        _folderService = folderService;
        _albumService = albumService;
        _imageService = imageService;
        _tagService = tagService;

        _folderService.FolderChanged += OnFolderChanged;
    }

    public async Task InitializeAsync()
    {
        var folders = await _folderService.GetRegisteredFoldersAsync();
        foreach (var f in folders) Folders.Add(f);

        var albums = await _albumService.GetAlbumsAsync();
        foreach (var a in albums) Albums.Add(a);

        if (App.AppSettings.LastSelectedFolder is { } last && Folders.Contains(last))
            await SelectFolderAsync(last);
    }

    [RelayCommand]
    private async Task AddFolder()
    {
        var dialog = new OpenFolderDialog { Title = "画像フォルダを選択" };
        if (dialog.ShowDialog() != true) return;
        var path = dialog.FolderName;
        await _folderService.RegisterFolderAsync(path);
        if (!Folders.Contains(path)) Folders.Add(path);
        await SelectFolderAsync(path);
    }

    [RelayCommand]
    private async Task RemoveFolder(string path)
    {
        await _folderService.UnregisterFolderAsync(path);
        Folders.Remove(path);
        if (SelectedFolder == path) { AllImages.Clear(); FilteredImages.Clear(); SelectedFolder = null; }
    }

    [RelayCommand]
    private async Task AddAlbum()
    {
        var name = Microsoft.VisualBasic.Interaction.InputBox("アルバム名を入力してください", "新規アルバム");
        if (string.IsNullOrWhiteSpace(name)) return;
        var album = await _albumService.CreateAlbumAsync(name);
        Albums.Add(album);
    }

    public async Task SelectFolderAsync(string path)
    {
        SelectedFolder = path;
        SelectedAlbum = null;
        App.AppSettings.LastSelectedFolder = path;
        await LoadImagesAsync(await _imageService.LoadImagesFromFolderAsync(path, _tagService));
    }

    public async Task SelectAlbumAsync(Album album)
    {
        SelectedAlbum = album;
        SelectedFolder = null;
        await LoadImagesAsync(await _imageService.LoadImagesFromAlbumAsync(album, _tagService));
    }

    private async Task LoadImagesAsync(IReadOnlyList<ImageItem> items)
    {
        IsLoading = true;
        AllImages.Clear();
        FilteredImages.Clear();
        TagFilters.Clear();

        var vms = items.Select(i => new ImageItemViewModel(i)).ToList();
        foreach (var vm in vms) AllImages.Add(vm);

        await RefreshTagFiltersAsync();
        ApplyTagFilter();

        // サムネイルを非同期で生成
        _ = Task.Run(async () =>
        {
            foreach (var vm in vms)
            {
                var bytes = await _imageService.GenerateThumbnailAsync(vm.Model.FilePath);
                Application.Current.Dispatcher.Invoke(() => vm.SetThumbnailFromBytes(bytes));
            }
            Application.Current.Dispatcher.Invoke(() => IsLoading = false);
        });
    }

    private Task RefreshTagFiltersAsync()
    {
        var tagNames = AllImages
            .SelectMany(vm => vm.Model.Tags.Select(t => t.Name))
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        TagFilters.Clear();
        foreach (var name in tagNames)
        {
            var item = new TagFilterItem(name);
            item.SelectionChanged += (_, _) => ApplyTagFilter();
            TagFilters.Add(item);
        }

        return Task.CompletedTask;
    }

    public void ApplyTagFilter()
    {
        var selectedTags = TagFilters.Where(t => t.IsSelected).Select(t => t.Name).ToHashSet();
        FilteredImages.Clear();
        foreach (var vm in AllImages)
        {
            if (selectedTags.Count == 0 || vm.Model.Tags.Any(t => selectedTags.Contains(t.Name)))
                FilteredImages.Add(vm);
        }
    }

    private async void OnFolderChanged(object? sender, FolderChangedEventArgs e)
    {
        if (SelectedFolder == e.FolderPath)
            await SelectFolderAsync(e.FolderPath);
        else if (SelectedAlbum?.FolderPaths.Contains(e.FolderPath) == true)
            await SelectAlbumAsync(SelectedAlbum);
    }

    partial void OnThumbnailSizeChanged(double value)
        => App.AppSettings.ThumbnailSize = value;
}
