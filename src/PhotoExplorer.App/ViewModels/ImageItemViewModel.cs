using CommunityToolkit.Mvvm.ComponentModel;
using PhotoExplorer.Core.Models;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace PhotoExplorer.App.ViewModels;

public partial class ImageItemViewModel : ObservableObject
{
    public ImageItem Model { get; }

    [ObservableProperty]
    private BitmapSource? _thumbnail;

    [ObservableProperty]
    private BitmapSource? _fullImage;

    [ObservableProperty]
    private bool _isSelected;

    public BitmapSource? DisplayImage => FullImage ?? Thumbnail;

    partial void OnFullImageChanged(BitmapSource? value) => OnPropertyChanged(nameof(DisplayImage));
    partial void OnThumbnailChanged(BitmapSource? value) => OnPropertyChanged(nameof(DisplayImage));

    public ImageItemViewModel(ImageItem model) => Model = model;

    public void SetThumbnailFromBytes(byte[]? bytes)
    {
        if (bytes == null) { Thumbnail = null; return; }
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = new MemoryStream(bytes);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        Thumbnail = bitmap;
    }

    public void LoadFullImage()
    {
        if (FullImage != null) return; // already loaded
        Task.Run(() =>
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(Model.FilePath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                Application.Current.Dispatcher.BeginInvoke(() => FullImage = bitmap);
            }
            catch
            {
                // leave FullImage as null — grid shows placeholder
            }
        });
    }

    public void ClearFullImage()
    {
        FullImage = null;
    }
}
