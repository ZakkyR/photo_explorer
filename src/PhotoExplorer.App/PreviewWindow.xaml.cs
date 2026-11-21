using Microsoft.Extensions.DependencyInjection;
using PhotoExplorer.App.ViewModels;
using PhotoExplorer.App.Views;
using PhotoExplorer.Core.Models;
using PhotoExplorer.Core.Services;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PhotoExplorer.App;

public partial class PreviewWindow : Window
{
    private readonly PreviewViewModel _vm;

    public PreviewWindow(IReadOnlyList<ImageItem> images, int initialIndex)
    {
        InitializeComponent();
        _vm = new PreviewViewModel(images, initialIndex);
        _vm.PropertyChanged += (_, e) =>
        {
            // 画像が切り替わったらズームをリセット
            if (e.PropertyName == nameof(PreviewViewModel.CurrentImage))
                ResetZoom();
        };
        DataContext = _vm;
        RestorePosition();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
                _vm.PreviousCommand.Execute(null);
                break;
            case Key.Right:
                _vm.NextCommand.Execute(null);
                break;
            case Key.Escape:
                Close();
                break;
            case Key.OemPlus:
            case Key.Add:
                ZoomAtCenter(ZoomStep);
                break;
            case Key.OemMinus:
            case Key.Subtract:
                ZoomAtCenter(1 / ZoomStep);
                break;
            case Key.D0:
            case Key.NumPad0:
                ResetZoom();
                break;
            case Key.D1:
            case Key.NumPad1:
                ZoomToActualSize(HostCenter());
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    // ───────── ズーム / パン ─────────
    // 倍率は「ウィンドウに合わせた表示サイズ」を 1.0 とする相対倍率で扱う

    private const double ZoomStep = 1.25;
    private const double MaxActualSizeMultiplier = 8.0; // 実寸の8倍まで
    private const double MinMaxZoom = 4.0;              // 小さい画像でも最低4倍までは拡大可

    private Point _panStart;
    private double _panStartX, _panStartY;
    private bool _isPanning;

    private double CurrentZoom => ZoomScale.ScaleX;

    /// <summary>実寸（画像1px = 画面1px）表示になる相対倍率</summary>
    private double ActualSizeZoom
    {
        get
        {
            if (_vm.CurrentImage is not { } bmp || PreviewImage.ActualWidth <= 0) return 1.0;
            var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            return bmp.PixelWidth / dpi / PreviewImage.ActualWidth;
        }
    }

    private double MaxZoom => Math.Max(MinMaxZoom, ActualSizeZoom * MaxActualSizeMultiplier);

    private Point HostCenter() => new(ImageHost.ActualWidth / 2, ImageHost.ActualHeight / 2);

    private void ResetZoom()
    {
        EndPan();
        ZoomScale.ScaleX = ZoomScale.ScaleY = 1.0;
        ZoomTranslate.X = ZoomTranslate.Y = 0;
        UpdateZoomUi();
    }

    /// <summary>ImageHost 座標の anchor を固定点として、倍率を newZoom にする</summary>
    private void SetZoom(double newZoom, Point anchor)
    {
        if (_vm.CurrentImage == null || PreviewImage.ActualWidth <= 0) return;

        newZoom = Math.Clamp(newZoom, 1.0, MaxZoom);
        if (newZoom <= 1.0 + 1e-9)
        {
            ResetZoom();
            return;
        }

        // anchor が指す画像上の点（変換前のローカル座標）を求め、拡大後も同じ画面位置に来るよう平行移動量を調整
        var origin = ImageOrigin();
        double s = CurrentZoom;
        double localX = (anchor.X - origin.X - ZoomTranslate.X) / s;
        double localY = (anchor.Y - origin.Y - ZoomTranslate.Y) / s;

        ZoomScale.ScaleX = ZoomScale.ScaleY = newZoom;
        ZoomTranslate.X = anchor.X - origin.X - localX * newZoom;
        ZoomTranslate.Y = anchor.Y - origin.Y - localY * newZoom;

        ClampTranslation();
        UpdateZoomUi();
    }

    private void ZoomAtCenter(double factor) => SetZoom(CurrentZoom * factor, HostCenter());

    private void ZoomToActualSize(Point anchor)
    {
        var target = ActualSizeZoom;
        // 実寸がウィンドウより小さい画像は、全体表示のままだと変化がないので2倍にする
        SetZoom(target <= 1.0 + 1e-9 ? 2.0 : target, anchor);
    }

    /// <summary>レイアウト上（変換前）の画像左上の位置（ImageHost 座標）</summary>
    private Point ImageOrigin() => new(
        (ImageHost.ActualWidth - PreviewImage.ActualWidth) / 2,
        (ImageHost.ActualHeight - PreviewImage.ActualHeight) / 2);

    /// <summary>拡大した画像が表示領域から外れすぎないように平行移動量を制限する</summary>
    private void ClampTranslation()
    {
        var origin = ImageOrigin();
        ZoomTranslate.X = ClampAxis(ZoomTranslate.X, origin.X, PreviewImage.ActualWidth * CurrentZoom, ImageHost.ActualWidth);
        ZoomTranslate.Y = ClampAxis(ZoomTranslate.Y, origin.Y, PreviewImage.ActualHeight * CurrentZoom, ImageHost.ActualHeight);
    }

    private static double ClampAxis(double translate, double origin, double scaledSize, double hostSize)
    {
        if (scaledSize <= hostSize)
            return (hostSize - scaledSize) / 2 - origin; // 収まる方向は中央寄せ
        double min = hostSize - scaledSize - origin;     // 右端/下端を領域端に合わせた位置
        double max = -origin;                            // 左端/上端を領域端に合わせた位置
        return Math.Clamp(translate, min, max);
    }

    private void UpdateZoomUi()
    {
        var hasImage = _vm.CurrentImage != null && PreviewImage.ActualWidth > 0;
        ZoomText.Text = hasImage ? $"{CurrentZoom / ActualSizeZoom * 100:0}%" : "";
        ImageHost.Cursor = CurrentZoom > 1.0 + 1e-9
            ? (_isPanning ? Cursors.ScrollAll : Cursors.Hand)
            : null;
    }

    private void ImageHost_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var factor = e.Delta > 0 ? ZoomStep : 1 / ZoomStep;
        SetZoom(CurrentZoom * factor, e.GetPosition(ImageHost));
        e.Handled = true;
    }

    private void ImageHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // ダブルクリック: 拡大中なら全体表示、全体表示なら実寸
            EndPan();
            if (CurrentZoom > 1.0 + 1e-9) ResetZoom();
            else ZoomToActualSize(e.GetPosition(ImageHost));
            e.Handled = true;
            return;
        }

        if (CurrentZoom <= 1.0 + 1e-9) return;
        _panStart = e.GetPosition(ImageHost);
        _panStartX = ZoomTranslate.X;
        _panStartY = ZoomTranslate.Y;
        _isPanning = ImageHost.CaptureMouse();
        UpdateZoomUi();
        e.Handled = true;
    }

    private void ImageHost_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;
        var p = e.GetPosition(ImageHost);
        ZoomTranslate.X = _panStartX + (p.X - _panStart.X);
        ZoomTranslate.Y = _panStartY + (p.Y - _panStart.Y);
        ClampTranslation();
    }

    private void ImageHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => EndPan();

    private void ImageHost_LostMouseCapture(object sender, MouseEventArgs e)
    {
        _isPanning = false;
        UpdateZoomUi();
    }

    private void EndPan()
    {
        if (!_isPanning) return;
        _isPanning = false;
        ImageHost.ReleaseMouseCapture();
    }

    private void ImageHost_SizeChanged(object sender, SizeChangedEventArgs e) => ClampTranslation();

    private void PreviewImage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ClampTranslation();
        UpdateZoomUi();
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomAtCenter(ZoomStep);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomAtCenter(1 / ZoomStep);
    private void ZoomFit_Click(object sender, RoutedEventArgs e) => ResetZoom();
    private void ZoomActual_Click(object sender, RoutedEventArgs e) => ZoomToActualSize(HostCenter());

    private void OpenExternal_Click(object sender, RoutedEventArgs e)
    {
        var path = _vm.CurrentItem.FilePath;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"既定のアプリで開けませんでした。\n{ex.Message}", "エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RestorePosition()
    {
        var s = App.AppSettings;
        Left = s.PreviewLeft;
        Top = s.PreviewTop;
        Width = s.PreviewWidth;
        Height = s.PreviewHeight;
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        var s = App.AppSettings;
        s.PreviewLeft = Left;
        s.PreviewTop = Top;
        s.PreviewWidth = Width;
        s.PreviewHeight = Height;
        s.Save();
    }

    private async void AddTag_Click(object sender, RoutedEventArgs e)
    {
        var tagService = App.Services.GetRequiredService<ITagService>();
        var dialog = new TagEditDialog(_vm.CurrentItem, tagService);
        dialog.Owner = this;
        dialog.ShowDialog();

        // Refresh tags from the service after dialog closes
        var updatedTags = await tagService.GetTagsAsync(_vm.CurrentItem.FilePath);
        _vm.CurrentItem.Tags = updatedTags.ToList();
        _vm.RefreshTags();
    }
}
