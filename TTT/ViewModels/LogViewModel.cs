// File: ViewModels/LogViewModel.cs

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TTT.Services;

namespace TTT.ViewModels;

public sealed partial class LogViewModel : BaseViewModel
{
    public ObservableCollection<LogEntryVm> Entries { get; } = [];

    public LogViewModel()
    {
        LogService.Instance.OnLogEntry += (msg, level) =>
            OnUI(() => Entries.Insert(0, new LogEntryVm(msg, level)));
    }

    [RelayCommand]
    private void ClearLog() => Entries.Clear();
}

/// <summary>Single log line for display.</summary>
public sealed record LogEntryVm(string Message, LogLevel Level)
{
    public string LevelColor => Level switch
    {
        LogLevel.Error => "#F44336",
        LogLevel.Warning => "#FF9800",
        LogLevel.Debug => "#B0B0B0",
        _ => "#FFFFFF"
    };
}
