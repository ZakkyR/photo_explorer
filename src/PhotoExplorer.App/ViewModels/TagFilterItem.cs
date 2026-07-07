using CommunityToolkit.Mvvm.ComponentModel;

namespace PhotoExplorer.App.ViewModels;

public partial class TagFilterItem : ObservableObject
{
    public string Name { get; }
    public bool IsUntaggedFilter { get; }

    [ObservableProperty]
    private bool _isSelected;

    public TagFilterItem(string name, bool isUntaggedFilter = false)
    {
        Name = name;
        IsUntaggedFilter = isUntaggedFilter;
    }

    partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke(this, EventArgs.Empty);

    public event EventHandler? SelectionChanged;
}
