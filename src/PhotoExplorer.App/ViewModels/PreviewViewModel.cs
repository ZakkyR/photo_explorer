using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoExplorer.Core.Models;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

namespace PhotoExplorer.App.ViewModels;

public partial class PreviewViewModel : ObservableObject
{
    private readonly IReadOnlyList<ImageItem> _images;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowErrorOverlay))]
    private int _currentIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowErrorOverlay))]
    private BitmapSource? _currentImage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowErrorOverlay))]
    private bool _isLoading;

    [ObservableProperty]
    private string _currentFileName = string.Empty;

    [ObservableProperty]
    private string _currentTags = string.Empty;

    public bool ShowErrorOverlay => CurrentImage == null && !IsLoading;

    public bool CanGoPrevious => CurrentIndex > 0;
    public bool CanGoNext => CurrentIndex < _images.Count - 1;

    public ImageItem CurrentItem => CurrentIndex >= 0 && CurrentIndex < _images.Count
        ? _images[CurrentIndex]
        : _images[0];

    public PreviewViewModel(IReadOnlyList<ImageItem> images, int initialIndex)
    {
        _images = images;
        _ = NavigateAsync(initialIndex);
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private Task Previous() => NavigateAsync(CurrentIndex - 1);

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private Task Next() => NavigateAsync(CurrentIndex + 1);

    private async Task NavigateAsync(int index)
    {
        if (index < 0 || index >= _images.Count) return;
        CurrentIndex = index;
        var item = _images[index];
        CurrentFileName = item.FileName;
        CurrentTags = item.Tags.Count > 0
            ? string.Join("  ", item.Tags.Select(t => $"[{t.Name}]"))
            : "(タグなし)";

        IsLoading = true;
        CurrentImage = await LoadPreviewAsync(item.FilePath);
        IsLoading = false;

        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(CurrentItem));
        PreviousCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
    }

    private static Task<BitmapSource?> LoadPreviewAsync(string filePath)
    {
        // BitmapDecoder.Create (WIC/STA) はサムネイル描画中に COM マーシャリングで61秒ブロックするため
        // ImageSharp (純粋マネージド) でデコードし UI スレッドで WriteableBitmap を作成する
        var tcs = new TaskCompletionSource<BitmapSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var img = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Bgra32>(filePath);
                var pixels = new byte[img.Width * img.Height * 4];
                img.CopyPixelDataTo(pixels);
                int w = img.Width, h = img.Height;

                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    var wb = new WriteableBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
                    wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
                    tcs.SetResult(wb);
                });
            }
            catch
            {
                tcs.SetResult(null);
            }
        });
        thread.IsBackground = true;
        thread.Name = "PreviewLoader";
        thread.Start();
        return tcs.Task;
    }

    public void RefreshTags()
    {
        var item = _images[CurrentIndex];
        CurrentTags = item.Tags.Count > 0
            ? string.Join("  ", item.Tags.Select(t => $"[{t.Name}]"))
            : "(タグなし)";
    }
}
