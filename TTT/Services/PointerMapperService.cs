// File: Services\PointerMapperService.cs

using System.Collections.Concurrent;
using TTT.Models;
using TTT.Utils;

namespace TTT.Services;

/// <summary>
/// Scans for pointer chains that lead to a given target address.
/// Uses iterative BFS with a single bulk memory scan per depth level
/// for maximum performance.
/// </summary>
public sealed class PointerMapperService(MemoryService memory)
{
    private readonly LogService _log = LogService.Instance;

    /// <summary>
    /// Max intermediate paths tracked per BFS level.
    /// Raising this dramatically reduces missed chains at the cost of more memory.
    /// CE has no cap because it uses a pointer-map file; we compensate with a large ceiling.
    /// </summary>
    private const int MAX_PATHS_PER_LEVEL = 100_000;

    public async Task<List<SavedPointer>> FindPointerChainsAsync(
        long targetAddress,
        int maxDepth = 4,
        int maxOffset = 0xFFF,
        bool prioritizeStaticModules = true,
        bool allowNegativeOffsets = false,
        ScanValueType valueType = ScanValueType.Byte4,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var results = new List<SavedPointer>();
        _log.Info($"PointerMap: target=0x{targetAddress:X}, depth={maxDepth}, maxOffset=0x{maxOffset:X}");

        int pointerSize = memory.TargetPointerSize == 4 ? 4 : 8;
        _log.Info($"PointerMap config: ptrSize={pointerSize}, allowNegativeOffsets={allowNegativeOffsets}");

        var modules = memory.GetModules().ToList();

        var allRegions = memory.EnumerateReadableRegions().ToList();
        var imageRegions   = allRegions.Where(r => IsImageRegion(r.Base, modules)).ToList();
        var privateRegions = allRegions.Except(imageRegions).ToList();

        var orderedRegions = prioritizeStaticModules
            ? imageRegions.Concat(privateRegions).ToList()
            : allRegions;

        // BFS state: address → list of offset chains that reach it.
        // We group by address so the bulk scanner only needs unique targets.
        var pathsByAddress = new Dictionary<long, List<List<long>>>
        {
            [targetAddress] = [new List<long>()]
        };

        await Task.Run(() =>
        {
            for (int depth = 1; depth <= maxDepth; depth++)
            {
                ct.ThrowIfCancellationRequested();
                if (pathsByAddress.Count == 0) break;

                var targetSet = pathsByAddress.Keys.ToHashSet();
                progress?.Report($"Nível {depth}: escaneando {targetSet.Count} alvos distintos...");

                var bulkResults = BulkFindPointers(targetSet, orderedRegions, maxOffset, allowNegativeOffsets, pointerSize, ct);
                progress?.Report($"Nível {depth}: {bulkResults.Count} ponteiros brutos encontrados.");

                var nextPathsByAddress = new Dictionary<long, List<List<long>>>();
                int totalNextPaths = 0;

                foreach (var (ptrAddr, matchedTarget, offset) in bulkResults)
                {
                    ct.ThrowIfCancellationRequested();

                    if (!pathsByAddress.TryGetValue(matchedTarget, out var existingPaths))
                        continue;

                    foreach (var existingOffsets in existingPaths)
                    {
                        // Build offset chain: this level's offset prepended to the accumulated tail
                        var newOffsets = new List<long>(existingOffsets.Count + 1) { offset };
                        newOffsets.AddRange(existingOffsets);

                        // Check if ptrAddr is inside a static module → complete chain
                        var chain = BuildChain(ptrAddr, modules);
                        if (chain is not null)
                        {
                            chain.Offsets    = newOffsets;
                            chain.ValueType  = valueType;
                            results.Add(chain);
                            if (results.Count % 500 == 0)
                                progress?.Report($"  {results.Count} cadeias encontradas...");
                        }
                        else if (depth < maxDepth && totalNextPaths < MAX_PATHS_PER_LEVEL)
                        {
                            // Queue for next depth
                            if (!nextPathsByAddress.TryGetValue(ptrAddr, out var pathList))
                            {
                                pathList = new List<List<long>>();
                                nextPathsByAddress[ptrAddr] = pathList;
                            }
                            pathList.Add(newOffsets);
                            totalNextPaths++;
                        }
                    }
                }

                pathsByAddress = nextPathsByAddress;
                progress?.Report($"Nível {depth} completo: {results.Count} cadeias, {pathsByAddress.Count} alvos para próximo nível.");
            }

            progress?.Report($"Mapeamento completo: {results.Count} cadeias encontradas. Validando...");

            // Post-scan validation & hop-stability analysis
            int verified = 0;
            foreach (var chain in results)
            {
                // --- Resolve and verify ---
                try
                {
                    long baseAddr = chain.ModuleBase + chain.ModuleOffset;
                    long resolved = memory.ResolvePointerChain(baseAddr, chain.Offsets);
                    chain.IsVerified = resolved == targetAddress;
                    if (chain.IsVerified) verified++;
                }
                catch { chain.IsVerified = false; }

                // --- Walk chain hop-by-hop to classify each INTERMEDIATE pointer storage address ---
                //
                // Stability = stability of the ADDRESS WHERE the next pointer is stored,
                // NOT where the pointer VALUE points (values always land in heap for game objects).
                //
                // Chain: base →[deref]→ P1 →[+off1]→ addr1 →[deref]→ P2 →[+off2]→ TARGET
                //   addr1 is the intermediate storage address we care about.
                //   For a 1-hop chain there are 0 intermediate addresses → "Direto".
                //   For a 2-hop chain, addr1 is checked.
                try
                {
                    int stableHops = 0;
                    int totalHops  = 0;
                    long walkAddr  = chain.ModuleBase + chain.ModuleOffset;
                    int  hopIndex  = 0;
                    int  lastIndex = chain.Offsets.Count - 1;

                    foreach (var offset in chain.Offsets)
                    {
                        long ptrValue = memory.ReadPointer(walkAddr);
                        if (ptrValue == 0) break;
                        walkAddr = ptrValue + offset;

                        // Skip the last hop: walkAddr is now the TARGET, not a pointer storage
                        if (hopIndex < lastIndex)
                        {
                            totalHops++;
                            uint regionType = memory.GetRegionType(walkAddr);
                            if ((regionType & Constants.MEM_IMAGE)  != 0 ||
                                (regionType & Constants.MEM_MAPPED) != 0)
                                stableHops++;
                        }
                        hopIndex++;
                    }

                    chain.StableHops = stableHops;
                    chain.TotalHops  = totalHops;
                }
                catch { /* leave 0/0 — displayed as "Direto" */ }

                chain.Score = ComputeScore(chain);
            }

            progress?.Report($"{verified}/{results.Count} cadeias verificadas com sucesso.");
        }, ct);

        // Sort by score descending (most reliable first).
        // No artificial max-results truncation is applied here.
        results.Sort((a, b) => b.Score.CompareTo(a.Score));

        _log.Info($"PointerMap done: {results.Count} chains, {results.Count(c => c.IsVerified)} verified, " +
                  $"{results.Count(c => c.StableHops == c.TotalHops && c.TotalHops > 0)} fully-static.");
        return results;
    }

    // ── Bulk scanner ──────────────────────────────────────────────────────

    /// <summary>
    /// Performs a single parallel pass over all memory regions, finding every
    /// 8-byte-aligned value that points within <paramref name="maxOffset"/> bytes
    /// of any address in <paramref name="targets"/>.
    /// Uses thread-local reusable buffers to avoid GC pressure.
    /// </summary>
    private List<(long PtrAddress, long TargetAddress, long Offset)> BulkFindPointers(
        HashSet<long> targets,
        List<(long Base, long Size)> regions,
        int maxOffset,
        bool allowNegativeOffsets,
        int pointerSize,
        CancellationToken ct)
    {
        // Sorted array for O(log N) binary-search per pointer value
        long[] sorted = targets.OrderBy(t => t).ToArray();

        var found = new ConcurrentBag<(long, long, long)>();

        Parallel.ForEach(
            regions,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
                CancellationToken = ct
            },
            // Thread-local: reusable chunk buffer
            () => new byte[Constants.SCAN_CHUNK_SIZE],
            (region, _, buffer) =>
            {
                var (regionBase, regionSize) = region;

                for (long off = 0; off < regionSize; off += Constants.SCAN_CHUNK_SIZE)
                {
                    ct.ThrowIfCancellationRequested();
                    int chunkSize = (int)Math.Min(Constants.SCAN_CHUNK_SIZE, regionSize - off);
                    int bytesRead = memory.ReadBytesUnsafe(regionBase + off, buffer, chunkSize);
                    if (bytesRead < pointerSize) continue;

                    // Hoist bounds out of the inner loop — computed once per chunk
                    // Without negative offsets: delta = target - ptrValue must be in [0, maxOffset]
                    //   → ptrValue in [sorted[0] - maxOffset, sorted[^1]]
                    // With negative offsets:    delta = target - ptrValue must be in [-maxOffset, maxOffset]
                    //   → ptrValue in [sorted[0] - maxOffset, sorted[^1] + maxOffset]
                    long lowerBound = sorted[0] - maxOffset;
                    long upperBound = allowNegativeOffsets ? sorted[^1] + maxOffset : sorted[^1];

                    // Step of 4 catches valid pointers in packed structs while keeping scan cost manageable.
                    int step = 4;
                    int limit = bytesRead - pointerSize + 1;
                    for (int i = 0; i < limit; i += step)
                    {
                        long ptrValue = pointerSize == 4
                            ? (long)BitConverter.ToUInt32(buffer, i)
                            : BitConverter.ToInt64(buffer, i);

                        // Reject values that cannot be valid pointers in 64-bit Windows userspace.
                        // This eliminates large classes of false positives (null pointers, kernel
                        // addresses, small integers) before the more expensive binary-search step.
                        if (pointerSize == 4)
                        {
                            if ((ulong)ptrValue is < 0x10000UL or > 0x7FFF_FFFFUL)
                                continue;
                        }
                        else
                        {
                            if ((ulong)ptrValue is < 0x10000UL or > 0x7FFF_FFFF_FFFFFUL)
                                continue;
                        }

                        if (ptrValue < lowerBound || ptrValue > upperBound)
                            continue;

                        // Binary search for the first target >= ptrValue - maxOffset
                        long searchFrom = allowNegativeOffsets ? ptrValue - maxOffset : ptrValue;
                        int idx = Array.BinarySearch(sorted, searchFrom);
                        if (idx < 0) idx = ~idx;

                        // Walk forward: delta = target - ptrValue must be in [-maxOffset, +maxOffset] (or [0, maxOffset])
                        for (int j = idx; j < sorted.Length; j++)
                        {
                            long delta = sorted[j] - ptrValue;
                            if (delta > maxOffset) break;
                            if (allowNegativeOffsets ? delta >= -maxOffset : delta >= 0)
                                found.Add((regionBase + off + i, sorted[j], delta));
                        }
                    }
                }
                return buffer;
            },
            _ => { } // thread-local finally: nothing to dispose
        );

        return found.ToList();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// If <paramref name="address"/> lies inside a known static module, creates a <see cref="SavedPointer"/>
    /// with the module-relative offset as the chain base. Returns <see langword="null"/> otherwise.
    /// </summary>
    private static SavedPointer? BuildChain(
        long address,
        List<(string Name, long Base, int Size)> modules)
    {
        foreach (var (name, modBase, modSize) in modules)
        {
            if (address >= modBase && address < modBase + modSize)
            {
                return new SavedPointer
                {
                    ModuleBase   = modBase,
                    ModuleName   = name,
                    ModuleOffset = address - modBase,
                    Offsets      = [],
                    Description  = $"{name}+0x{address - modBase:X}"
                };
            }
        }
        return null;
    }

    private static bool IsImageRegion(long regionBase, List<(string Name, long Base, int Size)> modules) =>
        modules.Any(m => regionBase >= m.Base && regionBase < m.Base + m.Size);

    // ── Scoring ───────────────────────────────────────────────────────────

    /// <summary>
    /// Assigns a reliability score to a pointer chain.
    /// Scoring is tier-based: stability class always dominates secondary factors.
    ///
    /// Tier order (highest → lowest):
    ///   Direto    (0 intermediate hops)                     → +3000
    ///   Estático  (all intermediate hops through MEM_IMAGE) → +2000
    ///   Misto     (mix of image and heap hops)              → +1000..+2000
    ///   Dinâmico  (all intermediate hops through heap)      → 500 − (hops × 400)
    ///
    /// Within the same tier: verified > short chain > aligned offsets > .exe base.
    /// </summary>
    private static int ComputeScore(SavedPointer ptr)
    {
        int score = 0;

        // ── Stability tier (dominates ranking) ──────────────────────────
        if (ptr.TotalHops == 0)
        {
            // Direto: exe+static_offset → TARGET in one shot — no heap dependency
            score += 3000;
        }
        else if (ptr.StableHops == ptr.TotalHops)
        {
            // Estático: every intermediate address is inside a PE image/mapping
            score += 2000;
        }
        else if (ptr.StableHops > 0)
        {
            // Misto: partial — scale between 1000 and 2000 by ratio
            score += 1000 + (int)(1000.0 * ptr.StableHops / ptr.TotalHops);
        }
        else
        {
            // Dinâmico: all intermediate addresses are heap — subtract per heap hop
            score += 500;
            score -= ptr.TotalHops * 400;
        }

        // ── Secondary factors (break ties within same tier) ──────────────

        // Verified at scan time
        if (ptr.IsVerified) score += 300;

        // Shorter chains are more reliable
        score -= ptr.Offsets.Count * 50;

        // .exe base is more stable than DLL bases
        if (ptr.ModuleName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            score += 100;

        // Offset alignment heuristic
        foreach (var offset in ptr.Offsets)
        {
            long abs = Math.Abs(offset);
            if (offset < 0)        score -= 20;
            if (abs == 0)          score += 15;
            else if (abs % 8 == 0) score += 10;
            else if (abs % 4 == 0) score += 5;
            if (abs > 0xFFF)       score -= 30;
        }

        return score;
    }
}
