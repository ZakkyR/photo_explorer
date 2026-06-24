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
    private bool _hasChanges;

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
        AddButton.IsEnabled = false;
        try
        {
            var targets = _items
                .Where(item => !item.Tags.Any(t => t.Name == name))
                .Select(item => item.FilePath)
                .ToList();
            await _tagService.AddTagBulkAsync(targets, name);

            // DB 書き込み後はメモリ内リストを直接更新（ファイル再読み不要）
            var newTag = new Tag(name);
            foreach (var item in _items)
                if (!item.Tags.Any(t => t.Name == name))
                    item.Tags = item.Tags.Append(newTag).OrderBy(t => t.Name).ToList();

            NewTagBox.Clear();
            RefreshCommonTags();
            _hasChanges = true;
        }
        finally
        {
            AddButton.IsEnabled = true;
            NewTagBox.Focus();
        }
    }

    private async void RemoveCommonTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string tagName)
        {
            var targets = _items
                .Where(item => item.Tags.Any(t => t.Name == tagName))
                .Select(item => item.FilePath)
                .ToList();
            await _tagService.RemoveTagBulkAsync(targets, tagName);

            // DB 削除後はメモリ内リストを直接更新（ファイル再読み不要）
            foreach (var item in _items)
                item.Tags = item.Tags.Where(t => t.Name != tagName).ToList();

            RefreshCommonTags();
            _hasChanges = true;
        }
    }

    private void NewTagBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        AddTag_Click(sender, e);
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = _hasChanges;
    }
}
