using Microsoft.Extensions.DependencyInjection;
using PhotoExplorer.App.ViewModels;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PhotoExplorer.App.Views;

public partial class ImageGridView : UserControl
{
    private Point _dragStartPoint;

    public ImageGridView() => InitializeComponent();

    private MainViewModel Vm => (MainViewModel)DataContext;

    private void Border_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => _dragStartPoint = e.GetPosition(null);

    private void Border_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(null);
        var diff = _dragStartPoint - pos;
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        if (sender is FrameworkElement fe && fe.DataContext is ImageItemViewModel vm)
        {
            var mainVm = Vm;
            var selectedItems = mainVm.FilteredImages.Where(x => x.IsSelected).ToList();
            string[] paths;
            if (selectedItems.Count > 1)
                paths = selectedItems.Select(x => x.Model.FilePath).ToArray();
            else
                paths = new[] { vm.Model.FilePath };

            var data = new DataObject(DataFormats.FileDrop, paths);
            DragDrop.DoDragDrop(fe, data, DragDropEffects.Copy);
        }
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not ImageItemViewModel vm)
            return;

        var mainVm = Vm;
        bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);

        if (e.ClickCount >= 2 && !ctrl)
        {
            var images = mainVm.FilteredImages.Select(i => i.Model).ToList();
            var index = images.IndexOf(vm.Model);
            var preview = new PreviewWindow(images, index);
            preview.Owner = Window.GetWindow(this);
            preview.Show();
            return;
        }

        if (ctrl)
        {
            vm.IsSelected = !vm.IsSelected;
        }
        else
        {
            foreach (var item in mainVm.FilteredImages)
                item.IsSelected = false;
            vm.IsSelected = true;
        }

        e.Handled = false;
    }

    private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl)) return;

        // e.Delta は標準マウス 1 ノッチで ±120。20px/ノッチ でエクスプローラーに近い操作感
        var newSize = Math.Clamp(Vm.ThumbnailSize + e.Delta / 6.0, 80, 500);
        Vm.ThumbnailSize = newSize;
        e.Handled = true; // ScrollViewer のスクロールをキャンセル
    }

    private async void TagMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var mainVm = Vm;
        var selected = mainVm.FilteredImages.Where(x => x.IsSelected).ToList();
        var tagService = App.Services.GetRequiredService<PhotoExplorer.Core.Services.ITagService>();

        // ContextMenu は別 Visual Tree で動くため PlacementTarget 経由で確実に vm を取得する
        var menuItem = sender as MenuItem;
        var contextMenu = menuItem?.Parent as ContextMenu;
        var placementBorder = contextMenu?.PlacementTarget as FrameworkElement;
        var targetVm = placementBorder?.DataContext as ImageItemViewModel;

        if (selected.Count > 1)
        {
            var dialog = new BulkTagEditDialog(selected.Select(x => x.Model).ToList(), tagService);
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true)
            {
                foreach (var selVm in selected)
                    selVm.Model.Tags = (await tagService.GetTagsAsync(selVm.Model.FilePath)).ToList();
                await mainVm.RefreshTagFiltersAsync();
                mainVm.ApplyTagFilter();
            }
        }
        else if (targetVm != null)
        {
            var dialog = new TagEditDialog(targetVm.Model, tagService);
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true)
            {
                targetVm.Model.Tags = (await tagService.GetTagsAsync(targetVm.Model.FilePath)).ToList();
                await mainVm.RefreshTagFiltersAsync();
                mainVm.ApplyTagFilter();
            }
        }
    }
}
