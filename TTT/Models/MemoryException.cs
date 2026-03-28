// File: Models\MemoryException.cs

namespace TTT.Models;

/// <summary>
/// Application-level exception for memory manipulation failures.
/// Carries a friendly Portuguese message suitable for display in message boxes.
/// </summary>
public sealed class MemoryException : Exception
{
    /// <summary>
    /// Portuguese user-facing description of the error.
    /// (English technical details are in <see cref="Exception.Message"/>.)
    /// </summary>
    public string FriendlyMessage { get; }

    /// <summary>
    /// Initializes a new <see cref="MemoryException"/> with a technical message,
    /// a friendly Portuguese description, and an optional inner exception.
    /// </summary>
    /// <param name="technicalMessage">English technical detail (for logs).</param>
    /// <param name="friendlyMessage">Portuguese user-facing message.</param>
    /// <param name="inner">The underlying exception, if any.</param>
    public MemoryException(string technicalMessage, string friendlyMessage, Exception? inner = null)
        : base(technicalMessage, inner)
    {
        FriendlyMessage = friendlyMessage;
    }

    // ── Static factory helpers ────────────────────────────────────────────

    /// <summary>Creates an exception for protected or inaccessible processes.</summary>
    public static MemoryException ProcessProtected(int? pid = null, Exception? inner = null) =>
        new(
            $"OpenProcess failed for PID {pid}. Error: {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}",
            "Processo protegido ou sem permissão de leitura. Execute o scanner como Administrador.",
            inner
        );

    /// <summary>Creates an exception when no process is attached.</summary>
    public static MemoryException NotAttached() =>
        new(
            "Operation attempted without an attached process handle.",
            "Nenhum processo está conectado. Vá à aba Processo e clique em Conectar."
        );

    /// <summary>Creates an exception for an address that cannot be read.</summary>
    public static MemoryException InvalidAddress(long address, Exception? inner = null) =>
        new(
            $"ReadProcessMemory failed at 0x{address:X16}. Error: {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}",
            $"Endereço 0x{address:X} inválido ou sem permissão de leitura.",
            inner
        );

    /// <summary>Creates an exception for write failures.</summary>
    public static MemoryException WriteFailed(long address, Exception? inner = null) =>
        new(
            $"WriteProcessMemory failed at 0x{address:X16}. Error: {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}",
            $"Falha ao escrever no endereço 0x{address:X}. O processo pode estar protegido contra escrita.",
            inner
        );

    /// <summary>Creates an exception when a pointer chain cannot be resolved.</summary>
    public static MemoryException PointerResolutionFailed(string chain, Exception? inner = null) =>
        new(
            $"Pointer chain resolution failed: {chain}",
            $"Não foi possível resolver a cadeia de ponteiros: {chain}. O processo pode ter reiniciado.",
            inner
        );

    /// <summary>Creates an exception for config load/save failures.</summary>
    public static MemoryException ConfigFailed(string path, Exception inner) =>
        new(
            $"Config operation failed for '{path}': {inner.Message}",
            $"Erro ao acessar o arquivo de configuração '{path}'. Verifique as permissões.",
            inner
        );
}

