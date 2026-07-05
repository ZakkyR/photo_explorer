using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PhotoExplorer.Core.Models;
using PhotoExplorer.Core.Services;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
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
    private readonly ISidecarService _sidecarService;

    public ObservableCollection<FolderInfo> Folders { get; } = new();
    public ObservableCollection<Album> Albums { get; } = new();
    public ObservableRangeCollection<ImageItemViewModel> AllImages { get; } = new();
    public ObservableRangeCollection<ImageItemViewModel> FilteredImages { get; } = new();
    public ObservableRangeCollection<TagFilterItem> TagFilters { get; } = new();

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
        ITagService tagService,
        ISidecarService sidecarService)
    {
        _folderService = folderService;
        _albumService = albumService;
        _imageService = imageService;
        _tagService = tagService;
        _sidecarService = sidecarService;

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
        if (SelectedFolder == path)
        {
            _sidecarService.StopWatching(path);
            AllImages.Clear();
            FilteredImages.Clear();
            SelectedFolder = null;
            SelectedFolderPath = string.Empty;
        }
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

    public async Task ApplyFolderRenameAsync(string folderPath, string newName)
    {
        var current = Folders.FirstOrDefault(f => f.Path == folderPath);
        var displayName = string.IsNullOrWhiteSpace(newName) ? null : newName.Trim();
        await _folderService.RenameFolderAsync(folderPath, displayName ?? string.Empty);

        var idx = current != null ? Folders.IndexOf(current) : -1;
        if (idx >= 0)
            Folders[idx] = new FolderInfo(folderPath, displayName);
    }

    public async Task SelectFolderAsync(string path)
    {
        if (SelectedFolder != null) _sidecarService.StopWatching(SelectedFolder);

        SelectedFolder = path;
        SelectedAlbum = null;
        SelectedFolderPath = path;
        App.AppSettings.LastSelectedFolder = path;
        await LoadImagesAsync(await _imageService.LoadImagesFromFolderAsync(path, _tagService));

        _sidecarService.StartWatching(path, async _ =>
        {
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                await LoadImagesAsync(
                    await _imageService.LoadImagesFromFolderAsync(path, _tagService));
            });
        });
    }

    public async Task SelectAlbumAsync(Album album)
    {
        if (SelectedFolder != null) _sidecarService.StopWatching(SelectedFolder);
        SelectedAlbum = album;
        SelectedFolder = null;
        SelectedFolderPath = string.Empty;
        await LoadImagesAsync(await _imageService.LoadImagesFromAlbumAsync(album, _tagService));
    }

    private async Task LoadImagesAsync(IReadOnlyList<ImageItem> items)
    {
        IsLoading = true;
        var vms = items.Select(i => new ImageItemViewModel(i)).ToList();
        AllImages.ReplaceAll(vms);

        await RefreshTagFiltersAsync();
        ApplyTagFilter();

        IsLoading = false;

        Directory.CreateDirectory(ThumbnailCacheDir);
        // 同時4枚に絞って並列読み込み。全枚同時展開すると大量メモリを消費するため SemaphoreSlim で制御
        _ = Task.Run(async () =>
        {
            using var sem = new SemaphoreSlim(4);
            await Task.WhenAll(vms.Select(vm => Task.Run(async () =>
            {
                await sem.WaitAsync();
                (byte[]? pixels, int w, int h) result;
                try { result = LoadThumbnailPixels(vm.Model.FilePath); }
                finally { sem.Release(); }
                if (result.pixels == null) return;
                await Application.Current.Dispatcher.InvokeAsync(
                    () =>
                    {
                        var wb = new WriteableBitmap(result.w, result.h, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
                        wb.WritePixels(new System.Windows.Int32Rect(0, 0, result.w, result.h), result.pixels, result.w * 4, 0);
                        vm.Thumbnail = wb;
                    },
                    System.Windows.Threading.DispatcherPriority.Background);
            })));
        });

        if (ThumbnailSize >= FullImageThreshold && FilteredImages.Count <= FullImageMaxCount)
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

        var newFilters = tagNames.Select(name =>
        {
            var item = new TagFilterItem(name) { IsSelected = previouslySelected.Contains(name) };
            item.SelectionChanged += (_, _) => ApplyTagFilter();
            return item;
        });
        TagFilters.ReplaceAll(newFilters);

        return Task.CompletedTask;
    }

    public void ApplyTagFilter()
    {
        var selectedTags = TagFilters.Where(t => t.IsSelected).Select(t => t.Name).ToHashSet();
        var filtered = AllImages.Where(vm =>
            selectedTags.Count == 0 || vm.Model.Tags.Any(t => selectedTags.Contains(t.Name)));
        FilteredImages.ReplaceAll(filtered);
    }

    private void OnFolderChanged(object? sender, FolderChangedEventArgs e)
    {
        if (SelectedFolder != e.FolderPath &&
            SelectedAlbum?.FolderPaths.Contains(e.FolderPath) != true) return;

        // FSW は ThreadPool スレッドで発火するため UI スレッドへディスパッチ
        Application.Current.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                switch (e.ChangeType)
                {
                    case WatcherChangeTypes.Created:
                        await AddImageAsync(e.FilePath);
                        break;
                    case WatcherChangeTypes.Deleted:
                        RemoveImage(e.FilePath);
                        break;
                    case WatcherChangeTypes.Renamed:
                        if (e.OldFilePath != null) RemoveImage(e.OldFilePath);
                        await AddImageAsync(e.FilePath);
                        break;
                }
            }
            catch { }
        }));
    }

    private async Task AddImageAsync(string filePath)
    {
        var tagsBulk = await _tagService.GetTagsBulkAsync(new List<string> { filePath });
        var item = new ImageItem(filePath)
        {
            Tags = tagsBulk.TryGetValue(filePath, out var t) ? t : new List<Tag>()
        };
        var vm = new ImageItemViewModel(item);
        AllImages.Add(vm);

        Directory.CreateDirectory(ThumbnailCacheDir);
        _ = Task.Run(() =>
        {
            var (pixels, width, height) = LoadThumbnailPixels(filePath);
            if (pixels != null)
            {
                Application.Current.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() =>
                    {
                        var wb = new WriteableBitmap(width, height, 96, 96,
                            System.Windows.Media.PixelFormats.Bgra32, null);
                        wb.WritePixels(new System.Windows.Int32Rect(0, 0, width, height),
                            pixels, width * 4, 0);
                        vm.Thumbnail = wb;
                    }));
            }
        });

        await RefreshTagFiltersAsync();
        ApplyTagFilter();
    }

    private void RemoveImage(string filePath)
    {
        var vm = AllImages.FirstOrDefault(v =>
            string.Equals(v.Model.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (vm == null) return;
        AllImages.Remove(vm);
        FilteredImages.Remove(vm);
        _ = RefreshTagFiltersAsync();
    }

    public bool HasMultipleSelected => FilteredImages.Count(x => x.IsSelected) > 1;

    private const double FullImageThreshold = 300.0;
    private const int FullImageMaxCount = 20;

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

    private static (byte[]? pixels, int width, int height) LoadThumbnailPixels(string filePath)
    {
        try
        {
            var cachePath = GetCachePath(filePath);
            if (!File.Exists(cachePath))
            {
                using (var img = SixLabors.ImageSharp.Image.Load(filePath))
                {
                    img.Mutate(x => x.Resize(new ResizeOptions
                    {
                        Size = new SixLabors.ImageSharp.Size(CacheThumbnailSize, CacheThumbnailSize),
                        Mode = SixLabors.ImageSharp.Processing.ResizeMode.Max
                    }));
                    using var ms = new MemoryStream();
                    img.Save(ms, new JpegEncoder { Quality = 85 });
                    ms.Position = 0;
                    using var fs = new FileStream(cachePath, FileMode.Create);
                    ms.CopyTo(fs);
                }
            }

            using var cached = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Bgra32>(cachePath);
            var pixels = new byte[cached.Width * cached.Height * 4];
            cached.CopyPixelDataTo(pixels);
            return (pixels, cached.Width, cached.Height);
        }
        catch { return (null, 0, 0); }
    }

    partial void OnThumbnailSizeChanged(double value)
    {
        App.AppSettings.ThumbnailSize = value;

        if (value >= FullImageThreshold && FilteredImages.Count <= FullImageMaxCount)
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
