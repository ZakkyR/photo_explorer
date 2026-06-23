using Microsoft.Extensions.DependencyInjection;
using PhotoExplorer.App.ViewModels;
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
            var data = new DataObject(DataFormats.FileDrop, new[] { vm.Model.FilePath });
            DragDrop.DoDragDrop(fe, data, DragDropEffects.Copy);
        }
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2) return;
        if (sender is FrameworkElement fe && fe.DataContext is ImageItemViewModel vm)
        {
            var images = Vm.FilteredImages.Select(i => i.Model).ToList();
            var index = images.IndexOf(vm.Model);
            var preview = new PreviewWindow(images, index);
            preview.Owner = Window.GetWindow(this);
            preview.Show();
        }
    }

    private async void TagMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ImageItemViewModel vm)
        {
            var tagService = App.Services.GetRequiredService<PhotoExplorer.Core.Services.ITagService>();
            var dialog = new TagEditDialog(vm.Model, tagService);
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true)
            {
                vm.Model.Tags = (await tagService.GetTagsAsync(vm.Model.FilePath)).ToList();
                Vm.ApplyTagFilter();
            }
        }
    }
}
