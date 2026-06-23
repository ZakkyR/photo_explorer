using CommunityToolkit.Mvvm.ComponentModel;
using PhotoExplorer.Core.Models;
using System.IO;
using System.Windows.Media.Imaging;

namespace PhotoExplorer.App.ViewModels;

public partial class ImageItemViewModel : ObservableObject
{
    public ImageItem Model { get; }

    [ObservableProperty]
    private BitmapSource? _thumbnail;

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
}
