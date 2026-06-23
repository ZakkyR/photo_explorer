using PhotoExplorer.Core.Models;
using PhotoExplorer.Core.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace PhotoExplorer.App.Views;

public partial class TagEditDialog : Window
{
    private readonly ImageItem _item;
    private readonly ITagService _tagService;
    private bool _hasChanges;

    public string FileName => _item.FileName;
    public ObservableCollection<string> Tags { get; } = new();

    public TagEditDialog(ImageItem item, ITagService tagService)
    {
        _item = item;
        _tagService = tagService;
        DataContext = this;
        InitializeComponent();
        foreach (var t in item.Tags) Tags.Add(t.Name);
    }

    private async void AddTag_Click(object sender, RoutedEventArgs e)
    {
        var name = NewTagBox.Text.Trim();
        if (string.IsNullOrEmpty(name) || Tags.Contains(name)) return;
        await _tagService.AddTagAsync(_item.FilePath, name);
        Tags.Add(name);
        NewTagBox.Clear();
        _hasChanges = true;
    }

    private async void RemoveTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string tagName)
        {
            await _tagService.RemoveTagAsync(_item.FilePath, tagName);
            Tags.Remove(tagName);
            _hasChanges = true;
        }
    }

    private void NewTagBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        AddTag_Click(sender, e);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = _hasChanges;
    }
}
