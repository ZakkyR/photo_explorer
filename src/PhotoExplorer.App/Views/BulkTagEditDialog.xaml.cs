using PhotoExplorer.Core.Models;
using PhotoExplorer.Core.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace PhotoExplorer.App.Views;

public partial class BulkTagEditDialog : Window
{
    private readonly IReadOnlyList<ImageItem> _items;
    private readonly ITagService _tagService;

    public ObservableCollection<string> CommonTags { get; } = new();

    public BulkTagEditDialog(IReadOnlyList<ImageItem> items, ITagService tagService)
    {
        _items = items;
        _tagService = tagService;
        DataContext = this;
        InitializeComponent();

        Title = $"一括タグ編集 ({items.Count}枚)";
        TitleBlock.Text = $"選択中: {items.Count}枚";

        RefreshCommonTags();
    }

    private void RefreshCommonTags()
    {
        CommonTags.Clear();
        var allTagNames = _items.SelectMany(x => x.Tags).Select(t => t.Name).Distinct();
        var commonTags = allTagNames
            .Where(t => _items.All(x => x.Tags.Any(tag => tag.Name == t)))
            .OrderBy(t => t)
            .ToList();
        foreach (var tag in commonTags)
            CommonTags.Add(tag);
    }

    private async void AddTag_Click(object sender, RoutedEventArgs e)
    {
        var name = NewTagBox.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;

        foreach (var item in _items)
        {
            if (!item.Tags.Any(t => t.Name == name))
                await _tagService.AddTagAsync(item.FilePath, name);
        }

        // Refresh model tags
        foreach (var item in _items)
            item.Tags = (await _tagService.GetTagsAsync(item.FilePath)).ToList();

        NewTagBox.Clear();
        RefreshCommonTags();
        DialogResult = true;
    }

    private async void RemoveCommonTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string tagName)
        {
            foreach (var item in _items)
            {
                if (item.Tags.Any(t => t.Name == tagName))
                    await _tagService.RemoveTagAsync(item.FilePath, tagName);
            }

            // Refresh model tags
            foreach (var item in _items)
                item.Tags = (await _tagService.GetTagsAsync(item.FilePath)).ToList();

            RefreshCommonTags();
            DialogResult = true;
        }
    }

    private void NewTagBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddTag_Click(sender, e);
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
