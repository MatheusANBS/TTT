using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TTT.Models;
using TTT.Services;
using TTT.Views;

namespace TTT.ViewModels;

public sealed partial class MainViewModel : BaseViewModel
{
    private readonly MemoryService _memory;
    private readonly ScannerService _scanner;
    private readonly PointerMapperService _pointerMapper;
    private readonly ConfigService _config;

    public ObservableCollection<MemoryEntry> AddressList { get; } = [];

    public ProcessViewModel ProcessVm { get; }
    public ScannerViewModel ScannerVm { get; }
    public AddressListViewModel AddressListVm { get; }
    public PointerMapperViewModel PointerMapperVm { get; }
    public LogViewModel LogVm { get; }

    [ObservableProperty] private object _currentView;
    [NotifyCanExecuteChangedFor(nameof(DisconnectProcessCommand))]
    [ObservableProperty] private bool _isAttached;
    [ObservableProperty] private string _attachedLabel = "Desconectado";

    public MainViewModel(
        MemoryService memory,
        ScannerService scanner,
        PointerMapperService pointerMapper,
        ConfigService config)
    {
        _memory = memory;
        _scanner = scanner;
        _pointerMapper = pointerMapper;
        _config = config;

        Action<string> snack = msg => LogService.Instance.Info($"UI: {msg}");
        SnackbarCallback = snack;

        ProcessVm = new ProcessViewModel(_memory) { SnackbarCallback = snack };
        ProcessVm.OnProcessAttached += HandleProcessAttached;

        ScannerVm = new ScannerViewModel(_scanner, AddressList) { SnackbarCallback = snack };
        ScannerVm.RequestNavigateToAddressList += NavigateToAddressList;

        AddressListVm = new AddressListViewModel(_memory, _config, AddressList) { SnackbarCallback = snack };
        AddressListVm.RequestOpenPointerMapper += HandlePointerMapperRequest;

        PointerMapperVm = new PointerMapperViewModel(_pointerMapper, _memory, AddressList) { SnackbarCallback = snack };
        LogVm = new LogViewModel { SnackbarCallback = snack };

        _currentView = ProcessVm;
    }

    private void HandleProcessAttached(string processName, bool attached)
    {
        IsAttached = attached;
        AttachedLabel = attached ? processName : "Desconectado";
        if (attached)
            ScannerVm.ResetScan();
    }

    private void HandlePointerMapperRequest(long address)
    {
        PointerMapperVm.TargetAddress = $"0x{address:X}";
        NavigateToPointerMapper();
    }

    [RelayCommand]
    private void NavigateToProcess() => CurrentView = ProcessVm;

    [RelayCommand]
    private void NavigateToScanner() => CurrentView = ScannerVm;

    [RelayCommand]
    private void NavigateToAddressList() => CurrentView = AddressListVm;

    [RelayCommand]
    private void NavigateToPointerMapper() => CurrentView = PointerMapperVm;

    [RelayCommand]
    private void NavigateToLog() => CurrentView = LogVm;

    [RelayCommand(CanExecute = nameof(CanDisconnectProcess))]
    private void DisconnectProcess()
    {
        if (!IsAttached)
            return;

        ProcessVm.DisconnectCommand.Execute(null);
    }

    private bool CanDisconnectProcess() => IsAttached;

    [RelayCommand]
    private void ShowHotkeysTutorial()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var dialog = new HotkeysTutorialWindow();
        dialog.ShowDialog(desktop.MainWindow!);
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task SaveConfigAsync()
    {
        var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "configs");
        System.IO.Directory.CreateDirectory(dir);
        var filePath = System.IO.Path.Combine(dir, "last.tttcfg");
        await SaveConfigToPathAsync(filePath);
    }

    public async System.Threading.Tasks.Task SaveConfigToPathAsync(string filePath)
    {
        await SafeRunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            var data = new ConfigData
            {
                ProcessName = _memory.AttachedProcessName ?? "",
                ModuleBase = _memory.MainModuleBase,
                AddressList = [.. AddressList.Select(ConfigService.ToDto)],
                PointerChains = [.. PointerMapperVm.AllResultsSnapshot],
                GroupHotkeys =
                [
                    .. AddressListVm.ExportGroupHotkeys().Select(h => new GroupHotkeyDto
                    {
                        GroupName = h.GroupName,
                        HotkeyText = h.HotkeyText,
                        Action = h.Action,
                        ActionValue = h.ActionValue,
                        IsEnabled = h.IsEnabled,
                    })
                ]
            };

            _config.SaveConfig(filePath, data);

            var settings = _config.LoadAppSettings();
            settings.LastConfigPath = filePath;
            _config.SaveAppSettings(settings);

            ShowSnackbar($"Configuracao salva em '{System.IO.Path.GetFileName(filePath)}'.");
            await System.Threading.Tasks.Task.CompletedTask;
        });
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task LoadConfigAsync()
    {
        var settings = _config.LoadAppSettings();
        var filePath = settings.LastConfigPath;
        if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
        {
            ShowSnackbar("Nenhuma configuracao salva encontrada.");
            return;
        }

        await LoadConfigFromPathAsync(filePath);
    }

    public async System.Threading.Tasks.Task LoadConfigFromPathAsync(string filePath)
    {
        await SafeRunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
            {
                ShowSnackbar("Arquivo de configuracao invalido.");
                return;
            }

            var data = _config.LoadConfig(filePath);
            AddressList.Clear();
            foreach (var dto in data.AddressList)
                AddressList.Add(ConfigService.FromDto(dto));

            PointerMapperVm.ReplaceAllResults(data.PointerChains);
            AddressListVm.ImportGroupHotkeys(data.GroupHotkeys.Select(h => new AddressListViewModel.GroupHotkeyExportItem
            {
                GroupName = h.GroupName,
                HotkeyText = h.HotkeyText,
                Action = h.Action,
                ActionValue = h.ActionValue,
                IsEnabled = h.IsEnabled,
            }));

            var settings = _config.LoadAppSettings();
            settings.LastConfigPath = filePath;
            _config.SaveAppSettings(settings);

            ShowSnackbar($"Configuracao carregada de '{System.IO.Path.GetFileName(filePath)}'.");
            await System.Threading.Tasks.Task.CompletedTask;
        });
    }
}
