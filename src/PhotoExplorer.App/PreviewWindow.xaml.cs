using Microsoft.Extensions.DependencyInjection;
using PhotoExplorer.App.ViewModels;
using PhotoExplorer.App.Views;
using PhotoExplorer.Core.Models;
using PhotoExplorer.Core.Services;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace PhotoExplorer.App;

public partial class PreviewWindow : Window
{
    private readonly PreviewViewModel _vm;

    public PreviewWindow(IReadOnlyList<ImageItem> images, int initialIndex)
    {
        InitializeComponent();
        _vm = new PreviewViewModel(images, initialIndex);
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
