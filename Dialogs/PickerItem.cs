using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AdManager.Dialogs;

public sealed class PickerItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public object Value { get; init; } = new();
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public string SearchBlob => (Title + " " + Subtitle).ToLowerInvariant();

    public event PropertyChangedEventHandler? PropertyChanged;
}
