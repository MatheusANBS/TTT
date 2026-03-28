// File: ViewModels/PointerMapperViewModel.cs

using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TTT.Models;
using TTT.Services;
using TTT.Utils;

namespace TTT.ViewModels;

public sealed partial class PointerMapperViewModel : BaseViewModel, IDisposable
{
    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// ObservableCollection that supports replacing all items in one shot,
    /// firing a single Reset notification instead of N individual Add/Remove events.
    /// </summary>
    private sealed class BulkObservableCollection<T> : ObservableCollection<T>
    {
        public void ReplaceAll(IReadOnlyList<T> items)
        {
            Items.Clear();
            foreach (var item in items) Items.Add(item);
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
                System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
        }
    }

    private static readonly JsonSerializerOptions _jsonSaveOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions _jsonLoadOptions = new() { PropertyNameCaseInsensitive = true };

    // ── v1 backward-compat model (plain array, long offsets) ────────────
    private sealed class V1Entry
    {
        public string ModuleName   { get; set; } = string.Empty;
        public long   ModuleOffset { get; set; }
        public List<long> Offsets  { get; set; } = [];
        public int    Score        { get; set; }
        public string Description  { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
        public ScanValueType ValueType { get; set; } = ScanValueType.Byte4;
    }

    /// <summary>
    /// Deserializes a .pscan file regardless of whether it is v1 (plain JSON array with
    /// long offsets) or v2 (PscanFile envelope with hex-string offsets).
    /// </summary>
    private static List<PointerScanEntry> ReadPscanEntries(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);

        if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            // v1 legacy
            var v1 = System.Text.Json.JsonSerializer.Deserialize<List<V1Entry>>(json, _jsonLoadOptions) ?? [];
            return v1.Select(e => new PointerScanEntry
            {
                ModuleName   = e.ModuleName,
                ModuleOffset = $"0x{e.ModuleOffset:X}",
                Offsets      = e.Offsets.Select(PointerScanEntry.OffsetToHex).ToList(),
                Score        = e.Score,
                Description  = e.Description,
                ValueType    = e.ValueType,
            }).ToList();
        }

        // v2 envelope
        var file = System.Text.Json.JsonSerializer.Deserialize<PscanFile>(json, _jsonLoadOptions);
        return file?.Chains ?? [];
    }

    private readonly PointerMapperService            _mapper;
    private readonly MemoryService                   _memory;
    private readonly ObservableCollection<MemoryEntry> _masterAddressList;
    private CancellationTokenSource?                 _cts;
    private readonly DispatcherTimer                 _refreshTimer;

    // ── Inputs ────────────────────────────────────────────────────────────
    [ObservableProperty] private string        _targetAddress        = string.Empty;
    [ObservableProperty] private int           _maxDepth             = 6;
    /// <summary>Raw text from the MaxOffset field — accepts decimal (4095) or hex (0xFFF).</summary>
    [ObservableProperty] private string        _maxOffset            = "0x3000";
    [ObservableProperty] private bool          _prioritizeStatic     = true;
    [ObservableProperty] private bool          _allowNegativeOffsets = true;
    [ObservableProperty] private ScanValueType _valueType            = ScanValueType.Byte4;

    // ── Results ───────────────────────────────────────────────────────────
    private readonly BulkObservableCollection<SavedPointer> _results = new();
    private readonly List<SavedPointer> _allResults = [];
    public ObservableCollection<SavedPointer> Results => _results;

    public IReadOnlyList<SavedPointer> AllResultsSnapshot => _allResults.ToList();

    /// <summary>Current page items shown by the DataGrid.</summary>
    public ObservableCollection<SavedPointer> ResultsView => _results;

    public static readonly string[] StabilityFilterOptions = ["Todos", "Direto", "Estático", "Misto", "Dinâmico"];

    [ObservableProperty] private string _stabilityFilter = "Todos";
    partial void OnStabilityFilterChanged(string value)
    {
        CurrentPage = 1;
        RebuildPagedResults();
    }

    [ObservableProperty] private string _currentValueFilter = string.Empty;
    partial void OnCurrentValueFilterChanged(string value)
    {
        CurrentPage = 1;
        RebuildPagedResults();
    }

    [ObservableProperty] private int _currentPage = 1;
    [ObservableProperty] private int _pageSize = 200;
    [ObservableProperty] private int _totalPages = 1;
    [ObservableProperty] private int _visibleResultCount;

    public string PageInfoText => $"Página {CurrentPage} de {TotalPages}";
    public bool HasPreviousPage => CurrentPage > 1;
    public bool HasNextPage => CurrentPage < TotalPages;
    public IReadOnlyList<int> PageSizeOptions { get; } = [50, 100, 200, 500];

    partial void OnCurrentPageChanged(int value) => RebuildPagedResults();

    partial void OnPageSizeChanged(int value)
    {
        if (value <= 0)
        {
            PageSize = 200;
            return;
        }

        CurrentPage = 1;
        RebuildPagedResults();
    }

    partial void OnTotalPagesChanged(int value) => UpdatePagingState();

    // ── Progress ──────────────────────────────────────────────────────────
    [ObservableProperty] private bool   _isSearching;
    [ObservableProperty] private string _progressText = string.Empty;

    public PointerMapperViewModel(
        PointerMapperService mapper,
        MemoryService memory,
        ObservableCollection<MemoryEntry> masterAddressList)
    {
        _mapper            = mapper;
        _memory            = memory;
        _masterAddressList = masterAddressList;

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Constants.ADDRESS_LIST_TIMER_MS)
        };
        _refreshTimer.Tick += OnRefreshTick;
        _refreshTimer.Start();
    }

    // ── Live refresh of result values ──────────────────────────────────────
    private void OnRefreshTick(object? sender, EventArgs e)
    {
        if (!_memory.IsAttached || Results.Count == 0) return;

        foreach (var ptr in Results)
        {
            try
            {
                long baseAddr = ptr.ModuleBase + ptr.ModuleOffset;
                long addr = ptr.Offsets.Count > 0
                    ? _memory.ResolvePointerChain(baseAddr, ptr.Offsets)
                    : baseAddr;

                if (addr == 0) { ptr.CurrentValue = "??"; continue; }

                ptr.ResolvedAddress = addr;
                var bytes = _memory.ReadBytes(addr, ptr.ValueType.ByteSize());
                ptr.CurrentValue = bytes is null ? "??" : bytes.ReadValueAs(ptr.ValueType);
            }
            catch { ptr.CurrentValue = "??"; }
        }

        if (!string.IsNullOrWhiteSpace(CurrentValueFilter))
            RebuildPagedResults();
    }

    // ── Commands ──────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        await SafeRunAsync(async () =>
        {
            if (!long.TryParse(
                    TargetAddress.Replace("0x", "").Replace("0X", ""),
                    System.Globalization.NumberStyles.HexNumber,
                    null, out var addr))
            {
                ShowSnackbar("Endereço inválido. Use formato hexadecimal (ex: 0x1A2B3C).");
                return;
            }

            IsSearching = true;
            _allResults.Clear();
            _results.Clear();
            VisibleResultCount = 0;
            CurrentPage = 1;
            TotalPages = 1;
            UpdatePagingState();

            using var cts = new CancellationTokenSource();
            _cts = cts;

            var progress = new Progress<string>(msg => OnUI(() => ProgressText = msg));

            if (!TryParseHexOrDec(MaxOffset, out int maxOffsetParsed) || maxOffsetParsed <= 0)
            {
                ShowSnackbar("Offset máx. inválido. Use decimal (4095) ou hex (0xFFF).");
                return;
            }

            var chains = await _mapper.FindPointerChainsAsync(
                addr, MaxDepth, maxOffsetParsed, PrioritizeStatic, AllowNegativeOffsets,
                ValueType, progress, cts.Token);

            OnUI(() =>
            {
                _allResults.Clear();
                _allResults.AddRange(chains);
                CurrentPage = 1;
                RebuildPagedResults();
                ProgressText = $"{_allResults.Count} cadeia(s) encontrada(s), {VisibleResultCount} após filtro.";
            });
        });

        IsSearching = false;
        _cts        = null;
    }

    private bool CanSearch() => !IsSearching;

    /// <summary>
    /// Parses a user-typed offset string that can be either decimal ("4096") or
    /// C-style hex ("0xFFF" / "0XFFF"). Returns false if the string is invalid.
    /// </summary>
    private static bool TryParseHexOrDec(string? text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(text[2..],
                System.Globalization.NumberStyles.HexNumber, null, out value);
        return int.TryParse(text, out value);
    }

    [RelayCommand]
    private void CancelSearch() => _cts?.Cancel();

    /// <summary>Re-validates all scanned chains against the live process.</summary>
    [RelayCommand]
    private void VerifyAll()
    {
        if (!_memory.IsAttached || _allResults.Count == 0)
        {
            ShowSnackbar("Nenhuma cadeia para verificar ou processo não conectado.");
            return;
        }

        if (!long.TryParse(
                TargetAddress.Replace("0x", "").Replace("0X", ""),
                System.Globalization.NumberStyles.HexNumber,
                null, out var targetAddr))
        {
            ShowSnackbar("Endereço alvo inválido — não é possível verificar.");
            return;
        }

        int ok = 0;
        foreach (var ptr in _allResults)
        {
            try
            {
                long moduleBase = !string.IsNullOrEmpty(ptr.ModuleName)
                    ? (_memory.GetModuleBase(ptr.ModuleName) is long mb and not 0 ? mb : ptr.ModuleBase)
                    : ptr.ModuleBase;

                long baseAddr = moduleBase + ptr.ModuleOffset;
                long resolved = _memory.ResolvePointerChain(baseAddr, ptr.Offsets);
                ptr.IsVerified = resolved == targetAddr;
                if (ptr.IsVerified) ok++;

                // Re-analyse hop stability: check INTERMEDIATE pointer storage addresses.
                // (The last walkAddr is the target itself — skip it.)
                int stableHops = 0, totalHops = 0;
                long walkAddr  = baseAddr;
                int  hopIndex  = 0;
                int  lastIndex = ptr.Offsets.Count - 1;
                foreach (var offset in ptr.Offsets)
                {
                    long ptrValue = _memory.ReadPointer(walkAddr);
                    if (ptrValue == 0) break;
                    walkAddr = ptrValue + offset;
                    if (hopIndex < lastIndex)
                    {
                        totalHops++;
                        uint regionType = _memory.GetRegionType(walkAddr);
                        if ((regionType & Constants.MEM_IMAGE)  != 0 ||
                            (regionType & Constants.MEM_MAPPED) != 0)
                            stableHops++;
                    }
                    hopIndex++;
                }
                ptr.StableHops = stableHops;
                ptr.TotalHops  = totalHops;
            }
            catch { ptr.IsVerified = false; }
        }

        int fullyStatic = _allResults.Count(p => p.TotalHops > 0 && p.StableHops == p.TotalHops);
        int direto      = _allResults.Count(p => p.TotalHops == 0 && p.IsVerified);
        RebuildPagedResults();
        ShowSnackbar($"{ok}/{_allResults.Count} verificadas — {fullyStatic} estáticas, {direto} diretas.");
    }

    [RelayCommand]
    private void AddPointerToList(SavedPointer? pointer)
    {
        if (pointer is null) return;
        _masterAddressList.Add(CreateMemoryEntry(pointer));
        ShowSnackbar("Ponteiro adicionado à lista.");
    }

    [RelayCommand]
    private void AddSelectedPointersToList(object parameter)
    {
        if (parameter is SavedPointer single)
        {
            _masterAddressList.Add(CreateMemoryEntry(single));
            ShowSnackbar("Ponteiro adicionado à lista.");
            return;
        }

        if (parameter is not IList items || items.Count == 0)
            return;

        int added = 0;
        foreach (var item in items)
        {
            if (item is not SavedPointer pointer) continue;
            _masterAddressList.Add(CreateMemoryEntry(pointer));
            added++;
        }

        if (added > 0)
            ShowSnackbar($"{added} ponteiro(s) adicionado(s) a lista.");
    }

    private static MemoryEntry CreateMemoryEntry(SavedPointer pointer) => new()
    {
        Description = pointer.Description ?? pointer.ToChainString(),
        Address     = pointer.ModuleBase + pointer.ModuleOffset,
        Type        = pointer.ValueType,
        Offsets     = [.. pointer.Offsets],
    };

    // ── Multi-session comparison ──────────────────────────────────────────

    /// <summary>
    /// Salva as cadeias atuais em um arquivo JSON para uso como base de comparação
    /// em outras sessões do jogo.
    /// </summary>
    [RelayCommand]
    private async Task SaveScanAsync()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "pointer-scans");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"scan_{DateTime.Now:yyyyMMdd_HHmmss}.pscan");
        await SaveScanToPathAsync(path);
    }

    public async Task SaveScanToPathAsync(string path)
    {
        if (_allResults.Count == 0)
        {
            ShowSnackbar("Nenhuma cadeia para salvar. Execute uma busca primeiro.");
            return;
        }

        if (string.IsNullOrWhiteSpace(path))
            return;

        var envelope = new PscanFile
        {
            SavedAt       = DateTime.Now.ToString("o"),
            TargetAddress = TargetAddress,
            MaxDepth      = MaxDepth,
            MaxOffset     = MaxOffset,
            Chains        = _allResults.Select(p => new PointerScanEntry
            {
                ModuleName   = p.ModuleName,
                ModuleOffset = $"0x{p.ModuleOffset:X}",
                Offsets      = p.Offsets.Select(PointerScanEntry.OffsetToHex).ToList(),
                Score        = p.Score,
                Description  = p.Description,
                StableHops   = p.StableHops,
                TotalHops    = p.TotalHops,
                ValueType    = p.ValueType,
            }).ToList(),
        };

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await Task.Run(() =>
            {
                var json = JsonSerializer.Serialize(envelope, _jsonSaveOptions);
                File.WriteAllText(path, json);
            });
            ShowSnackbar($"{envelope.Chains.Count} cadeias salvas em '{Path.GetFileName(path)}'");
        }
        catch (Exception ex)
        {
            ShowSnackbar($"Erro ao salvar: {ex.Message}");
        }
    }

    /// <summary>
    /// Carrega um arquivo .pscan salvo anteriormente e restaura as cadeias na lista de resultados.
    /// As bases dos módulos são re-resolvidas a partir do processo atualmente anexado.
    /// </summary>
    [RelayCommand]
    private async Task LoadScanAsync()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "pointer-scans");
        if (!Directory.Exists(dir))
        {
            ShowSnackbar("Nenhum scan salvo encontrado.");
            return;
        }

        var path = Directory
            .GetFiles(dir, "*.pscan", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(path))
        {
            ShowSnackbar("Nenhum scan salvo encontrado.");
            return;
        }

        await LoadScanFromPathAsync(path);
    }

    public async Task LoadScanFromPathAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            ShowSnackbar("Arquivo de scan inválido.");
            return;
        }

        // Resolve module bases ONCE (single process enumeration, not once per entry)
        var moduleBaseCache = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (_memory.IsAttached)
        {
            foreach (var (name, modBase, _) in _memory.GetModules())
                moduleBaseCache[name] = modBase;
        }

        List<SavedPointer>? items;
        try
        {
            items = await Task.Run(() =>
            {
                var entries = ReadPscanEntries(File.ReadAllText(path));
                if (entries.Count == 0) return null;

                return entries.Select(e =>
                {
                    moduleBaseCache.TryGetValue(e.ModuleName, out long moduleBase);
                    return new SavedPointer
                    {
                        ModuleName   = e.ModuleName,
                        ModuleBase   = moduleBase,
                        ModuleOffset = e.ModuleOffsetLong,
                        Offsets      = e.OffsetsLong,
                        Score        = e.Score,
                        Description  = e.Description,
                        StableHops   = e.StableHops,
                        TotalHops    = e.TotalHops,
                        ValueType    = e.ValueType,
                    };
                }).ToList();
            });
        }
        catch (Exception ex)
        {
            ShowSnackbar($"Erro ao carregar: {ex.Message}");
            return;
        }

        if (items is null)
        {
            ShowSnackbar("Arquivo inválido ou vazio.");
            return;
        }

        ReplaceAllResults(items);
        ProgressText = $"{items.Count} cadeia(s) carregada(s) de '{Path.GetFileName(path)}'.";
        ShowSnackbar(ProgressText);
    }

    /// Cadeias que sobrevivem à interseção são confiáveis entre sessões.
    /// </summary>
    [RelayCommand]
    private async Task CompareWithSavedAsync()
    {
        if (_allResults.Count == 0)
        {
            ShowSnackbar("Execute uma busca primeiro para ter resultados a comparar.");
            return;
        }

        var dir = Path.Combine(AppContext.BaseDirectory, "pointer-scans");
        if (!Directory.Exists(dir))
        {
            ShowSnackbar("Nenhum scan base encontrado para comparação.");
            return;
        }

        var path = Directory
            .GetFiles(dir, "*.pscan", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(path))
        {
            ShowSnackbar("Nenhum scan salvo encontrado para comparação.");
            return;
        }

        await CompareWithFileAsync(path);
    }

    public async Task CompareWithFileAsync(string path)
    {
        if (_allResults.Count == 0)
        {
            ShowSnackbar("Execute uma busca primeiro para ter resultados a comparar.");
            return;
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            ShowSnackbar("Arquivo base inválido para comparação.");
            return;
        }

        HashSet<string>? savedKeys;
        try
        {
            savedKeys = await Task.Run(() =>
            {
                var entries = ReadPscanEntries(File.ReadAllText(path));
                return entries.Count > 0
                    ? entries.Select(ChainKey).ToHashSet(StringComparer.Ordinal)
                    : null;
            });
        }
        catch (Exception ex)
        {
            ShowSnackbar($"Erro ao comparar: {ex.Message}");
            return;
        }

        if (savedKeys is null)
        {
            ShowSnackbar("Arquivo inválido ou vazio.");
            return;
        }

        var kept    = _allResults.Where(p => savedKeys.Contains(ChainKey(p))).ToList();
        int removed = _allResults.Count - kept.Count;

        _allResults.Clear();
        _allResults.AddRange(kept);
        CurrentPage = 1;
        RebuildPagedResults();
        ShowSnackbar($"{kept.Count} cadeia(s) sobreviveram à comparação com '{Path.GetFileName(path)}' ({removed} removidas).");
    }

    public void ReplaceAllResults(IEnumerable<SavedPointer> pointers)
    {
        _allResults.Clear();
        _allResults.AddRange(pointers);
        CurrentPage = 1;
        RebuildPagedResults();
        ProgressText = _allResults.Count == 0
            ? "Nenhuma cadeia carregada."
            : $"{_allResults.Count} cadeia(s) prontas.";
    }

    /// <summary>Unique key identifying a pointer chain across sessions (module-relative, no runtime addresses).</summary>
    private static string ChainKey(SavedPointer p) =>
        $"{NormalizeModuleName(p.ModuleName)}|{p.ModuleOffset:X}|{string.Join(',', p.Offsets.Select(NormalizeOffset))}";

    private static string ChainKey(PointerScanEntry p) =>
        $"{NormalizeModuleName(p.ModuleName)}|{p.ModuleOffsetLong:X}|{string.Join(',', p.OffsetsLong.Select(NormalizeOffset))}";

    private static string NormalizeModuleName(string? moduleName) =>
        (moduleName ?? string.Empty).Trim().ToUpperInvariant();

    private static string NormalizeOffset(long offset) =>
        offset < 0 ? $"-{(-offset):X}" : $"+{offset:X}";

    public void Dispose()
    {
        _refreshTimer.Stop();
        _refreshTimer.Tick -= OnRefreshTick;
    }

    private bool MatchesStabilityFilter(SavedPointer p) => StabilityFilter switch
    {
        "Direto"   => p.TotalHops == 0,
        "Estático" => p.TotalHops > 0 && p.StableHops == p.TotalHops,
        "Misto"    => p.TotalHops > 0 && p.StableHops > 0 && p.StableHops < p.TotalHops,
        "Dinâmico" => p.TotalHops > 0 && p.StableHops == 0,
        _          => true // "Todos"
    };

    private bool MatchesCurrentValueFilter(SavedPointer p)
    {
        var filter = CurrentValueFilter?.Trim();
        if (string.IsNullOrEmpty(filter))
            return true;

        var current = p.CurrentValue?.Trim();
        if (string.IsNullOrEmpty(current) || current == "??")
            return false;

        if (string.Equals(current, filter, StringComparison.OrdinalIgnoreCase))
            return true;

        if (long.TryParse(filter, NumberStyles.Integer, CultureInfo.InvariantCulture, out var filterLong) &&
            long.TryParse(current, NumberStyles.Integer, CultureInfo.InvariantCulture, out var currentLong))
            return currentLong == filterLong;

        if (double.TryParse(filter, NumberStyles.Float, CultureInfo.InvariantCulture, out var filterDouble) &&
            double.TryParse(current, NumberStyles.Float, CultureInfo.InvariantCulture, out var currentDouble))
            return Math.Abs(currentDouble - filterDouble) < 0.0000001d;

        return false;
    }

    private void RebuildPagedResults()
    {
        var filtered = _allResults
            .Where(MatchesStabilityFilter)
            .Where(MatchesCurrentValueFilter)
            .ToList();

        VisibleResultCount = filtered.Count;

        int totalPages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)PageSize));
        if (TotalPages != totalPages)
            TotalPages = totalPages;

        int newPage = CurrentPage;
        if (newPage < 1) newPage = 1;
        if (newPage > TotalPages) newPage = TotalPages;

        if (newPage != CurrentPage)
        {
            CurrentPage = newPage;
            return;
        }

        var pageItems = filtered
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        _results.ReplaceAll(pageItems);
        UpdatePagingState();
    }

    private void UpdatePagingState()
    {
        OnPropertyChanged(nameof(PageInfoText));
        OnPropertyChanged(nameof(HasPreviousPage));
        OnPropertyChanged(nameof(HasNextPage));

        FirstPageCommand.NotifyCanExecuteChanged();
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        LastPageCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanGoFirstPage))]
    private void FirstPage() => CurrentPage = 1;

    [RelayCommand(CanExecute = nameof(CanGoPreviousPage))]
    private void PreviousPage() => CurrentPage--;

    [RelayCommand(CanExecute = nameof(CanGoNextPage))]
    private void NextPage() => CurrentPage++;

    [RelayCommand(CanExecute = nameof(CanGoLastPage))]
    private void LastPage() => CurrentPage = TotalPages;

    private bool CanGoFirstPage() => HasPreviousPage;
    private bool CanGoPreviousPage() => HasPreviousPage;
    private bool CanGoNextPage() => HasNextPage;
    private bool CanGoLastPage() => HasNextPage;
}
