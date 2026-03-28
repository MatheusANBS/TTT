// File: ViewModels/AddressListViewModel.cs

using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TTT.Models;
using TTT.Services;
using TTT.Utils;

namespace TTT.ViewModels;

public sealed partial class AddressListViewModel : BaseViewModel, IDisposable
{
    public enum GroupHotkeyAction
    {
        SetValue,
        IncreaseBy,
        DecreaseBy,
        ToggleFreeze
    }

    public sealed class GroupHotkeyConfig
    {
        public required string GroupName { get; init; }
        public required string HotkeyText { get; set; }
        public required GroupHotkeyAction Action { get; set; }
        public string ActionValue { get; set; } = "1";
        public bool IsEnabled { get; set; } = true;
    }

    public sealed class GroupHotkeyExportItem
    {
        public required string GroupName { get; init; }
        public required string HotkeyText { get; init; }
        public required string Action { get; init; }
        public string ActionValue { get; init; } = "1";
        public bool IsEnabled { get; init; } = true;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;

    public sealed partial class AddressGroupSection : ObservableObject
    {
        public AddressGroupSection(string name)
        {
            Name = name;
        }

        [ObservableProperty] private string _name;
        [ObservableProperty] private bool _isExpanded = true;
        [ObservableProperty] private bool _hasFrozenEntries;
        [ObservableProperty] private bool _hasHotkey;
        [ObservableProperty] private string _hotkeySummary = string.Empty;
        public ObservableCollection<MemoryEntry> Entries { get; } = [];
    }

    private readonly MemoryService                   _memory;
    private readonly ConfigService                   _config;
    private readonly ObservableCollection<MemoryEntry> _list;
    private readonly HashSet<string>                 _customGroups = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GroupHotkeyConfig> _groupHotkeyConfigs = new(StringComparer.OrdinalIgnoreCase);
    private readonly object                          _groupHotkeyLock = new();
    private readonly Dictionary<string, bool>        _hotkeyPressedMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource         _hotkeyCts = new();
    private readonly Task                            _hotkeyWorker;
    private readonly List<MemoryEntry>               _pendingGroupMoveEntries = [];
    private readonly DispatcherTimer                 _refreshTimer;
    private readonly object                          _freeze = new();
    private int _isRefreshRunning;

    // ── Navigation helper ─────────────────────────────────────────────────
    public event Action<long>? RequestOpenPointerMapper;
    public event Action? GroupingChanged;

    // ── UI state ──────────────────────────────────────────────────────────
    public ObservableCollection<MemoryEntry> AddressList => _list;
    public ObservableCollection<AddressGroupSection> GroupedAddressList { get; } = [];
    public ObservableCollection<string> GroupOptions { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(FindPointersCommand))]
    private MemoryEntry? _selectedEntry;

    public AddressListViewModel(
        MemoryService memory,
        ConfigService config,
        ObservableCollection<MemoryEntry> list)
    {
        _memory = memory;
        _config = config;
        _list   = list;

        _list.CollectionChanged += OnAddressListCollectionChanged;
        foreach (var entry in _list)
            entry.PropertyChanged += OnEntryPropertyChanged;

        RebuildGroupedAddressList();
        RefreshGroupOptions();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Constants.ADDRESS_LIST_TIMER_MS)
        };
        _refreshTimer.Tick += OnRefreshTick;
        _refreshTimer.Start();

        _hotkeyWorker = Task.Run(() => HotkeyWorkerLoopAsync(_hotkeyCts.Token));
    }

    // ── Refresh timer ─────────────────────────────────────────────────────
    private bool _editingPaused;

    public void PauseRefresh()  => _editingPaused = true;
    public void ResumeRefresh() => _editingPaused = false;

    private void OnRefreshTick(object? sender, EventArgs e)
    {
        if (!_editingPaused)
            _ = RefreshValuesAsync();
    }

    [RelayCommand]
    private void RefreshValues()
    {
        _ = RefreshValuesAsync();
    }

    private async Task RefreshValuesAsync()
    {
        if (!_memory.IsAttached) return;
        if (Interlocked.Exchange(ref _isRefreshRunning, 1) == 1) return;

        try
        {
            var snapshot = _list.ToList();
            var updates = await Task.Run(() =>
            {
                var result = new List<(MemoryEntry Entry, long ResolvedAddress, string? Value)>(snapshot.Count);

                foreach (var entry in snapshot)
                {
                    try
                    {
                        long addr = entry.IsPointerChain
                            ? _memory.ResolvePointerChain(entry.Address, entry.Offsets)
                            : entry.Address;

                        string? value = null;
                        if (addr != 0)
                        {
                            var bytes = _memory.ReadBytes(addr, entry.Type.ByteSize());
                            if (bytes is not null)
                                value = bytes.ReadValueAs(entry.Type);
                        }

                        if (entry.IsFrozen && entry.FrozenValue is { Length: > 0 } && addr != 0)
                        {
                            lock (_freeze)
                                _memory.WriteBytes(addr, entry.FrozenValue);
                        }

                        result.Add((entry, addr, value));
                    }
                    catch
                    {
                        // Keep refresh resilient even when some addresses become invalid.
                    }
                }

                return result;
            });

            OnUI(() =>
            {
                foreach (var (entry, resolvedAddress, value) in updates)
                {
                    entry.ResolvedAddress = resolvedAddress;
                    if (value is not null)
                        entry.CurrentValue = value;
                }
            });
        }
        finally
        {
            Interlocked.Exchange(ref _isRefreshRunning, 0);
        }
    }

    // ── CRUD commands ─────────────────────────────────────────────────────

    // ── Multi-edit logic ──────────────────────────────────────────────────
    [ObservableProperty] private bool _isEditDialogOpen;
    [ObservableProperty] private string _editDialogTitle = string.Empty;
    [ObservableProperty] private string _editDialogValue = string.Empty;
    [ObservableProperty] private bool _isGroupPickerOpen;
    [ObservableProperty] private string _groupPickerTitle = string.Empty;
    [ObservableProperty] private string? _selectedGroupOption;
    [ObservableProperty] private bool _isGroupHotkeyDialogOpen;
    [ObservableProperty] private string _groupHotkeyDialogTitle = string.Empty;
    [ObservableProperty] private string _groupHotkeyGroupName = string.Empty;
    [ObservableProperty] private bool _groupHotkeyEnabled = true;
    [ObservableProperty] private string _groupHotkeyText = string.Empty;
    [ObservableProperty] private GroupHotkeyAction _selectedGroupHotkeyAction = GroupHotkeyAction.SetValue;
    [ObservableProperty] private string _groupHotkeyValue = "1";
    [ObservableProperty] private bool _hasGroupHotkeyInDialog;
    [ObservableProperty] private bool _isHotkeyCaptureArmed;
    [ObservableProperty] private string _groupHotkeyCaptureHint = "Clique em Gravar e pressione a combinação.";
    private Action<string>? _onEditDialogCommit;

    public IReadOnlyList<GroupHotkeyAction> GroupHotkeyActions { get; } = Enum.GetValues<GroupHotkeyAction>();

    [RelayCommand]
    private void CloseEditDialog() => IsEditDialogOpen = false;

    [RelayCommand]
    private void CloseGroupPicker()
    {
        IsGroupPickerOpen = false;
        _pendingGroupMoveEntries.Clear();
    }

    [RelayCommand]
    private void CloseGroupHotkeyDialog()
    {
        IsHotkeyCaptureArmed = false;
        GroupHotkeyCaptureHint = "Clique em Gravar e pressione a combinação.";
        IsGroupHotkeyDialogOpen = false;
    }

    [RelayCommand]
    private void CommitEditDialog()
    {
        IsEditDialogOpen = false;
        _onEditDialogCommit?.Invoke(EditDialogValue);
    }

    [RelayCommand]
    private void CommitGroupPicker()
    {
        var groupName = SelectedGroupOption?.Trim();
        if (string.IsNullOrWhiteSpace(groupName))
            return;

        RegisterCustomGroup(groupName);

        if (_pendingGroupMoveEntries.Count > 0)
        {
            foreach (var item in _pendingGroupMoveEntries)
                item.GroupName = groupName;

            GroupingChanged?.Invoke();
        }

        IsGroupPickerOpen = false;
        _pendingGroupMoveEntries.Clear();
    }

    [RelayCommand]
    private void CreateGroup()
    {
        EditDialogTitle = "Criar novo grupo";
        EditDialogValue = string.Empty;
        _onEditDialogCommit = (newVal) =>
        {
            var groupName = newVal?.Trim();
            if (string.IsNullOrWhiteSpace(groupName))
            {
                ShowSnackbar("Informe um nome de grupo válido.");
                return;
            }

            RegisterCustomGroup(groupName);
            ShowSnackbar($"Grupo '{groupName}' criado.");
        };
        IsEditDialogOpen = true;
    }

    [RelayCommand]
    private void OpenGroupHotkeyDialog(string? groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            return;

        var normalized = groupName.Trim();
        GroupHotkeyGroupName = normalized;
        GroupHotkeyDialogTitle = $"Hotkey Global - {normalized}";

        GroupHotkeyConfig? existing;
        lock (_groupHotkeyLock)
            _groupHotkeyConfigs.TryGetValue(normalized, out existing);

        if (existing is not null)
        {
            GroupHotkeyEnabled = existing.IsEnabled;
            GroupHotkeyText = existing.HotkeyText;
            SelectedGroupHotkeyAction = existing.Action;
            GroupHotkeyValue = existing.ActionValue;
            HasGroupHotkeyInDialog = true;
        }
        else
        {
            GroupHotkeyEnabled = true;
            GroupHotkeyText = string.Empty;
            SelectedGroupHotkeyAction = GroupHotkeyAction.SetValue;
            GroupHotkeyValue = "1";
            HasGroupHotkeyInDialog = false;
        }

        IsHotkeyCaptureArmed = false;
        GroupHotkeyCaptureHint = "Clique em Gravar e pressione a combinação.";

        IsGroupHotkeyDialogOpen = true;
    }

    [RelayCommand]
    private void StartGroupHotkeyCapture()
    {
        IsHotkeyCaptureArmed = true;
        GroupHotkeyCaptureHint = "Gravando... pressione teclas e/ou botão do mouse.";
    }

    [RelayCommand]
    private void CancelGroupHotkeyCapture()
    {
        IsHotkeyCaptureArmed = false;
        GroupHotkeyCaptureHint = "Captura cancelada.";
    }

    [RelayCommand]
    private void SaveGroupHotkeyDialog()
    {
        var groupName = GroupHotkeyGroupName?.Trim();
        if (string.IsNullOrWhiteSpace(groupName))
            return;

        if (!GroupHotkeyEnabled)
        {
            lock (_groupHotkeyLock)
                _groupHotkeyConfigs.Remove(groupName);

            HasGroupHotkeyInDialog = false;
            IsGroupHotkeyDialogOpen = false;
            RebuildGroupedAddressList();
            ShowSnackbar($"Hotkey removida para o grupo '{groupName}'.");
            return;
        }

        if (!TryParseHotkey(GroupHotkeyText, out _))
        {
            ShowSnackbar("Hotkey inválida. Exemplo: Ctrl+Shift+F1");
            return;
        }

        if (SelectedGroupHotkeyAction is GroupHotkeyAction.SetValue or GroupHotkeyAction.IncreaseBy or GroupHotkeyAction.DecreaseBy)
        {
            if (string.IsNullOrWhiteSpace(GroupHotkeyValue))
            {
                ShowSnackbar("Informe o valor para a ação configurada.");
                return;
            }
        }

        var config = new GroupHotkeyConfig
        {
            GroupName = groupName,
            HotkeyText = GroupHotkeyText.Trim(),
            Action = SelectedGroupHotkeyAction,
            ActionValue = GroupHotkeyValue.Trim(),
            IsEnabled = true
        };

        lock (_groupHotkeyLock)
            _groupHotkeyConfigs[groupName] = config;

        IsHotkeyCaptureArmed = false;
        GroupHotkeyCaptureHint = "Clique em Gravar e pressione a combinação.";
        HasGroupHotkeyInDialog = true;
        IsGroupHotkeyDialogOpen = false;
        RebuildGroupedAddressList();
        ShowSnackbar($"Hotkey salva para o grupo '{groupName}'.");
    }

    [RelayCommand]
    private void RemoveGroupHotkeyDialog()
    {
        var groupName = GroupHotkeyGroupName?.Trim();
        if (string.IsNullOrWhiteSpace(groupName))
            return;

        lock (_groupHotkeyLock)
            _groupHotkeyConfigs.Remove(groupName);

        IsHotkeyCaptureArmed = false;
        GroupHotkeyCaptureHint = "Clique em Gravar e pressione a combinação.";
        HasGroupHotkeyInDialog = false;
        IsGroupHotkeyDialogOpen = false;
        RebuildGroupedAddressList();
        ShowSnackbar($"Hotkey removida do grupo '{groupName}'.");
    }

    [RelayCommand]
    private void EditGroupValues(string? groupName)
    {
        if (string.IsNullOrEmpty(groupName)) return;
        var items = _list.Where(e => e.GroupName == groupName).ToList();
        if (items.Count == 0) return;

        EditDialogTitle = $"Alterar Valor — grupo '{groupName}' ({items.Count} entradas)";
        EditDialogValue = items[0].CurrentValue;
        _onEditDialogCommit = (newVal) =>
        {
            foreach (var item in items)
            {
                item.CurrentValue = newVal;
                WriteValue(item);
            }
        };
        IsEditDialogOpen = true;
    }

    [RelayCommand]
    private void ToggleFreezeGroup(string? groupName)
    {
        if (string.IsNullOrEmpty(groupName)) return;
        var items = _list.Where(e => e.GroupName == groupName).ToList();
        if (items.Count == 0) return;

        // If ANY item is unfrozen, freeze all; otherwise unfreeze all
        bool freezeAll = items.Any(e => !e.IsFrozen);

        if (freezeAll && !_memory.IsAttached)
        {
            ShowSnackbar("Conecte-se a um processo antes de congelar valores.");
            return;
        }

        foreach (var entry in items)
        {
            if (freezeAll)
            {
                try
                {
                    long addr = entry.ResolvedAddress != 0 ? entry.ResolvedAddress : entry.Address;
                    var bytes = _memory.ReadBytes(addr, entry.Type.ByteSize());
                    entry.FrozenValue = bytes;
                    entry.IsFrozen = bytes is { Length: > 0 };
                }
                catch (MemoryException mex)
                {
                    ShowSnackbar(mex.FriendlyMessage);
                    entry.IsFrozen = false;
                    entry.FrozenValue = null;
                }
            }
            else
            {
                entry.IsFrozen    = false;
                entry.FrozenValue = null;
            }
        }
    }

    [RelayCommand]
    private void DeleteGroup(string? groupName)
    {
        if (string.IsNullOrEmpty(groupName)) return;

        var items = _list.Where(e => e.GroupName == groupName).ToList();
        foreach (var item in items)
            _list.Remove(item);

        // Remove custom empty groups as well; keep default group available.
        if (!string.Equals(groupName, "Geral", StringComparison.OrdinalIgnoreCase))
            _customGroups.RemoveWhere(g => string.Equals(g, groupName, StringComparison.OrdinalIgnoreCase));

        lock (_groupHotkeyLock)
            _groupHotkeyConfigs.Remove(groupName);

        RefreshGroupOptions();
        RebuildGroupedAddressList();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void EditSelectedGroups(object parameter)
    {
        var items = GetSelectedItems(parameter);
        if (items.Count == 0) return;

        _pendingGroupMoveEntries.Clear();
        _pendingGroupMoveEntries.AddRange(items);

        RefreshGroupOptions(items[0].GroupName);
        GroupPickerTitle = items.Count == 1 ? "Mover para Grupo" : $"Mover para Grupo ({items.Count} selecionados)";
        IsGroupPickerOpen = true;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void EditSelectedDescriptions(object parameter)
    {
        var items = GetSelectedItems(parameter);
        if (items.Count == 0) return;

        EditDialogTitle = items.Count == 1 ? "Alterar Descrição" : $"Alterar Descrição ({items.Count} selecionados)";
        EditDialogValue = items[0].Description;
        _onEditDialogCommit = (newVal) =>
        {
            foreach (var item in items) item.Description = newVal;
        };
        IsEditDialogOpen = true;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void EditSelectedValues(object parameter)
    {
        var items = GetSelectedItems(parameter);
        if (items.Count == 0) return;

        EditDialogTitle = items.Count == 1 ? "Alterar Valor Atual" : $"Alterar Valor ({items.Count} selecionados)";
        EditDialogValue = items[0].CurrentValue;
        _onEditDialogCommit = (newVal) =>
        {
            foreach (var item in items)
            {
                item.CurrentValue = newVal;
                WriteValue(item); // Write the new value to memory
            }
        };
        IsEditDialogOpen = true;
    }

    [RelayCommand]
    private void EditEntryDescription(MemoryEntry? entry)
    {
        if (entry is null) return;
        EditDialogTitle = "Editar descrição";
        EditDialogValue = entry.Description;
        _onEditDialogCommit = newVal => entry.Description = newVal;
        IsEditDialogOpen = true;
    }

    [RelayCommand]
    private void EditEntryValue(MemoryEntry? entry)
    {
        if (entry is null) return;
        EditDialogTitle = "Editar valor";
        EditDialogValue = entry.CurrentValue;
        _onEditDialogCommit = newVal =>
        {
            entry.CurrentValue = newVal;
            WriteValue(entry);
        };
        IsEditDialogOpen = true;
    }

    [RelayCommand]
    private void EditEntryAddress(MemoryEntry? entry)
    {
        if (entry is null) return;
        EditDialogTitle = "Editar endereço (hex ou decimal)";
        EditDialogValue = $"0x{entry.Address:X}";
        _onEditDialogCommit = newVal =>
        {
            if (!TryParseAddress(newVal, out long parsed))
            {
                ShowSnackbar("Endereço inválido. Use decimal ou hexadecimal (0x...).");
                return;
            }

            entry.Address = parsed;
            entry.ResolvedAddress = 0;
        };
        IsEditDialogOpen = true;
    }

    [RelayCommand]
    private void EditEntryGroup(MemoryEntry? entry)
    {
        if (entry is null) return;
        EditDialogTitle = "Editar grupo";
        EditDialogValue = entry.GroupName;
        _onEditDialogCommit = newVal =>
        {
            if (string.IsNullOrWhiteSpace(newVal)) return;
            entry.GroupName = newVal.Trim();
            GroupingChanged?.Invoke();
        };
        IsEditDialogOpen = true;
    }

    private System.Collections.Generic.List<MemoryEntry> GetSelectedItems(object parameter)
    {
        var list = new System.Collections.Generic.List<MemoryEntry>();
        if (parameter is System.Collections.IList items && items.Count > 0)
            list.AddRange(items.Cast<MemoryEntry>());
        else if (SelectedEntry is not null)
            list.Add(SelectedEntry);
        return list;
    }

    // ── Add/Write commands ────────────────────────────────────────────────

    [RelayCommand]
    private void WriteValue(MemoryEntry entry)
    {
        if (!_memory.IsAttached) return;
        try
        {
            var bytes = entry.CurrentValue.ToBytes(entry.Type);
            if (bytes is null || bytes.Length == 0)
            {
                ShowSnackbar("Valor inválido para o tipo selecionado.");
                return;
            }

            // Re-resolve the address at write time instead of using a potentially-stale ResolvedAddress
            long addr = entry.IsPointerChain && entry.Offsets.Count > 0
                ? _memory.ResolvePointerChain(entry.Address, entry.Offsets)
                : entry.Address;

            if (addr == 0)
            {
                ShowSnackbar("Ponteiro não resolvido (0x0). Aguarde o próximo ciclo de atualização ou verifique a cadeia.");
                return;
            }

            entry.ResolvedAddress = addr;
            _memory.WriteBytes(addr, bytes);
            LogService.Instance.Info($"Valor escrito em 0x{addr:X}: {entry.CurrentValue}");
        }
        catch (Models.MemoryException mex) { ShowSnackbar(mex.FriendlyMessage); }
        catch (Exception ex)               { ShowSnackbar($"Erro ao escrever: {ex.Message}"); }
    }

    [RelayCommand]
    private void AddEntry()
    {
        _list.Add(new MemoryEntry
        {
            Description = "Novo endereço",
            Type        = ScanValueType.Byte4,
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeleteSelected(object parameter)
    {
        if (parameter is System.Collections.IList items && items.Count > 0)
        {
            var entriesToRemove = items.Cast<MemoryEntry>().ToList();
            foreach (var entry in entriesToRemove)
            {
                _list.Remove(entry);
            }
        }
        else if (SelectedEntry is not null)
        {
            _list.Remove(SelectedEntry);
        }
    }

    [RelayCommand]
    private void ToggleFreeze(object parameter)
    {
        var itemsToToggle = new System.Collections.Generic.List<MemoryEntry>();

        if (parameter is MemoryEntry singleEntry)
            itemsToToggle.Add(singleEntry);
        else if (parameter is System.Collections.IList items && items.Count > 0)
            itemsToToggle.AddRange(items.Cast<MemoryEntry>());
        else if (SelectedEntry is not null)
            itemsToToggle.Add(SelectedEntry);

        foreach (var entry in itemsToToggle)
        {
            if (entry.IsFrozen)
            {
                entry.IsFrozen = false;
                entry.FrozenValue = null;
            }
            else
            {
                if (!_memory.IsAttached)
                {
                    ShowSnackbar("Conecte-se a um processo antes de congelar valores.");
                    return;
                }

                var size = entry.Type.ByteSize();
                try
                {
                    long addr = entry.ResolvedAddress != 0 ? entry.ResolvedAddress : entry.Address;
                    var bytes = _memory.ReadBytes(addr, size);
                    entry.FrozenValue = bytes;
                    entry.IsFrozen = bytes is { Length: > 0 };
                }
                catch (MemoryException mex)
                {
                    ShowSnackbar(mex.FriendlyMessage);
                    entry.IsFrozen = false;
                    entry.FrozenValue = null;
                }
            }
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void FindPointers()
    {
        if (SelectedEntry is null) return;
        RequestOpenPointerMapper?.Invoke(SelectedEntry.ResolvedAddress);
    }

    [RelayCommand]
    private async Task ExportCheatTableAsync()
    {
        await SafeRunAsync(async () =>
        {
            var xml = ConfigService.ExportAsCheatTable(_list);
            var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "exports");
            System.IO.Directory.CreateDirectory(dir);
            var filePath = System.IO.Path.Combine(dir, $"address-list-{DateTime.Now:yyyyMMdd-HHmmss}.CT");
            await System.IO.File.WriteAllTextAsync(filePath, xml);
            ShowSnackbar($"Cheat Table exportado: {System.IO.Path.GetFileName(filePath)}");
        });
    }

    private bool HasSelection() => SelectedEntry is not null;

    private void RefreshGroupOptions(string? preferredGroup = null)
    {
        GroupOptions.Clear();

        var names = _list
            .Select(e => e.GroupName?.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .Concat(_customGroups)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var name in names)
            GroupOptions.Add(name);

        var preferred = preferredGroup?.Trim();
        if (!string.IsNullOrWhiteSpace(preferred) && GroupOptions.Any(g => string.Equals(g, preferred, StringComparison.OrdinalIgnoreCase)))
            SelectedGroupOption = GroupOptions.First(g => string.Equals(g, preferred, StringComparison.OrdinalIgnoreCase));
        else
            SelectedGroupOption = GroupOptions.FirstOrDefault();
    }

    private void RegisterCustomGroup(string groupName)
    {
        var normalized = groupName.Trim();
        if (normalized.Length == 0)
            return;

        _customGroups.Add(normalized);
        RefreshGroupOptions(normalized);
        RebuildGroupedAddressList();
    }

    private void OnAddressListCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var removed in e.OldItems.OfType<MemoryEntry>())
                removed.PropertyChanged -= OnEntryPropertyChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (var added in e.NewItems.OfType<MemoryEntry>())
                added.PropertyChanged += OnEntryPropertyChanged;
        }

        RefreshGroupOptions();
        RebuildGroupedAddressList();
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MemoryEntry.GroupName) or nameof(MemoryEntry.Description) or nameof(MemoryEntry.IsFrozen))
        {
            RefreshGroupOptions();
            RebuildGroupedAddressList();
        }
    }

    private void RebuildGroupedAddressList()
    {
        var expandedMap = GroupedAddressList.ToDictionary(g => g.Name, g => g.IsExpanded, StringComparer.OrdinalIgnoreCase);
        var groups = _list
            .GroupBy(entry => string.IsNullOrWhiteSpace(entry.GroupName) ? "Geral" : entry.GroupName.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var custom in _customGroups)
        {
            if (!groups.ContainsKey(custom))
                groups[custom] = [];
        }

        var orderedNames = groups.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();

        GroupedAddressList.Clear();
        foreach (var name in orderedNames)
        {
            GroupHotkeyConfig? hotkey;
            lock (_groupHotkeyLock)
                _groupHotkeyConfigs.TryGetValue(name, out hotkey);

            var section = new AddressGroupSection(name)
            {
                IsExpanded = expandedMap.TryGetValue(name, out var expanded) ? expanded : true,
                HasFrozenEntries = groups[name].Any(entry => entry.IsFrozen),
                HasHotkey = hotkey is { IsEnabled: true },
                HotkeySummary = hotkey is { IsEnabled: true } ? BuildHotkeySummary(hotkey) : string.Empty
            };

            foreach (var entry in groups[name])
                section.Entries.Add(entry);

            GroupedAddressList.Add(section);
        }
    }

    private static bool TryParseAddress(string? text, out long value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

        if (text.Any(c => (c is >= 'A' and <= 'F') || (c is >= 'a' and <= 'f')))
            return long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public IReadOnlyList<GroupHotkeyExportItem> ExportGroupHotkeys()
    {
        lock (_groupHotkeyLock)
        {
            return _groupHotkeyConfigs.Values
                .Select(h => new GroupHotkeyExportItem
                {
                    GroupName = h.GroupName,
                    HotkeyText = h.HotkeyText,
                    Action = h.Action.ToString(),
                    ActionValue = h.ActionValue,
                    IsEnabled = h.IsEnabled,
                })
                .ToList();
        }
    }

    public void ImportGroupHotkeys(IEnumerable<GroupHotkeyExportItem>? hotkeys)
    {
        lock (_groupHotkeyLock)
        {
            _groupHotkeyConfigs.Clear();
            _hotkeyPressedMap.Clear();

            if (hotkeys is null)
                return;

            foreach (var item in hotkeys)
            {
                if (!item.IsEnabled || string.IsNullOrWhiteSpace(item.GroupName) || string.IsNullOrWhiteSpace(item.HotkeyText))
                    continue;

                if (!Enum.TryParse<GroupHotkeyAction>(item.Action, true, out var action))
                    continue;

                _groupHotkeyConfigs[item.GroupName.Trim()] = new GroupHotkeyConfig
                {
                    GroupName = item.GroupName.Trim(),
                    HotkeyText = item.HotkeyText.Trim(),
                    Action = action,
                    ActionValue = item.ActionValue,
                    IsEnabled = true,
                };
            }
        }

        RebuildGroupedAddressList();
    }

    private static string BuildHotkeySummary(GroupHotkeyConfig config)
    {
        return config.Action switch
        {
            GroupHotkeyAction.SetValue => $"{config.HotkeyText} -> set {config.ActionValue}",
            GroupHotkeyAction.IncreaseBy => $"{config.HotkeyText} -> +{config.ActionValue}",
            GroupHotkeyAction.DecreaseBy => $"{config.HotkeyText} -> -{config.ActionValue}",
            GroupHotkeyAction.ToggleFreeze => $"{config.HotkeyText} -> toggle freeze",
            _ => config.HotkeyText
        };
    }

    private async Task HotkeyWorkerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (IsHotkeyCaptureArmed)
                {
                    if (TryCaptureCurrentHotkey(out var captured))
                    {
                        OnUI(() =>
                        {
                            GroupHotkeyText = captured.Canonical;
                            GroupHotkeyCaptureHint = $"Capturada: {captured.Canonical}";
                            IsHotkeyCaptureArmed = false;
                        });
                    }

                    await Task.Delay(30, ct);
                    continue;
                }

                List<GroupHotkeyConfig> active;
                lock (_groupHotkeyLock)
                    active = _groupHotkeyConfigs.Values.Where(h => h.IsEnabled).Select(h => new GroupHotkeyConfig
                    {
                        GroupName = h.GroupName,
                        HotkeyText = h.HotkeyText,
                        Action = h.Action,
                        ActionValue = h.ActionValue,
                        IsEnabled = h.IsEnabled
                    }).ToList();

                foreach (var config in active)
                {
                    if (!TryParseHotkey(config.HotkeyText, out var parsed))
                        continue;

                    bool isDown = IsHotkeyPressed(parsed);
                    string keyId = $"{config.GroupName}|{parsed.Canonical}";
                    _hotkeyPressedMap.TryGetValue(keyId, out var wasDown);

                    if (isDown && !wasDown)
                        OnUI(() => ExecuteGroupHotkey(config));

                    _hotkeyPressedMap[keyId] = isDown;
                }

                await Task.Delay(40, ct);
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogService.Instance.Warn($"Hotkey worker error: {ex.Message}");
                await Task.Delay(200, ct);
            }
        }
    }

    private static readonly int[] _captureKeys = BuildCaptureKeys();

    private static int[] BuildCaptureKeys()
    {
        var keys = new List<int>
        {
            0x01, // MouseLeft
            0x02, // MouseRight
            0x04, // MouseMiddle
            0x05, // MouseX1
            0x06, // MouseX2
            0x2D, 0x2E, 0x24, 0x23, 0x21, 0x22, // Insert/Delete/Home/End/PageUp/PageDown
            0x26, 0x28, 0x25, 0x27, 0x20 // Arrows + Space
        };

        for (int vk = 0x70; vk <= 0x7B; vk++)
            keys.Add(vk); // F1-F12

        for (int vk = 'A'; vk <= 'Z'; vk++)
            keys.Add(vk);

        for (int vk = '0'; vk <= '9'; vk++)
            keys.Add(vk);

        return [.. keys];
    }

    private static bool TryCaptureCurrentHotkey(out ParsedHotkey hotkey)
    {
        hotkey = default;

        bool ctrl = IsKeyDown(VK_CONTROL);
        bool alt = IsKeyDown(VK_MENU);
        bool shift = IsKeyDown(VK_SHIFT);

        int key = 0;
        foreach (var vk in _captureKeys)
        {
            if (IsKeyDown(vk))
            {
                key = vk;
                break;
            }
        }

        if (key == 0)
            return false;

        var parts = new List<string>();
        if (ctrl) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        parts.Add(GetHotkeyDisplayName(key));

        hotkey = new ParsedHotkey(ctrl, alt, shift, key, string.Join('+', parts));
        return true;
    }

    private void ExecuteGroupHotkey(GroupHotkeyConfig config)
    {
        var groupItems = _list.Where(e => string.Equals(e.GroupName, config.GroupName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (groupItems.Count == 0)
            return;

        switch (config.Action)
        {
            case GroupHotkeyAction.ToggleFreeze:
                ToggleFreezeGroup(config.GroupName);
                break;

            case GroupHotkeyAction.SetValue:
                if (!_memory.IsAttached)
                {
                    ShowSnackbar("Conecte-se a um processo antes de usar hotkeys de escrita.");
                    return;
                }

                foreach (var entry in groupItems)
                {
                    entry.CurrentValue = config.ActionValue;
                    WriteValue(entry);
                }
                break;

            case GroupHotkeyAction.IncreaseBy:
            case GroupHotkeyAction.DecreaseBy:
                if (!_memory.IsAttached)
                {
                    ShowSnackbar("Conecte-se a um processo antes de usar hotkeys de escrita.");
                    return;
                }

                if (!double.TryParse(config.ActionValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var delta))
                {
                    ShowSnackbar($"Valor de hotkey inválido para o grupo '{config.GroupName}'.");
                    return;
                }

                var signedDelta = config.Action == GroupHotkeyAction.DecreaseBy ? -delta : delta;
                foreach (var entry in groupItems)
                {
                    if (!TryApplyDeltaToEntry(entry, signedDelta, out var nextValue))
                        continue;

                    entry.CurrentValue = nextValue;
                    WriteValue(entry);
                }
                break;
        }
    }

    private static bool TryApplyDeltaToEntry(MemoryEntry entry, double delta, out string nextValue)
    {
        nextValue = entry.CurrentValue;
        var style = NumberStyles.Float | NumberStyles.AllowLeadingSign;

        switch (entry.Type)
        {
            case ScanValueType.Byte1:
                if (!int.TryParse(entry.CurrentValue, style, CultureInfo.InvariantCulture, out var b1Current)) return false;
                var b1Next = Math.Clamp((int)Math.Round(b1Current + delta), byte.MinValue, byte.MaxValue);
                nextValue = b1Next.ToString(CultureInfo.InvariantCulture);
                return true;

            case ScanValueType.Byte2:
                if (!int.TryParse(entry.CurrentValue, style, CultureInfo.InvariantCulture, out var b2Current)) return false;
                var b2Next = Math.Clamp((int)Math.Round(b2Current + delta), short.MinValue, short.MaxValue);
                nextValue = b2Next.ToString(CultureInfo.InvariantCulture);
                return true;

            case ScanValueType.Byte4:
                if (!long.TryParse(entry.CurrentValue, style, CultureInfo.InvariantCulture, out var b4Current)) return false;
                var b4Raw = b4Current + (long)Math.Round(delta);
                var b4Next = Math.Clamp(b4Raw, int.MinValue, int.MaxValue);
                nextValue = b4Next.ToString(CultureInfo.InvariantCulture);
                return true;

            case ScanValueType.Byte8:
                if (!long.TryParse(entry.CurrentValue, style, CultureInfo.InvariantCulture, out var b8Current)) return false;
                var b8Next = b8Current + (long)Math.Round(delta);
                nextValue = b8Next.ToString(CultureInfo.InvariantCulture);
                return true;

            case ScanValueType.Float:
                if (!float.TryParse(entry.CurrentValue, style, CultureInfo.InvariantCulture, out var fCurrent)) return false;
                var fNext = fCurrent + (float)delta;
                nextValue = fNext.ToString("G9", CultureInfo.InvariantCulture);
                return true;

            case ScanValueType.Double:
                if (!double.TryParse(entry.CurrentValue, style, CultureInfo.InvariantCulture, out var dCurrent)) return false;
                var dNext = dCurrent + delta;
                nextValue = dNext.ToString("G17", CultureInfo.InvariantCulture);
                return true;

            default:
                return false;
        }
    }

    private readonly record struct ParsedHotkey(bool Ctrl, bool Alt, bool Shift, int Key, string Canonical);

    private static bool TryParseHotkey(string? hotkeyText, out ParsedHotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(hotkeyText))
            return false;

        bool ctrl = false, alt = false, shift = false;
        int key = 0;
        string keyName = string.Empty;

        var tokens = hotkeyText.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var raw in tokens)
        {
            var token = raw.Trim().ToUpperInvariant();
            if (token is "CTRL" or "CONTROL")
            {
                ctrl = true;
                continue;
            }

            if (token is "ALT" or "MENU")
            {
                alt = true;
                continue;
            }

            if (token is "SHIFT")
            {
                shift = true;
                continue;
            }

            if (!TryParseHotkeyKey(token, out key))
                return false;

            keyName = GetHotkeyDisplayName(key);
        }

        if (key == 0)
            return false;

        var parts = new List<string>();
        if (ctrl) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        parts.Add(keyName);

        hotkey = new ParsedHotkey(ctrl, alt, shift, key, string.Join('+', parts));
        return true;
    }

    private static bool TryParseHotkeyKey(string token, out int vk)
    {
        vk = 0;
        if (token.Length == 1)
        {
            char c = token[0];
            if (c is >= 'A' and <= 'Z')
            {
                vk = c;
                return true;
            }

            if (c is >= '0' and <= '9')
            {
                vk = c;
                return true;
            }
        }

        if (token.Length >= 2 && token[0] == 'F' && int.TryParse(token[1..], out var fKey) && fKey is >= 1 and <= 12)
        {
            vk = 0x70 + (fKey - 1);
            return true;
        }

        return token switch
        {
            "MOUSELEFT" or "LMB" => SetVk(0x01, out vk),
            "MOUSERIGHT" or "RMB" => SetVk(0x02, out vk),
            "MOUSEMIDDLE" or "MMB" => SetVk(0x04, out vk),
            "MOUSEX1" => SetVk(0x05, out vk),
            "MOUSEX2" => SetVk(0x06, out vk),
            "INSERT" => SetVk(0x2D, out vk),
            "DELETE" => SetVk(0x2E, out vk),
            "HOME" => SetVk(0x24, out vk),
            "END" => SetVk(0x23, out vk),
            "PAGEUP" or "PGUP" => SetVk(0x21, out vk),
            "PAGEDOWN" or "PGDN" => SetVk(0x22, out vk),
            "UP" => SetVk(0x26, out vk),
            "DOWN" => SetVk(0x28, out vk),
            "LEFT" => SetVk(0x25, out vk),
            "RIGHT" => SetVk(0x27, out vk),
            "SPACE" => SetVk(0x20, out vk),
            _ => false
        };
    }

    private static string GetHotkeyDisplayName(int vk) => vk switch
    {
        0x01 => "MouseLeft",
        0x02 => "MouseRight",
        0x04 => "MouseMiddle",
        0x05 => "MouseX1",
        0x06 => "MouseX2",
        0x2D => "Insert",
        0x2E => "Delete",
        0x24 => "Home",
        0x23 => "End",
        0x21 => "PageUp",
        0x22 => "PageDown",
        0x26 => "Up",
        0x28 => "Down",
        0x25 => "Left",
        0x27 => "Right",
        0x20 => "Space",
        >= 0x70 and <= 0x7B => $"F{vk - 0x6F}",
        _ when vk is >= 'A' and <= 'Z' => ((char)vk).ToString(),
        _ when vk is >= '0' and <= '9' => ((char)vk).ToString(),
        _ => vk.ToString(CultureInfo.InvariantCulture)
    };

    private static bool SetVk(int value, out int vk)
    {
        vk = value;
        return true;
    }

    private static bool IsKeyDown(int vKey) => (GetAsyncKeyState(vKey) & 0x8000) != 0;

    private static bool IsHotkeyPressed(ParsedHotkey hotkey)
    {
        if (hotkey.Ctrl && !IsKeyDown(VK_CONTROL)) return false;
        if (hotkey.Alt && !IsKeyDown(VK_MENU)) return false;
        if (hotkey.Shift && !IsKeyDown(VK_SHIFT)) return false;
        return IsKeyDown(hotkey.Key);
    }

    public void Dispose()
    {
        _hotkeyCts.Cancel();

        _list.CollectionChanged -= OnAddressListCollectionChanged;
        foreach (var entry in _list)
            entry.PropertyChanged -= OnEntryPropertyChanged;

        _refreshTimer.Stop();
        _refreshTimer.Tick -= OnRefreshTick;

        try
        {
            _hotkeyWorker.Wait(200);
        }
        catch
        {
            // Ignore shutdown race.
        }

        _hotkeyCts.Dispose();
    }
}
