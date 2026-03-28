// File: Services\LogService.cs

using System.IO;
using TTT.Utils;

namespace TTT.Services;

/// <summary>Severity levels for log messages.</summary>
public enum LogLevel { Info, Warning, Error, Debug }

/// <summary>
/// Thread-safe singleton logger.
/// Writes to <see cref="Constants.LOG_FILE_NAME"/> and fires <see cref="OnLogEntry"/>
/// so the UI can append entries to its RichTextBox log panel.
/// </summary>
public sealed class LogService
{
    private static readonly Lazy<LogService> _instance = new(() => new LogService());
    /// <summary>Gets the singleton instance.</summary>
    public static LogService Instance => _instance.Value;

    private readonly string _logPath = Path.Combine(AppContext.BaseDirectory, Constants.LOG_FILE_NAME);
    private readonly object _fileLock = new();

    /// <summary>
    /// Raised on the calling thread whenever a new log entry is appended.
    /// Subscribe from <c>MainForm</c> to update the log RichTextBox.
    /// Parameters: (message, level).
    /// </summary>
    public event Action<string, LogLevel>? OnLogEntry;

    private LogService() { }

    /// <summary>
    /// Writes a log entry to the file and fires <see cref="OnLogEntry"/>.
    /// </summary>
    /// <param name="message">The message text.</param>
    /// <param name="level">Severity level (default: <see cref="LogLevel.Info"/>).</param>
    public void Log(string message, LogLevel level = LogLevel.Info)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level,7}] {message}";

        // Fire UI event first (non-blocking)
        try { OnLogEntry?.Invoke(line, level); }
        catch { /* never let UI errors kill background scan threads */ }

        // Write to file (thread-safe)
        lock (_fileLock)
        {
            try
            {
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
            catch
            {
                // Silently ignore file I/O failures (could be read-only directory)
            }
        }
    }

    /// <summary>Convenience overload for <see cref="LogLevel.Info"/>.</summary>
    public void Info(string message)    => Log(message, LogLevel.Info);

    /// <summary>Convenience overload for <see cref="LogLevel.Warning"/>.</summary>
    public void Warn(string message)    => Log(message, LogLevel.Warning);

    /// <summary>Convenience overload for <see cref="LogLevel.Error"/>.</summary>
    public void Error(string message)   => Log(message, LogLevel.Error);

    /// <summary>Convenience overload for <see cref="LogLevel.Debug"/>.</summary>
    public void Debug(string message)   => Log(message, LogLevel.Debug);

    /// <summary>Clears the log file on disk.</summary>
    public void ClearFile()
    {
        lock (_fileLock)
        {
            try { File.WriteAllText(_logPath, string.Empty); }
            catch { }
        }
    }
}

