// File: ViewModels/ProcessViewModel.cs

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TTT.Services;
using AvaloniaBrush = Avalonia.Media.Brush;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;

namespace TTT.ViewModels;

public sealed partial class ProcessViewModel : BaseViewModel
{
    private readonly MemoryService _memory;
    private readonly DispatcherTimer _attachedProcessWatchdog;
    private readonly Dictionary<string, AvaloniaBitmap?> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _iconCacheLock = new();

    public ObservableCollection<ProcessInfo> Processes { get; } = [];

    public bool HasProcesses => Processes.Count > 0;
    public bool HasNoProcesses => Processes.Count == 0;
    public string ProcessCountLabel => $"{Processes.Count} processo(s)";
    public bool IsNotRefreshing => !IsRefreshing;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AttachCommand))]
    private ProcessInfo? _selectedProcess;

    [ObservableProperty] private string _filterText = string.Empty;

    [ObservableProperty] private bool _showOnlyWithWindows = true;

    [ObservableProperty] private bool _isRefreshing;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    private bool _isAttached;

    [ObservableProperty] private string _connectedProcessLabel = "Desconectado";

    /// <summary>Fired when a process is successfully attached or detached.</summary>
    public event Action<string, bool>? OnProcessAttached;

    private ProcessInfo[] _allProcesses = [];

    public ProcessViewModel(MemoryService memory)
    {
        _memory = memory;
        Processes.CollectionChanged += OnProcessesCollectionChanged;

        _attachedProcessWatchdog = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _attachedProcessWatchdog.Tick += OnAttachedProcessWatchdogTick;
        _attachedProcessWatchdog.Start();

        UpdateAttachState();
        _ = RefreshProcessesAsync();
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();
    partial void OnShowOnlyWithWindowsChanged(bool value) => ApplyFilter();
    partial void OnIsRefreshingChanged(bool value) => OnPropertyChanged(nameof(IsNotRefreshing));

    private void OnProcessesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasProcesses));
        OnPropertyChanged(nameof(HasNoProcesses));
        OnPropertyChanged(nameof(ProcessCountLabel));
    }

    [RelayCommand]
    private async Task RefreshProcessesAsync()
    {
        if (IsRefreshing)
            return;

        IsRefreshing = true;
        try
        {
            var selectedPid = SelectedProcess?.Pid;
            await SafeRunAsync(async () =>
            {
                var processes = await Task.Run(LoadProcesses);
                OnUI(() =>
                {
                    _allProcesses = processes;
                    SelectedProcess = selectedPid is null
                        ? null
                        : _allProcesses.FirstOrDefault(process => process.Pid == selectedPid.Value);

                    SyncConnectedProcessState();
                    ApplyFilter();
                });
            });
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void ApplyFilter()
    {
        var filter = FilterText.Trim().ToLowerInvariant();
        Processes.Clear();
        foreach (var pi in _allProcesses)
        {
            if (ShowOnlyWithWindows && !pi.HasWindow)
                continue;

            if (string.IsNullOrEmpty(filter) ||
                pi.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                pi.Title.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                pi.Pid.ToString().Contains(filter))
                Processes.Add(pi);
        }
    }

    [RelayCommand(CanExecute = nameof(CanAttach))]
    private void Attach()
    {
        if (SelectedProcess is null)
            return;

        AttachProcess(SelectedProcess);
    }

    private bool CanAttach() => SelectedProcess is not null;

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private void Disconnect()
    {
        if (!_memory.IsAttached)
            return;

        var processName = _memory.AttachedProcessName;
        _memory.CloseCurrentHandle();
        OnProcessAttached?.Invoke(processName, false);
        UpdateAttachState();
        ShowSnackbar($"Desconectado: {processName}.");
    }

    private bool CanDisconnect() => _memory.IsAttached;

    [RelayCommand]
    private void ToggleProcessCard(ProcessInfo? process)
    {
        if (process is null)
            return;

        SelectedProcess = process;

        if (_memory.IsAttached && _memory.AttachedPid == process.Pid)
        {
            Disconnect();
            return;
        }

        AttachProcess(process);
    }

    private void OnAttachedProcessWatchdogTick(object? sender, EventArgs e)
    {
        if (!_memory.IsAttached || _memory.AttachedPid <= 0)
            return;

        bool stillAlive;
        try
        {
            using var proc = Process.GetProcessById(_memory.AttachedPid);
            stillAlive = !proc.HasExited;
        }
        catch
        {
            stillAlive = false;
        }

        if (stillAlive)
            return;

        var name = _memory.AttachedProcessName;
        _memory.CloseCurrentHandle();
        OnProcessAttached?.Invoke(name, false);
        UpdateAttachState();
        ShowSnackbar($"Processo encerrado: {name}. Desconectado automaticamente.");
        _ = RefreshProcessesAsync();
    }

    private void UpdateAttachState()
    {
        IsAttached = _memory.IsAttached;
        ConnectedProcessLabel = _memory.IsAttached
            ? $"{_memory.AttachedProcessName} (PID {_memory.AttachedPid})"
            : "Desconectado";

        SyncConnectedProcessState();
        DisconnectCommand.NotifyCanExecuteChanged();
    }

    private void AttachProcess(ProcessInfo process)
    {
        try
        {
            _memory.OpenHandle(process.Pid);
            OnProcessAttached?.Invoke(process.Name, true);
            UpdateAttachState();
            ShowSnackbar($"Conectado: {process.Name} (PID {process.Pid})");
        }
        catch (Models.MemoryException mex)
        {
            ShowSnackbar(mex.FriendlyMessage);
        }
    }

    private void SyncConnectedProcessState()
    {
        foreach (var process in _allProcesses)
            process.IsConnected = _memory.IsAttached && process.Pid == _memory.AttachedPid;
    }

    private ProcessInfo[] LoadProcesses()
    {
        var currentProcessId = Environment.ProcessId;
        var processes = new List<ProcessInfo>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == currentProcessId)
                    continue;

                string title = string.Empty;
                bool hasWindow = false;

                try { title = process.MainWindowTitle; } catch { }
                try { hasWindow = process.MainWindowHandle != IntPtr.Zero; } catch { }

                var icon = TryGetProcessIcon(process);
                processes.Add(new ProcessInfo(process.Id, process.ProcessName, title, icon, hasWindow));
            }
            finally
            {
                process.Dispose();
            }
        }

        return processes
            .OrderByDescending(process => process.HasWindow)
            .ThenBy(process => process.Name)
            .ToArray();
    }

    private AvaloniaBitmap? TryGetProcessIcon(Process process)
    {
        try
        {
            var filePath = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(filePath))
                return null;

            lock (_iconCacheLock)
            {
                if (_iconCache.TryGetValue(filePath, out var cachedIcon))
                    return cachedIcon;
            }

            AvaloniaBitmap? icon = null;
            using var sysIcon = Icon.ExtractAssociatedIcon(filePath);
            if (sysIcon is not null)
            {
                using var iconBitmap = sysIcon.ToBitmap();
                using var ms = new MemoryStream();
                iconBitmap.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                icon = new AvaloniaBitmap(ms);
            }

            lock (_iconCacheLock)
                _iconCache[filePath] = icon;

            return icon;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Display-only process entry.</summary>
public sealed partial class ProcessInfo : ObservableObject
{
    private static readonly IBrush DefaultCardBackground = AvaloniaBrush.Parse("#202A31");
    private static readonly IBrush ConnectedCardBackground = AvaloniaBrush.Parse("#21352D");
    private static readonly IBrush DefaultCardBorderBrush = AvaloniaBrush.Parse("#33414B");
    private static readonly IBrush ConnectedCardBorderBrush = AvaloniaBrush.Parse("#63B087");

    public ProcessInfo(int pid, string name, string title, AvaloniaBitmap? icon, bool hasWindow)
    {
        Pid = pid;
        Name = name;
        Title = title;
        Icon = icon;
        HasWindow = hasWindow;
    }

    public int Pid { get; }
    public string Name { get; }
    public string Title { get; }
    public AvaloniaBitmap? Icon { get; }
    public bool HasWindow { get; }
    public bool HasIcon => Icon is not null;
    public bool HasNoIcon => !HasIcon;
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name[..1].ToUpperInvariant();
    public string TitleOrFallback => string.IsNullOrWhiteSpace(Title) ? "Sem janela visível" : Title;
    public IBrush CardBackground => IsConnected ? ConnectedCardBackground : DefaultCardBackground;
    public IBrush CardBorderBrush => IsConnected ? ConnectedCardBorderBrush : DefaultCardBorderBrush;

    [ObservableProperty]
    private bool _isConnected;

    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(CardBackground));
        OnPropertyChanged(nameof(CardBorderBrush));
    }
}
