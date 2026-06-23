using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PhotoExplorer.Core.Models;
using PhotoExplorer.Core.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace PhotoExplorer.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IFolderService _folderService;
    private readonly IAlbumService _albumService;
    private readonly IImageService _imageService;
    private readonly ITagService _tagService;

    public ObservableCollection<FolderInfo> Folders { get; } = new();
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

    [ObservableProperty]
    private string _selectedFolderPath = string.Empty;

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

        if (App.AppSettings.LastSelectedFolder is { } last && Folders.Any(f => f.Path == last))
            await SelectFolderAsync(last);
    }

    [RelayCommand]
    private async Task AddFolder()
    {
        var dialog = new OpenFolderDialog { Title = "画像フォルダを選択" };
        if (dialog.ShowDialog() != true) return;
        var path = dialog.FolderName;
        await _folderService.RegisterFolderAsync(path);
        if (!Folders.Any(f => f.Path == path)) Folders.Add(new FolderInfo(path, null));
        await SelectFolderAsync(path);
    }

    [RelayCommand]
    private async Task RemoveFolder(string path)
    {
        await _folderService.UnregisterFolderAsync(path);
        var item = Folders.FirstOrDefault(f => f.Path == path);
        if (item != null) Folders.Remove(item);
        if (SelectedFolder == path) { AllImages.Clear(); FilteredImages.Clear(); SelectedFolder = null; SelectedFolderPath = string.Empty; }
    }

    [RelayCommand]
    private async Task AddAlbum()
    {
        var name = Microsoft.VisualBasic.Interaction.InputBox("アルバム名を入力してください", "新規アルバム");
        if (string.IsNullOrWhiteSpace(name)) return;
        var album = await _albumService.CreateAlbumAsync(name);
        Albums.Add(album);
    }

    [RelayCommand]
    private async Task DeleteAlbum(int albumId)
    {
        var album = Albums.FirstOrDefault(a => a.Id == albumId);
        if (album == null) return;
        await _albumService.DeleteAlbumAsync(albumId);
        Albums.Remove(album);
        if (SelectedAlbum?.Id == albumId) { AllImages.Clear(); FilteredImages.Clear(); SelectedAlbum = null; }
    }

    public async Task RefreshAlbumAsync(int albumId)
    {
        var updated = (await _albumService.GetAlbumsAsync()).FirstOrDefault(a => a.Id == albumId);
        if (updated == null) return;
        var idx = Albums.IndexOf(Albums.FirstOrDefault(a => a.Id == albumId)!);
        if (idx >= 0) Albums[idx] = updated;
        if (SelectedAlbum?.Id == albumId)
            await SelectAlbumAsync(updated);
    }

    [RelayCommand]
    private async Task RenameFolder(string folderPath)
    {
        var current = Folders.FirstOrDefault(f => f.Path == folderPath);
        var currentName = current?.DisplayName ?? string.Empty;
        var newName = Microsoft.VisualBasic.Interaction.InputBox(
            "新しい表示名を入力してください（空欄でリセット）",
            "名前を変更",
            currentName);
        if (newName == null) return; // cancelled
        var displayName = string.IsNullOrWhiteSpace(newName) ? null : newName.Trim();
        await _folderService.RenameFolderAsync(folderPath, displayName ?? string.Empty);

        // Refresh the FolderInfo in the collection
        var idx = Folders.IndexOf(current!);
        if (idx >= 0)
            Folders[idx] = new FolderInfo(folderPath, displayName);
    }

    public async Task SelectFolderAsync(string path)
    {
        SelectedFolder = path;
        SelectedAlbum = null;
        SelectedFolderPath = path;
        App.AppSettings.LastSelectedFolder = path;
        await LoadImagesAsync(await _imageService.LoadImagesFromFolderAsync(path, _tagService));
    }

    public async Task SelectAlbumAsync(Album album)
    {
        SelectedAlbum = album;
        SelectedFolder = null;
        SelectedFolderPath = string.Empty;
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

        // サムネイルを並列生成（WIC + ディスクキャッシュ）
        Directory.CreateDirectory(ThumbnailCacheDir);
        _ = Task.Run(async () =>
        {
            try
            {
                await Parallel.ForEachAsync(vms,
                    new ParallelOptions { MaxDegreeOfParallelism = 4 },
                    (vm, _) =>
                    {
                        var thumbnail = LoadThumbnailFast(vm.Model.FilePath);
                        if (thumbnail != null)
                            Application.Current.Dispatcher.Invoke(() => vm.Thumbnail = thumbnail);
                        return ValueTask.CompletedTask;
                    });
            }
            finally
            {
                Application.Current.Dispatcher.Invoke(() => IsLoading = false);
            }
        });

        if (ThumbnailSize >= FullImageThreshold)
        {
            foreach (var vm in FilteredImages)
                vm.LoadFullImage();
        }
    }

    public Task RefreshTagFiltersAsync()
    {
        var previouslySelected = TagFilters
            .Where(t => t.IsSelected)
            .Select(t => t.Name)
            .ToHashSet();

        var tagNames = AllImages
            .SelectMany(vm => vm.Model.Tags.Select(t => t.Name))
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        TagFilters.Clear();
        foreach (var name in tagNames)
        {
            var item = new TagFilterItem(name) { IsSelected = previouslySelected.Contains(name) };
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
        try
        {
            if (SelectedFolder == e.FolderPath)
                await SelectFolderAsync(e.FolderPath);
            else if (SelectedAlbum?.FolderPaths.Contains(e.FolderPath) == true)
                await SelectAlbumAsync(SelectedAlbum);
        }
        catch { /* FileSystemWatcher callbacks must not crash the process */ }
    }

    public bool HasMultipleSelected => FilteredImages.Count(x => x.IsSelected) > 1;

    private const double FullImageThreshold = 300.0;

    private static readonly string ThumbnailCacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PhotoExplorer", "thumbnails");

    private const int CacheThumbnailSize = 300;

    private static string GetCachePath(string filePath)
    {
        var lastWrite = File.GetLastWriteTimeUtc(filePath).Ticks;
        var key = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(filePath)));
        return Path.Combine(ThumbnailCacheDir, $"{key}_{lastWrite:x}.jpg");
    }

    private static BitmapSource? LoadThumbnailFast(string filePath)
    {
        try
        {
            var cachePath = GetCachePath(filePath);
            string source = File.Exists(cachePath) ? cachePath : filePath;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(source);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            if (source == filePath)
                bmp.DecodePixelWidth = CacheThumbnailSize;
            bmp.EndInit();
            bmp.Freeze();

            if (source == filePath)
                SaveCache(cachePath, bmp);

            return bmp;
        }
        catch { return null; }
    }

    private static void SaveCache(string cachePath, BitmapSource bmp)
    {
        try
        {
            var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = new FileStream(cachePath, FileMode.Create);
            encoder.Save(fs);
        }
        catch { }
    }

    partial void OnThumbnailSizeChanged(double value)
    {
        App.AppSettings.ThumbnailSize = value;

        if (value >= FullImageThreshold)
        {
            foreach (var vm in FilteredImages)
                vm.LoadFullImage();
        }
        else
        {
            foreach (var vm in FilteredImages)
                vm.ClearFullImage();
        }
    }
}
