using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoExplorer.Core.Models;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Imaging;

namespace PhotoExplorer.App.ViewModels;

public partial class PreviewViewModel : ObservableObject
{
    private readonly IReadOnlyList<ImageItem> _images;

    [ObservableProperty]
    private int _currentIndex;

    [ObservableProperty]
    private BitmapSource? _currentImage;

    [ObservableProperty]
    private string _currentFileName = string.Empty;

    [ObservableProperty]
    private string _currentTags = string.Empty;

    public bool CanGoPrevious => CurrentIndex > 0;
    public bool CanGoNext => CurrentIndex < _images.Count - 1;

    public ImageItem CurrentItem => _images[CurrentIndex];

    public PreviewViewModel(IReadOnlyList<ImageItem> images, int initialIndex)
    {
        _images = images;
        Navigate(initialIndex);
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void Previous() => Navigate(CurrentIndex - 1);

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next() => Navigate(CurrentIndex + 1);

    private void Navigate(int index)
    {
        if (index < 0 || index >= _images.Count) return;
        CurrentIndex = index;
        var item = _images[index];
        CurrentFileName = item.FileName;
        CurrentTags = item.Tags.Count > 0
            ? string.Join("  ", item.Tags.Select(t => $"[{t.Name}]"))
            : "(タグなし)";

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(item.FilePath);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            CurrentImage = bitmap;
        }
        catch { CurrentImage = null; }

        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(CurrentItem));
        PreviousCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
    }

    public void RefreshTags()
    {
        var item = _images[CurrentIndex];
        CurrentTags = item.Tags.Count > 0
            ? string.Join("  ", item.Tags.Select(t => $"[{t.Name}]"))
            : "(タグなし)";
    }
}
