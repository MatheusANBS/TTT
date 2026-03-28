// File: Utils\Constants.cs

namespace TTT.Utils;

/// <summary>
/// Application-wide constants: P/Invoke flags, buffer sizes, UI timings.
/// </summary>
public static class Constants
{
    // ── Process access rights ──────────────────────────────────────────────
    /// <summary>Required for ReadProcessMemory and WriteProcessMemory.</summary>
    public const uint PROCESS_VM_READ             = 0x0010;
    /// <summary>Required for WriteProcessMemory.</summary>
    public const uint PROCESS_VM_WRITE            = 0x0020;
    /// <summary>Required for VirtualAllocEx / VirtualProtectEx.</summary>
    public const uint PROCESS_VM_OPERATION        = 0x0008;
    /// <summary>Required for querying token and other info.</summary>
    public const uint PROCESS_QUERY_INFORMATION   = 0x0400;
    /// <summary>Combined mask used when attaching to a target process.</summary>
    public const uint PROCESS_ACCESS_MASK =
        PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION | PROCESS_QUERY_INFORMATION;

    // ── VirtualQueryEx / Memory state & type ─────────────────────────────
    /// <summary>Region is committed and accessible.</summary>
    public const uint MEM_COMMIT   = 0x1000;
    /// <summary>Region is a private mapping (heap / stack).</summary>
    public const uint MEM_PRIVATE  = 0x20000;
    /// <summary>Region is backed by a PE image (static modules).</summary>
    public const uint MEM_IMAGE    = 0x1000000;
    /// <summary>Region is a mapped file (not image).</summary>
    public const uint MEM_MAPPED   = 0x40000;

    // ── Page protection masks ─────────────────────────────────────────────
    public const uint PAGE_NOACCESS          = 0x01;
    public const uint PAGE_READONLY          = 0x02;
    public const uint PAGE_READWRITE         = 0x04;
    public const uint PAGE_WRITECOPY         = 0x08;
    public const uint PAGE_EXECUTE_READ      = 0x20;
    public const uint PAGE_EXECUTE_READWRITE = 0x40;
    public const uint PAGE_EXECUTE_WRITECOPY = 0x80;
    public const uint PAGE_GUARD             = 0x100;
    public const uint PAGE_NOCACHE           = 0x200;

    // ── Scanner tuning ────────────────────────────────────────────────────
    /// <summary>Size of each memory read chunk during scan (1 MB).</summary>
    public const int SCAN_CHUNK_SIZE = 1024 * 1024;

    /// <summary>Maximum number of scan results kept before warning user.</summary>
    public const int SCAN_MAX_RESULTS = 500_000;

    // ── Pointer mapper defaults ───────────────────────────────────────────
    /// <summary>Default cap on returned pointer chains.</summary>
    public const int POINTER_DEFAULT_MAX_RESULTS = 500;

    /// <summary>Maximum pointer offset considered valid (avoid false positives).</summary>
    public const long POINTER_MAX_OFFSET = 0x1000;

    // ── Address list refresh ──────────────────────────────────────────────
    /// <summary>Interval in ms for the live-refresh timer on the Address List tab.</summary>
    public const int ADDRESS_LIST_TIMER_MS = 200;

    // ── Logging ───────────────────────────────────────────────────────────
    /// <summary>Log file name written next to the executable.</summary>
    public const string LOG_FILE_NAME = "logs.txt";

    // ── Config ────────────────────────────────────────────────────────────
    /// <summary>App-local JSON settings sidecar (stores last config path, theme, etc.).</summary>
    public const string APP_SETTINGS_FILE = "appsettings.json";

    // ── UI ────────────────────────────────────────────────────────────────
    /// <summary>Hex prefix used for display strings.</summary>
    public const string HEX_PREFIX = "0x";
}

