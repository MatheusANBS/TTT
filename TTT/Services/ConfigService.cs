// File: Services\ConfigService.cs

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TTT.Models;
using TTT.Utils;

namespace TTT.Services;

// ── Data transfer objects ─────────────────────────────────────────────────

/// <summary>Serialized representation of a single Address List entry.</summary>
public sealed class MemoryEntryDto
{
    public string Description { get; set; } = "No description";
    public long Address { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ScanValueType Type { get; set; } = ScanValueType.Byte4;
    public bool IsFrozen { get; set; }
    public byte[]? FrozenValue { get; set; }
    public List<long> Offsets { get; set; } = [];
    public string GroupName { get; set; } = "Geral";
}

/// <summary>Top-level config data written to / read from a .json file.</summary>
public sealed class ConfigData
{
    public string ProcessName { get; set; } = string.Empty;
    public long ModuleBase { get; set; }
    public List<MemoryEntryDto> AddressList { get; set; } = [];
    public List<SavedPointer> PointerChains { get; set; } = [];
    public List<GroupHotkeyDto> GroupHotkeys { get; set; } = [];
}

public sealed class GroupHotkeyDto
{
    public string GroupName { get; set; } = string.Empty;
    public string HotkeyText { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string ActionValue { get; set; } = "1";
    public bool IsEnabled { get; set; } = true;
}

/// <summary>Sidecar settings (last path, theme, etc.) stored in appsettings.json next to the .exe.</summary>
public sealed class AppSettings
{
    public string LastConfigPath { get; set; } = string.Empty;
    public bool DarkMode { get; set; }
    public int PointerMaxDepth { get; set; } = 4;
    public int PointerMaxResults { get; set; } = 500;
    public bool PointerPrioritizeStatic { get; set; } = true;
}

/// <summary>
/// Handles serialization/deserialization of the scanner configuration to/from JSON.
/// Provides auto-save/load of the last used config path via a sidecar <c>appsettings.json</c>.
/// </summary>
public sealed class ConfigService
{
    private readonly LogService _log = LogService.Instance;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly string _settingsPath =
        Path.Combine(AppContext.BaseDirectory, Constants.APP_SETTINGS_FILE);

    // ── App settings ──────────────────────────────────────────────────────

    /// <summary>Loads the sidecar app settings, returning defaults if the file is missing or corrupt.</summary>
    public AppSettings LoadAppSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return new AppSettings();
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            _log.Warn($"LoadAppSettings failed: {ex.Message}");
            return new AppSettings();
        }
    }

    /// <summary>Persists app settings to the sidecar file.</summary>
    public void SaveAppSettings(AppSettings settings)
    {
        try
        {
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, _jsonOptions));
        }
        catch (Exception ex)
        {
            _log.Warn($"SaveAppSettings failed: {ex.Message}");
        }
    }

    // ── Config file ───────────────────────────────────────────────────────

    /// <summary>
    /// Serializes <paramref name="data"/> to a JSON file at <paramref name="path"/>.
    /// </summary>
    /// <param name="path">Full path to the target .json file.</param>
    /// <param name="data">Config data to serialize.</param>
    /// <exception cref="MemoryException">Thrown on I/O failure.</exception>
    public void SaveConfig(string path, ConfigData data)
    {
        try
        {
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            File.WriteAllText(path, json);
            _log.Info($"Config saved to '{path}' ({data.AddressList.Count} entries, {data.PointerChains.Count} chains).");
        }
        catch (Exception ex) when (ex is not MemoryException)
        {
            throw MemoryException.ConfigFailed(path, ex);
        }
    }

    /// <summary>
    /// Deserializes a config file from <paramref name="path"/>.
    /// </summary>
    /// <param name="path">Full path to the .json file.</param>
    /// <returns>The deserialized <see cref="ConfigData"/>.</returns>
    /// <exception cref="MemoryException">Thrown on I/O or parse failure.</exception>
    public ConfigData LoadConfig(string path)
    {
        try
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Config file not found: {path}");

            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<ConfigData>(json, _jsonOptions)
                       ?? throw new JsonException("Null deserialization result.");

            _log.Info($"Config loaded from '{path}' ({data.AddressList.Count} entries, {data.PointerChains.Count} chains).");
            return data;
        }
        catch (Exception ex) when (ex is not MemoryException)
        {
            throw MemoryException.ConfigFailed(path, ex);
        }
    }

    // ── Conversion helpers ────────────────────────────────────────────────

    /// <summary>Converts a <see cref="MemoryEntry"/> to its DTO for serialization.</summary>
    public static MemoryEntryDto ToDto(MemoryEntry entry) =>
        new()
        {
            Description = entry.Description,
            Address     = entry.Address,
            Type        = entry.Type,
            IsFrozen    = entry.IsFrozen,
            FrozenValue = entry.FrozenValue,
            Offsets     = [.. entry.Offsets],
            GroupName   = entry.GroupName
        };

    /// <summary>Converts a DTO back to a live <see cref="MemoryEntry"/>.</summary>
    public static MemoryEntry FromDto(MemoryEntryDto dto) =>
        new()
        {
            Description = dto.Description,
            Address     = dto.Address,
            Type        = dto.Type,
            IsFrozen    = dto.IsFrozen,
            FrozenValue = dto.FrozenValue,
            Offsets     = [.. dto.Offsets],
            GroupName   = dto.GroupName
        };

    /// <summary>
    /// Exports the Address List in a simplified Cheat Table text format compatible with basic
    /// external tooling. Format: one entry per line —
    /// <c>[Description] Address=0xXXXX Type=Byte4 Frozen=False Offsets=0x10,0x8</c>
    /// </summary>
    public static string ExportAsCheatTable(IEnumerable<MemoryEntry> entries)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<CheatTable>");
        sb.AppendLine("  <CheatEntries>");
        int id = 1;
        foreach (var e in entries)
        {
            sb.AppendLine($"    <CheatEntry id=\"{id++}\">");
            sb.AppendLine($"      <Description>{System.Security.SecurityElement.Escape(e.Description)}</Description>");
            sb.AppendLine($"      <VariableType>{e.Type}</VariableType>");
            if (e.IsPointerChain)
            {
                sb.AppendLine($"      <Address>0x{e.Address:X}</Address>");
                sb.AppendLine("      <Offsets>");
                foreach (var off in Enumerable.Reverse(e.Offsets))
                    sb.AppendLine($"        <Offset>0x{off:X}</Offset>");
                sb.AppendLine("      </Offsets>");
            }
            else
            {
                sb.AppendLine($"      <Address>0x{e.Address:X}</Address>");
            }
            sb.AppendLine($"      <Frozen>{e.IsFrozen.ToString().ToLower()}</Frozen>");
            sb.AppendLine("    </CheatEntry>");
        }
        sb.AppendLine("  </CheatEntries>");
        sb.AppendLine("</CheatTable>");
        return sb.ToString();
    }
}
