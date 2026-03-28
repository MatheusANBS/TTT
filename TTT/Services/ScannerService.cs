// File: Services/ScannerService.cs

using System.Collections.Concurrent;
using TTT.Models;
using TTT.Utils;

namespace TTT.Services;

/// <summary>
/// Detailed progress information reported during a scan operation.
/// </summary>
public record ScanProgress(
    int Percent,
    long CurrentAddress,
    double MbScanned,
    int ResultCount,
    string StatusText,
    double EstimatedSecondsRemaining = -1);

public sealed record ScanResultRefresh(
    MemoryScanResult Result,
    byte[] RawBytes,
    string DisplayValue);

/// <summary>
/// High-performance memory scanner.
/// Optimisations applied vs. the original:
/// <list type="bullet">
///   <item>Parallel region scanning (<see cref="Parallel.ForEach"/>)</item>
///   <item>Natural-alignment stride for numeric types (skips 75% of offsets for 4-byte values)</item>
///   <item>SIMD-accelerated <see cref="ReadOnlySpan{T}.SequenceEqual"/> for pattern matching</item>
///   <item>Reusable per-thread chunk buffers (zero allocation per read)</item>
///   <item>Lock-free <c>ReadBytesUnsafe</c> for the hot read path</item>
///   <item>Batched re-reads in NextScan — groups nearby addresses into a single kernel call</item>
/// </list>
/// </summary>
public sealed class ScannerService(MemoryService memory)
{
    private readonly LogService _log = LogService.Instance;

    /// <summary>Results from the most recent scan.</summary>
    public List<MemoryScanResult> CurrentResults { get; private set; } = [];

    /// <summary>Returns <see langword="true"/> if a first scan has been performed.</summary>
    public bool HasPreviousScan => CurrentResults.Count > 0;

    // ═══════════════════════════════════════════════════════════════════════
    // ── First Scan (parallel, aligned, Span-matched) ─────────────────────
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<List<MemoryScanResult>> FirstScanAsync(
        string searchValue,
        ScanValueType valueType,
        ScanType scanType,
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default)
    {
        byte[]? target = scanType == ScanType.ExactValue ? searchValue.ToBytes(valueType) : null;
        int valueSize = valueType.ByteSize();
        if (valueSize == 0 && valueType == ScanValueType.String)
            valueSize = target?.Length ?? 2;

        // Alignment: numeric types are virtually always naturally aligned in memory.
        // Strings and unknown scans scan byte-by-byte.
        int stride = (scanType == ScanType.ExactValue && valueType != ScanValueType.String)
            ? valueSize
            : 1;

        _log.Info($"FirstScan: type={valueType}, scan={scanType}, value='{searchValue}', stride={stride}");

        var regions = memory.EnumerateReadableRegions().ToList();
        long totalBytes = regions.Sum(r => r.Size);
        long scannedBytes = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Thread-safe collector: each parallel task appends to its own local list.
        var resultBag = new ConcurrentBag<List<MemoryScanResult>>();
        int globalResultCount = 0;
        bool capReached = false;

        await Task.Run(() =>
        {
            int maxDop = Math.Max(1, Environment.ProcessorCount - 1);

            Parallel.ForEach(
                regions,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxDop,
                    CancellationToken      = ct
                },
                // Thread-local init: each thread gets its own chunk buffer + result list.
                () => (
                    Buffer:  new byte[Constants.SCAN_CHUNK_SIZE],
                    Results: new List<MemoryScanResult>()
                ),
                // Body — executed per region per thread.
                (region, loopState, threadLocal) =>
                {
                    var (regionBase, regionSize) = region;
                    var chunkBuf = threadLocal.Buffer;
                    var localResults = threadLocal.Results;

                    for (long offset = 0; offset < regionSize; offset += Constants.SCAN_CHUNK_SIZE)
                    {
                        if (loopState.IsStopped || capReached) break;
                        ct.ThrowIfCancellationRequested();

                        int chunkSize = (int)Math.Min(Constants.SCAN_CHUNK_SIZE, regionSize - offset);
                        int bytesRead = memory.ReadBytesUnsafe(regionBase + offset, chunkBuf, chunkSize);
                        if (bytesRead == 0)
                        {
                            Interlocked.Add(ref scannedBytes, chunkSize);
                            continue;
                        }

                        int searchSize = valueSize > 0 ? valueSize : (target?.Length ?? 2);
                        int limit = bytesRead - searchSize + 1;
                        var chunkSpan = chunkBuf.AsSpan(0, bytesRead);

                        for (int i = 0; i < limit; i += stride)
                        {
                            if (scanType == ScanType.UnknownInitialValue)
                            {
                                var raw = chunkSpan.Slice(i, searchSize).ToArray();
                                localResults.Add(new MemoryScanResult
                                {
                                    Address      = regionBase + offset + i,
                                    RawBytes     = raw,
                                    DisplayValue = raw.ReadValueAs(valueType),
                                    Type         = valueType
                                });
                            }
                            else if (target is not null &&
                                     chunkSpan.Slice(i, target.Length).SequenceEqual(target))
                            {
                                var raw = chunkSpan.Slice(i, target.Length).ToArray();
                                localResults.Add(new MemoryScanResult
                                {
                                    Address      = regionBase + offset + i,
                                    RawBytes     = raw,
                                    DisplayValue = raw.ReadValueAs(valueType),
                                    Type         = valueType
                                });
                            }

                            // Global cap check (approximate — OK if a few extras slip in)
                            if (Volatile.Read(ref globalResultCount) + localResults.Count >= Constants.SCAN_MAX_RESULTS)
                            {
                                capReached = true;
                                _log.Warn($"Scan cap ({Constants.SCAN_MAX_RESULTS}) atingido, interrompendo.");
                                loopState.Stop();
                                break;
                            }
                        }

                        Interlocked.Add(ref scannedBytes, chunkSize);

                        // Progress report (throttled to every ~4 MB)
                        long scanned = Volatile.Read(ref scannedBytes);
                        if (progress is not null && scanned % (4 * 1024 * 1024) < chunkSize)
                            ReportProgress(progress, scanned, totalBytes, regionBase + offset,
                                Volatile.Read(ref globalResultCount) + localResults.Count, sw);
                    }

                    return threadLocal;
                },
                // Thread-local finally: merge local results into the bag.
                threadLocal =>
                {
                    if (threadLocal.Results.Count > 0)
                    {
                        Interlocked.Add(ref globalResultCount, threadLocal.Results.Count);
                        resultBag.Add(threadLocal.Results);
                    }
                });
        }, ct);

        // Merge all per-thread result lists, sorted by address.
        var results = new List<MemoryScanResult>(globalResultCount);
        foreach (var list in resultBag)
            results.AddRange(list);
        results.Sort((a, b) => a.Address.CompareTo(b.Address));

        if (results.Count > Constants.SCAN_MAX_RESULTS)
            results.RemoveRange(Constants.SCAN_MAX_RESULTS, results.Count - Constants.SCAN_MAX_RESULTS);

        CurrentResults = results;
        _log.Info($"FirstScan completo: {results.Count} resultados em {sw.Elapsed.TotalSeconds:F2}s");
        progress?.Report(new ScanProgress(100, 0, Volatile.Read(ref scannedBytes) / 1_048_576.0,
            results.Count, $"Scan completo: {results.Count:N0} resultados", 0));
        return results;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ── Next Scan (batched reads + parallel) ─────────────────────────────
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<List<MemoryScanResult>> NextScanAsync(
        string searchValue,
        ScanType scanType,
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!HasPreviousScan)
            throw new InvalidOperationException("Nenhum scan anterior para continuar.");

        var previous = CurrentResults;
        int total = previous.Count;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        _log.Info($"NextScan: scan={scanType}, value='{searchValue}', candidatos={total}");

        // Sort previous by address for batching.
        var sorted = previous.OrderBy(r => r.Address).ToList();

        // Build contiguous batches: group addresses within 4 KB of each other → single kernel call.
        const long BATCH_GAP = 4096;
        var batches = BuildBatches(sorted, BATCH_GAP);

        byte[]? target = scanType == ScanType.ExactValue
            ? searchValue.ToBytes(previous[0].Type)
            : null;

        var filteredBag = new ConcurrentBag<List<MemoryScanResult>>();
        long processed = 0;

        await Task.Run(() =>
        {
            int maxDop = Math.Max(1, Environment.ProcessorCount - 1);

            Parallel.ForEach(
                batches,
                new ParallelOptions { MaxDegreeOfParallelism = maxDop, CancellationToken = ct },
                () => new List<MemoryScanResult>(),
                (batch, _, localFiltered) =>
                {
                    ct.ThrowIfCancellationRequested();

                    // Read the entire contiguous block in one kernel call.
                    int blockSize = (int)(batch.EndAddress - batch.StartAddress);
                    var block = new byte[blockSize];
                    int bytesRead = memory.ReadBytesUnsafe(batch.StartAddress, block, blockSize);

                    foreach (var prev in batch.Results)
                    {
                        int offsetInBlock = (int)(prev.Address - batch.StartAddress);
                        int size = prev.RawBytes.Length;

                        byte[]? current = null;
                        if (bytesRead > 0 && offsetInBlock >= 0 && offsetInBlock + size <= bytesRead)
                        {
                            current = new byte[size];
                            Array.Copy(block, offsetInBlock, current, 0, size);
                        }
                        else
                        {
                            current = memory.ReadBytes(prev.Address, size);
                        }

                        if (current is null)
                        {
                            Interlocked.Increment(ref processed);
                            continue;
                        }

                        bool match = scanType switch
                        {
                            ScanType.ExactValue          => target is not null && current.AsSpan().SequenceEqual(target),
                            ScanType.UnknownInitialValue => true,
                            ScanType.ChangedValue        => !current.AsSpan().SequenceEqual(prev.RawBytes),
                            ScanType.UnchangedValue      => current.AsSpan().SequenceEqual(prev.RawBytes),
                            ScanType.IncreasedValue      => current.ToLongValue(prev.Type) > prev.RawBytes.ToLongValue(prev.Type),
                            ScanType.DecreasedValue      => current.ToLongValue(prev.Type) < prev.RawBytes.ToLongValue(prev.Type),
                            _ => false
                        };

                        if (match)
                        {
                            localFiltered.Add(new MemoryScanResult
                            {
                                Address          = prev.Address,
                                RawBytes         = current,
                                DisplayValue     = current.ReadValueAs(prev.Type),
                                Type             = prev.Type,
                                PreviousRawBytes = prev.RawBytes
                            });
                        }

                        long p = Interlocked.Increment(ref processed);
                        if (progress is not null && p % 10000 == 0)
                        {
                            int pct = (int)(p / (double)total * 100);
                            double elapsed = sw.Elapsed.TotalSeconds;
                            double eta = elapsed > 0 && pct > 0 ? elapsed / pct * (100 - pct) : -1;
                            progress.Report(new ScanProgress(pct, prev.Address,
                                p * prev.RawBytes.Length / 1_048_576.0, 0,
                                $"Verificando... {pct}% ({p:N0}/{total:N0})", eta));
                        }
                    }

                    return localFiltered;
                },
                localFiltered =>
                {
                    if (localFiltered.Count > 0)
                        filteredBag.Add(localFiltered);
                });
        }, ct);

        var filtered = new List<MemoryScanResult>();
        foreach (var list in filteredBag)
            filtered.AddRange(list);
        filtered.Sort((a, b) => a.Address.CompareTo(b.Address));

        CurrentResults = filtered;
        _log.Info($"NextScan completo: {filtered.Count} restantes em {sw.Elapsed.TotalSeconds:F2}s");
        progress?.Report(new ScanProgress(100, 0, 0, filtered.Count,
            $"Próximo scan completo: {filtered.Count:N0} resultados", 0));
        return filtered;
    }

    /// <summary>Clears all scan results, allowing a fresh first scan.</summary>
    public void Reset()
    {
        CurrentResults = [];
        _log.Info("Scan reset.");
    }

    public async Task<List<ScanResultRefresh>> RefreshVisibleResultsAsync(
        IEnumerable<MemoryScanResult> results,
        CancellationToken ct = default)
    {
        var snapshot = results
            .Where(result => result.RawBytes.Length > 0)
            .OrderBy(result => result.Address)
            .ToList();

        if (snapshot.Count == 0)
            return [];

        const long BATCH_GAP = 4096;
        var batches = BuildBatches(snapshot, BATCH_GAP);
        var refreshedBag = new ConcurrentBag<List<ScanResultRefresh>>();

        await Task.Run(() =>
        {
            int maxDop = Math.Max(1, Environment.ProcessorCount - 1);

            Parallel.ForEach(
                batches,
                new ParallelOptions { MaxDegreeOfParallelism = maxDop, CancellationToken = ct },
                () => new List<ScanResultRefresh>(),
                (batch, _, localRefreshes) =>
                {
                    ct.ThrowIfCancellationRequested();

                    int blockSize = (int)(batch.EndAddress - batch.StartAddress);
                    var block = new byte[blockSize];
                    int bytesRead = memory.ReadBytesUnsafe(batch.StartAddress, block, blockSize);

                    foreach (var result in batch.Results)
                    {
                        int size = result.RawBytes.Length;
                        if (size <= 0)
                            continue;

                        int offsetInBlock = (int)(result.Address - batch.StartAddress);
                        byte[]? current = null;

                        if (bytesRead > 0 && offsetInBlock >= 0 && offsetInBlock + size <= bytesRead)
                        {
                            current = new byte[size];
                            Array.Copy(block, offsetInBlock, current, 0, size);
                        }
                        else
                        {
                            current = memory.ReadBytes(result.Address, size);
                        }

                        if (current is null)
                            continue;

                        localRefreshes.Add(new ScanResultRefresh(
                            result,
                            current,
                            current.ReadValueAs(result.Type)));
                    }

                    return localRefreshes;
                },
                localRefreshes =>
                {
                    if (localRefreshes.Count > 0)
                        refreshedBag.Add(localRefreshes);
                });
        }, ct);

        var refreshed = new List<ScanResultRefresh>();
        foreach (var list in refreshedBag)
            refreshed.AddRange(list);

        return refreshed;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ── Helpers ───────────────────────────────────────────────────────────
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Groups sorted results into batches where consecutive addresses are within
    /// <paramref name="maxGap"/> bytes, so each batch can be read in a single kernel call.
    /// </summary>
    private static List<ScanBatch> BuildBatches(List<MemoryScanResult> sorted, long maxGap)
    {
        if (sorted.Count == 0) return [];
        var batches = new List<ScanBatch>();
        var current = new ScanBatch { StartAddress = sorted[0].Address };
        current.Results.Add(sorted[0]);
        long currentEnd = sorted[0].Address + sorted[0].RawBytes.Length;

        for (int i = 1; i < sorted.Count; i++)
        {
            var r = sorted[i];
            if (r.Address - currentEnd <= maxGap &&
                (r.Address + r.RawBytes.Length - current.StartAddress) < 16 * 1024 * 1024) // cap batch at 16 MB
            {
                current.Results.Add(r);
                currentEnd = Math.Max(currentEnd, r.Address + r.RawBytes.Length);
            }
            else
            {
                current.EndAddress = currentEnd;
                batches.Add(current);
                current = new ScanBatch { StartAddress = r.Address };
                current.Results.Add(r);
                currentEnd = r.Address + r.RawBytes.Length;
            }
        }
        current.EndAddress = currentEnd;
        batches.Add(current);
        return batches;
    }

    private sealed class ScanBatch
    {
        public long StartAddress;
        public long EndAddress;
        public readonly List<MemoryScanResult> Results = [];
    }

    private static void ReportProgress(
        IProgress<ScanProgress>? progress,
        long scannedBytes, long totalBytes,
        long currentAddress, int resultCount,
        System.Diagnostics.Stopwatch sw)
    {
        if (progress is null || totalBytes == 0) return;
        int pct = (int)(scannedBytes / (double)totalBytes * 100);
        double elapsed = sw.Elapsed.TotalSeconds;
        double eta = elapsed > 0 && pct > 0 ? elapsed / pct * (100 - pct) : -1;
        progress.Report(new ScanProgress(
            pct, currentAddress,
            scannedBytes / 1_048_576.0, resultCount,
            $"Scanning 0x{currentAddress:X}... {pct}%", eta));
    }
}
