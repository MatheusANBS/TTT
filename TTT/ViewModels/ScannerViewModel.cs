// File: ViewModels/ScannerViewModel.cs

using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TTT.Models;
using TTT.Services;
using TTT.Utils;

namespace TTT.ViewModels;

public sealed partial class ScannerViewModel : BaseViewModel
{
    private readonly ScannerService                  _scanner;
    private readonly ObservableCollection<MemoryEntry> _masterAddressList;
    private readonly DispatcherTimer                 _resultRefreshTimer;
    private CancellationTokenSource?                 _cts;
    private readonly List<MemoryScanResult>          _allVisibleScanResults = [];
    private int _isResultRefreshRunning;

    // ── Scan parameters ───────────────────────────────────────────────────
    [ObservableProperty] private ScanValueType _valueType  = ScanValueType.Byte4;
    [ObservableProperty] private ScanType      _scanType   = ScanType.ExactValue;
    [ObservableProperty] private string        _valueText  = string.Empty;
    [ObservableProperty] private string        _resultValueFilter = string.Empty;
    [ObservableProperty] private int           _currentPage = 1;
    [ObservableProperty] private int           _pageSize    = 100;
    [ObservableProperty] private int           _totalPages  = 1;
    [ObservableProperty] private int           _visibleResultCount;

    // ── Scan results ──────────────────────────────────────────────────────
    public ObservableCollection<MemoryScanResult> ScanResults { get; } = [];

    // ── Progress ──────────────────────────────────────────────────────────
    [ObservableProperty] private int    _progressPercent;
    [ObservableProperty] private string _statusText       = "Pronto.";
    [ObservableProperty] private string _etaText          = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FirstScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetScanCommand))]
    private bool   _isScanning;

    [ObservableProperty] private int    _resultCount;

    // ── Mini address-list preview (bottom panel) ──────────────────────────
    public ObservableCollection<MemoryEntry> AddressListPreview { get; }

    // ── Navigation ────────────────────────────────────────────────────────
    public event Action? RequestNavigateToAddressList;

    // ── Scan state ────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetScanCommand))]
    private bool _hasPreviousScan;

    public ScannerViewModel(ScannerService scanner, ObservableCollection<MemoryEntry> masterAddressList)
    {
        _scanner           = scanner;
        _masterAddressList = masterAddressList;
        AddressListPreview = masterAddressList;   // same collection reference

        _resultRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Constants.ADDRESS_LIST_TIMER_MS)
        };
        _resultRefreshTimer.Tick += OnResultRefreshTick;
        _resultRefreshTimer.Start();
    }

    public IReadOnlyList<int> PageSizeOptions { get; } = [50, 100, 200];

    public string PageInfoText => $"Página {CurrentPage} de {TotalPages}";
    public bool HasPreviousPage => CurrentPage > 1;
    public bool HasNextPage => CurrentPage < TotalPages;

    partial void OnResultValueFilterChanged(string value)
    {
        CurrentPage = 1;
        RebuildPagedResults();
    }

    partial void OnCurrentPageChanged(int value)
    {
        RebuildPagedResults();
        OnPropertyChanged(nameof(PageInfoText));
        OnPropertyChanged(nameof(HasPreviousPage));
        OnPropertyChanged(nameof(HasNextPage));
    }

    partial void OnTotalPagesChanged(int value)
    {
        OnPropertyChanged(nameof(PageInfoText));
        OnPropertyChanged(nameof(HasPreviousPage));
        OnPropertyChanged(nameof(HasNextPage));
    }

    partial void OnPageSizeChanged(int value)
    {
        if (value <= 0)
        {
            PageSize = 100;
            return;
        }

        CurrentPage = 1;
        RebuildPagedResults();
    }

    // ── Commands ──────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanFirstScan))]
    private async Task FirstScanAsync()
    {
        await SafeRunAsync(async () =>
        {
            IsScanning = true;
            using var cts = new CancellationTokenSource();
            _cts = cts;

            var progress = new Progress<ScanProgress>(p =>
            {
                OnUI(() =>
                {
                    ProgressPercent = p.Percent;
                    StatusText      = p.StatusText;
                    ResultCount     = p.ResultCount;
                    EtaText         = p.EstimatedSecondsRemaining > 0
                        ? $"~{p.EstimatedSecondsRemaining:F0}s restantes"
                        : string.Empty;
                });
            });

            await _scanner.FirstScanAsync(ValueText, ValueType, ScanType, progress, cts.Token);
            await RefreshResultsAsync();
            HasPreviousScan = _scanner.HasPreviousScan;
        });
        IsScanning      = false;
        ProgressPercent = 100;
        EtaText         = string.Empty;
        _cts            = null;
    }

    private bool CanFirstScan() => !IsScanning;

    [RelayCommand(CanExecute = nameof(CanNextScan))]
    private async Task NextScanAsync()
    {
        await SafeRunAsync(async () =>
        {
            IsScanning = true;
            using var cts = new CancellationTokenSource();
            _cts = cts;

            var progress = new Progress<ScanProgress>(p =>
            {
                OnUI(() =>
                {
                    ProgressPercent = p.Percent;
                    StatusText      = p.StatusText;
                    ResultCount     = p.ResultCount;
                    EtaText         = p.EstimatedSecondsRemaining > 0
                        ? $"~{p.EstimatedSecondsRemaining:F0}s restantes"
                        : string.Empty;
                });
            });

            await _scanner.NextScanAsync(ValueText, ScanType, progress, cts.Token);
            await RefreshResultsAsync();
        });
        IsScanning      = false;
        ProgressPercent = 100;
        EtaText         = string.Empty;
        _cts            = null;
    }

    private bool CanNextScan() => !IsScanning && HasPreviousScan;

    [RelayCommand]
    private void CancelScan()
    {
        _cts?.Cancel();
        StatusText = "Cancelando...";
    }

    [RelayCommand(CanExecute = nameof(CanReset))]
    public void ResetScan()
    {
        _cts?.Cancel();
        _scanner.Reset();
        _allVisibleScanResults.Clear();
        ScanResults.Clear();
        CurrentPage = 1;
        TotalPages = 1;
        VisibleResultCount = 0;
        HasPreviousScan = false;
        ProgressPercent = 0;
        StatusText      = "Pronto.";
        ResultCount     = 0;
        EtaText         = string.Empty;
    }

    private bool CanReset() => HasPreviousScan || IsScanning;

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

    [RelayCommand]
    private void AddResultToAddressList(object parameter)
    {
        if (parameter is System.Collections.IList items && items.Count > 0)
        {
            foreach (var item in items)
            {
                if (item is MemoryScanResult result)
                {
                    _masterAddressList.Add(new MemoryEntry
                    {
                        Address     = result.Address,
                        Type        = result.Type,
                        Description = $"Endereço 0x{result.Address:X8}",
                    });
                }
            }
            ShowSnackbar($"{items.Count} endereços adicionados à lista.");
        }
        else if (parameter is MemoryScanResult singleResult)
        {
            _masterAddressList.Add(new MemoryEntry
            {
                Address     = singleResult.Address,
                Type        = singleResult.Type,
                Description = $"Endereço 0x{singleResult.Address:X8}",
            });
            ShowSnackbar($"Adicionado: 0x{singleResult.Address:X8}");
        }
    }

    [RelayCommand]
    private void GoToAddressList() => RequestNavigateToAddressList?.Invoke();

    // ── Helpers ───────────────────────────────────────────────────────────
    private void OnResultRefreshTick(object? sender, EventArgs e)
    {
        if (IsScanning || !HasPreviousScan || ScanResults.Count == 0)
            return;

        _ = RefreshVisibleResultValuesAsync();
    }

    private Task RefreshResultsAsync()
    {
        var results = _scanner.CurrentResults;
        OnUI(() =>
        {
            _allVisibleScanResults.Clear();
            var max = Math.Min(results.Count, 2000);
            for (var i = 0; i < max; i++)
                _allVisibleScanResults.Add(results[i]);

            CurrentPage = 1;
            RebuildPagedResults();

            StatusText  = results.Count > 2000
                ? $"{results.Count:N0} resultados (exibindo 2.000)"
                : $"{results.Count:N0} resultados";
            ResultCount = results.Count;
        });
        return Task.CompletedTask;
    }

    private async Task RefreshVisibleResultValuesAsync()
    {
        if (Interlocked.Exchange(ref _isResultRefreshRunning, 1) == 1)
            return;

        try
        {
            var visibleResults = ScanResults.ToList();
            if (visibleResults.Count == 0)
                return;

            var refreshes = await _scanner.RefreshVisibleResultsAsync(visibleResults);
            OnUI(() =>
            {
                foreach (var refresh in refreshes)
                {
                    refresh.Result.RawBytes = refresh.RawBytes;
                    refresh.Result.DisplayValue = refresh.DisplayValue;
                }

                if (!string.IsNullOrWhiteSpace(ResultValueFilter))
                    RebuildPagedResults();
            });
        }
        finally
        {
            Interlocked.Exchange(ref _isResultRefreshRunning, 0);
        }
    }

    private void RebuildPagedResults()
    {
        var filtered = _allVisibleScanResults
            .Where(MatchesValueFilter)
            .ToList();

        VisibleResultCount = filtered.Count;

        TotalPages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)PageSize));
        if (CurrentPage > TotalPages)
            CurrentPage = TotalPages;
        if (CurrentPage < 1)
            CurrentPage = 1;

        var pageItems = filtered
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        ScanResults.Clear();
        foreach (var item in pageItems)
            ScanResults.Add(item);

        FirstPageCommand.NotifyCanExecuteChanged();
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        LastPageCommand.NotifyCanExecuteChanged();
    }

    private bool MatchesValueFilter(MemoryScanResult result)
    {
        if (string.IsNullOrWhiteSpace(ResultValueFilter))
            return true;

        return result.DisplayValue.Contains(ResultValueFilter.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
