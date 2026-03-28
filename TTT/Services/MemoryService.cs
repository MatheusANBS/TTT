// File: Services\MemoryService.cs

using System.Diagnostics;
using System.Runtime.InteropServices;
using TTT.Models;
using TTT.Utils;

namespace TTT.Services;

/// <summary>
/// Core P/Invoke wrapper for all Windows memory API calls.
/// Manages the process handle and exposes read/write/enumerate operations.
/// All public methods are thread-safe (handle is stored as IntPtr, write operations lock on <see cref="_handleLock"/>).
/// </summary>
public sealed class MemoryService : IDisposable
{
    // ── P/Invoke declarations ─────────────────────────────────────────────

    /// <summary>Opens a handle to an existing local process.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        int dwProcessId);

    /// <summary>Closes an open object handle.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>Reads data from an area of memory in a specified process.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        byte[] lpBuffer,
        nint nSize,
        out nint lpNumberOfBytesRead);

    /// <summary>Writes data to an area of memory in a specified process.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        byte[] lpBuffer,
        nint nSize,
        out nint lpNumberOfBytesWritten);

    /// <summary>Changes the protection on a region of committed pages in the virtual address space of a specified process.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualProtectEx(
        IntPtr hProcess,
        IntPtr lpAddress,
        nint dwSize,
        uint flNewProtect,
        out uint lpflOldProtect);

    private const uint PAGE_EXECUTE_READWRITE = 0x40;
    private const uint PAGE_READWRITE         = 0x04;

    /// <summary>Retrieves information about a range of pages within the virtual address space of a specified process.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint VirtualQueryEx(
        IntPtr hProcess,
        IntPtr lpAddress,
        out MEMORY_BASIC_INFORMATION64 lpBuffer,
        nint dwLength);

    /// <summary>
    /// Reports whether the target process is running under WOW64 (x86 process on x64 OS).
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process(
        IntPtr hProcess,
        [MarshalAs(UnmanagedType.Bool)] out bool wow64Process);

    /// <summary>Retrieves information about the current system (min/max application address).</summary>
    [DllImport("kernel32.dll", SetLastError = false)]
    private static extern void GetSystemInfo(out SYSTEM_INFO lpSystemInfo);

    // ── Native structs ────────────────────────────────────────────────────

    /// <summary>64-bit memory basic information returned by VirtualQueryEx on 64-bit processes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORY_BASIC_INFORMATION64
    {
        /// <summary>A pointer to the base address of the region of pages.</summary>
        public ulong BaseAddress;
        /// <summary>A pointer to the base address of a range of pages allocated by VirtualAllocEx.</summary>
        public ulong AllocationBase;
        /// <summary>The memory protection option when the region was initially allocated.</summary>
        public uint  AllocationProtect;
        /// <summary>Alignment padding.</summary>
        public uint  __alignment1;
        /// <summary>The size of the region beginning at the base address, in bytes.</summary>
        public ulong RegionSize;
        /// <summary>The state of the pages in the region (MEM_COMMIT, MEM_FREE, MEM_RESERVE).</summary>
        public uint  State;
        /// <summary>The access protection of the pages in the region.</summary>
        public uint  Protect;
        /// <summary>The type of pages in the region (MEM_IMAGE, MEM_MAPPED, MEM_PRIVATE).</summary>
        public uint  Type;
        /// <summary>Alignment padding.</summary>
        public uint  __alignment2;
    }

    /// <summary>System information including virtual address range for applications.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_INFO
    {
        public uint  dwOemId;
        public uint  dwPageSize;
        public ulong lpMinimumApplicationAddress;
        public ulong lpMaximumApplicationAddress;
        public ulong dwActiveProcessorMask;
        public uint  dwNumberOfProcessors;
        public uint  dwProcessorType;
        public uint  dwAllocationGranularity;
        public ushort wProcessorLevel;
        public ushort wProcessorRevision;
    }

    // ── State ─────────────────────────────────────────────────────────────

    private IntPtr _processHandle = IntPtr.Zero;
    private readonly object _handleLock = new();
    private readonly LogService _log = LogService.Instance;

    /// <summary>PID of the currently attached process, or 0 if not attached.</summary>
    public int AttachedPid { get; private set; }

    /// <summary>Display name of the attached process (e.g. "notepad.exe").</summary>
    public string AttachedProcessName { get; private set; } = string.Empty;

    /// <summary>Base address of the main module of the attached process.</summary>
    public long MainModuleBase { get; private set; }

    /// <summary>Pointer width used by the attached target process (4 for x86, 8 for x64).</summary>
    public int TargetPointerSize { get; private set; } = IntPtr.Size;

    /// <summary>Returns <see langword="true"/> if a process handle is currently open.</summary>
    public bool IsAttached => _processHandle != IntPtr.Zero;

    // ── Attach / Detach ───────────────────────────────────────────────────

    /// <summary>
    /// Opens a handle to a process with the full VM access mask required for reading/writing/scanning.
    /// </summary>
    /// <param name="pid">Target process identifier.</param>
    /// <exception cref="MemoryException">Thrown if OpenProcess fails (protected process or insufficient privileges).</exception>
    public void OpenHandle(int pid)
    {
        lock (_handleLock)
        {
            CloseCurrentHandle();
            var handle = OpenProcess(Constants.PROCESS_ACCESS_MASK, false, pid);
            if (handle == IntPtr.Zero)
                throw MemoryException.ProcessProtected(pid);

            _processHandle = handle;
            AttachedPid = pid;

            try
            {
                var proc = Process.GetProcessById(pid);
                AttachedProcessName = proc.ProcessName + ".exe";
                MainModuleBase = proc.MainModule?.BaseAddress.ToInt64() ?? 0L;
                TargetPointerSize = DetectTargetPointerSize(handle);
            }
            catch (Exception ex)
            {
                _log.Warn($"Could not query main module for PID {pid}: {ex.Message}");
                AttachedProcessName = $"PID:{pid}";
                MainModuleBase = 0;
                TargetPointerSize = IntPtr.Size;
            }

            _log.Info($"Attached to {AttachedProcessName} (PID {pid}), base=0x{MainModuleBase:X}, ptrSize={TargetPointerSize}");
        }
    }

    /// <summary>Closes the current process handle and resets state.</summary>
    public void CloseCurrentHandle()
    {
        lock (_handleLock)
        {
            if (_processHandle != IntPtr.Zero)
            {
                CloseHandle(_processHandle);
                _log.Info($"Detached from {AttachedProcessName} (PID {AttachedPid})");
                _processHandle = IntPtr.Zero;
                AttachedPid = 0;
                AttachedProcessName = string.Empty;
                MainModuleBase = 0;
                TargetPointerSize = IntPtr.Size;
            }
        }
    }

    // ── Read / Write ──────────────────────────────────────────────────────

    /// <summary>
    /// Reads <paramref name="size"/> bytes from the target process at <paramref name="address"/>.
    /// </summary>
    /// <param name="address">Virtual address in the target process.</param>
    /// <param name="size">Number of bytes to read.</param>
    /// <returns>The bytes read, or <see langword="null"/> on failure.</returns>
    /// <exception cref="MemoryException">Thrown if not attached.</exception>
    public byte[]? ReadBytes(long address, int size)
    {
        EnsureAttached();
        var buffer = new byte[size];
        bool ok;
        lock (_handleLock)
        {
            ok = ReadProcessMemory(_processHandle, (IntPtr)address, buffer, size, out _);
        }
        return ok ? buffer : null;
    }

    /// <summary>
    /// Reads into an existing buffer — avoids allocation.
    /// Returns the number of bytes actually read, or 0 on failure.
    /// </summary>
    public int ReadBytesInto(long address, byte[] buffer, int size)
    {
        EnsureAttached();
        bool ok = ReadProcessMemory(_processHandle, (IntPtr)address, buffer, size, out var bytesRead);
        return ok ? (int)bytesRead : 0;
    }

    /// <summary>
    /// Fast-path read for scanning — no lock, no allocation.
    /// Only safe when called from a single scanner thread.
    /// </summary>
    internal int ReadBytesUnsafe(long address, byte[] buffer, int size)
    {
        bool ok = ReadProcessMemory(_processHandle, (IntPtr)address, buffer, size, out var bytesRead);
        return ok ? (int)bytesRead : 0;
    }

    /// <summary>
    /// Reads a full pointer value from the target process (4 bytes for x86, 8 bytes for x64).
    /// Returns 0 on read failure.
    /// </summary>
    public long ReadPointer(long address)
    {
        int pointerSize = TargetPointerSize == 4 ? 4 : 8;
        var bytes = ReadBytes(address, pointerSize);
        if (bytes is null || bytes.Length < pointerSize)
            return 0L;

        return pointerSize == 4
            ? (long)BitConverter.ToUInt32(bytes, 0)
            : BitConverter.ToInt64(bytes, 0);
    }

    /// <summary>
    /// Writes <paramref name="data"/> to the target process at <paramref name="address"/>.
    /// If the page is read-only or execute-read, temporarily elevates protection via VirtualProtectEx
    /// so the write succeeds, then restores the original protection.
    /// </summary>
    /// <param name="address">Virtual address in the target process.</param>
    /// <param name="data">Bytes to write.</param>
    /// <exception cref="MemoryException">Thrown if not attached, address is null, or write ultimately fails.</exception>
    public void WriteBytes(long address, byte[] data)
    {
        EnsureAttached();

        if (address == 0)
            throw new MemoryException(
                "WriteBytes called with address 0 (null pointer).",
                "Endereço de destino é 0x0 — a cadeia de ponteiros não foi resolvida ainda.");

        bool ok;
        lock (_handleLock)
        {
            ok = WriteProcessMemory(_processHandle, (IntPtr)address, data, data.Length, out _);

            if (!ok)
            {
                // Best-effort protection bypass: make the page writable, retry, then restore
                if (VirtualProtectEx(_processHandle, (IntPtr)address, data.Length, PAGE_EXECUTE_READWRITE, out uint oldProtect))
                {
                    ok = WriteProcessMemory(_processHandle, (IntPtr)address, data, data.Length, out _);
                    VirtualProtectEx(_processHandle, (IntPtr)address, data.Length, oldProtect, out _);
                }
            }
        }

        if (!ok)
            throw MemoryException.WriteFailed(address);
    }

    // ── Region enumeration ─────────────────────────────────────────────────

    /// <summary>
    /// Enumerates all readable and committed memory regions in the target process.
    /// Filters out guard pages, no-access pages, and regions smaller than the requested value size.
    /// </summary>
    /// <param name="onlyStatic">When <see langword="true"/>, yields only MEM_IMAGE regions (static modules).</param>
    /// <returns>Sequence of (BaseAddress, RegionSize) tuples.</returns>
    public IEnumerable<(long Base, long Size)> EnumerateReadableRegions(bool onlyStatic = false)
    {
        EnsureAttached();
        GetSystemInfo(out var sysInfo);

        var address = (long)sysInfo.lpMinimumApplicationAddress;
        var maxAddr = (long)sysInfo.lpMaximumApplicationAddress;
        var mbiSize = (nint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION64>();

        while (address < maxAddr)
        {
            MEMORY_BASIC_INFORMATION64 mbi;
            nint result;
            lock (_handleLock)
            {
                result = VirtualQueryEx(_processHandle, (IntPtr)address, out mbi, mbiSize);
            }
            if (result == 0) break;

            var regionBase = (long)mbi.BaseAddress;
            var regionSize = (long)mbi.RegionSize;

            if (regionSize <= 0)
            {
                address = regionBase + 0x1000;
                continue;
            }

            bool isCommitted = (mbi.State & Constants.MEM_COMMIT) != 0;
            bool isReadable  = (mbi.Protect & (Constants.PAGE_NOACCESS | Constants.PAGE_GUARD)) == 0;
            bool isImage     = (mbi.Type & Constants.MEM_IMAGE) != 0;

            if (isCommitted && isReadable && (!onlyStatic || isImage))
                yield return (regionBase, regionSize);

            address = regionBase + regionSize;
            if (address <= regionBase) break; // overflow guard
        }
    }

    /// <summary>
    /// Retrieves all memory regions classified as image (MEM_IMAGE) — i.e. loaded PE modules.
    /// </summary>
    public IEnumerable<(long Base, long Size)> EnumerateImageRegions() =>
        EnumerateReadableRegions(onlyStatic: true);

    /// <summary>
    /// Returns the <c>MBI.Type</c> for the page that contains <paramref name="address"/>.
    /// Possible values: <see cref="Constants.MEM_IMAGE"/> (0x1000000),
    /// <see cref="Constants.MEM_MAPPED"/> (0x40000), <see cref="Constants.MEM_PRIVATE"/> (0x20000).
    /// Returns 0 if the query fails (unmapped / unallocated address).
    /// </summary>
    public uint GetRegionType(long address)
    {
        if (!IsAttached) return 0;
        MEMORY_BASIC_INFORMATION64 mbi;
        var mbiSize = (nint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION64>();
        nint r;
        lock (_handleLock)
            r = VirtualQueryEx(_processHandle, (IntPtr)address, out mbi, mbiSize);
        return r == 0 ? 0u : mbi.Type;
    }

    // ── Pointer chain resolution ──────────────────────────────────────────

    /// <summary>
    /// Resolves a pointer chain: reads the pointer at <paramref name="baseAddress"/>,
    /// then adds each offset and reads the next pointer, returning the final effective address.
    /// </summary>
    /// <param name="baseAddress">Starting absolute address (e.g. a static global pointer).</param>
    /// <param name="offsets">Ordered list of offsets to apply after each dereference.</param>
    /// <returns>The resolved final address, or 0 if any step fails.</returns>
    public long ResolvePointerChain(long baseAddress, IEnumerable<long> offsets)
    {
        var current = baseAddress;
        foreach (var offset in offsets)
        {
            var pointer = ReadPointer(current);
            if (pointer == 0) return 0;
            current = pointer + offset;
        }
        return current;
    }

    /// <summary>
    /// Gets the base address of a module by name within the attached process.
    /// </summary>
    /// <param name="moduleName">Module name to search for (e.g. "mono.dll").</param>
    /// <returns>Base address or 0 if not found.</returns>
    public long GetModuleBase(string moduleName)
    {
        EnsureAttached();
        try
        {
            var proc = Process.GetProcessById(AttachedPid);
            foreach (ProcessModule mod in proc.Modules)
            {
                if (mod.ModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase))
                    return mod.BaseAddress.ToInt64();
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"GetModuleBase({moduleName}): {ex.Message}");
        }
        return 0;
    }

    /// <summary>
    /// Returns all loaded modules for the attached process as (Name, BaseAddress, Size) tuples.
    /// </summary>
    public IEnumerable<(string Name, long Base, int Size)> GetModules()
    {
        EnsureAttached();
        Process proc;
        try { proc = Process.GetProcessById(AttachedPid); }
        catch { yield break; }

        foreach (ProcessModule mod in proc.Modules)
            yield return (mod.ModuleName, mod.BaseAddress.ToInt64(), mod.ModuleMemorySize);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>Throws <see cref="MemoryException.NotAttached"/> if no handle is open.</summary>
    private void EnsureAttached()
    {
        if (!IsAttached) throw MemoryException.NotAttached();
    }

    private static int DetectTargetPointerSize(IntPtr processHandle)
    {
        // Fallback-safe detection: if anything fails, keep host pointer width.
        if (!Environment.Is64BitOperatingSystem)
            return 4;

        if (!Environment.Is64BitProcess)
            return 4;

        if (!IsWow64Process(processHandle, out bool isWow64))
            return IntPtr.Size;

        return isWow64 ? 4 : 8;
    }

    /// <inheritdoc/>
    public void Dispose() => CloseCurrentHandle();
}
