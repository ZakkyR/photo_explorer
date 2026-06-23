using CommunityToolkit.Mvvm.ComponentModel;

namespace PhotoExplorer.App.ViewModels;

public partial class TagFilterItem : ObservableObject
{
    public string Name { get; }

    [ObservableProperty]
    private bool _isSelected;

    public TagFilterItem(string name) => Name = name;

    partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke(this, EventArgs.Empty);

    public event EventHandler? SelectionChanged;
}
