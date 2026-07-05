using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace PhotoExplorer.App;

public class AppStatus : INotifyPropertyChanged
{
    private string _statusMessage = string.Empty;
    private readonly object _ctLock = new();
    private CancellationTokenSource? _clearCts;

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            _statusMessage = value;
            var d = Application.Current?.Dispatcher;
            if (d is null || d.CheckAccess()) Notify();
            else d.BeginInvoke(Notify);
        }
    }

    public void Set(string message, bool autoClear = false)
    {
        CancellationTokenSource? oldCts;
        CancellationTokenSource? newCts = autoClear ? new() : null;
        lock (_ctLock) { oldCts = _clearCts; _clearCts = newCts; }
        oldCts?.Cancel();
        StatusMessage = message;
        if (newCts == null) return;
        var token = newCts.Token;
        _ = Task.Delay(3000, token).ContinueWith(t =>
        {
            if (!t.IsCanceled) StatusMessage = string.Empty;
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusMessage)));
}
