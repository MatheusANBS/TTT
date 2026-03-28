// File: ViewModels/BaseViewModel.cs

using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using TTT.Models;
using TTT.Services;

namespace TTT.ViewModels;

/// <summary>
/// Base class for all ViewModels.
/// Provides snackbar messaging, UI dispatch, and error-safe async execution.
/// </summary>
public abstract class BaseViewModel : ObservableObject
{
    /// <summary>Injected by <see cref="MainViewModel"/> after construction.</summary>
    public Action<string>? SnackbarCallback { get; set; }

    // ── Helpers ────────────────────────────────────────────────────────────

    protected void ShowSnackbar(string message) =>
        SnackbarCallback?.Invoke(message);

    /// <summary>Runs <paramref name="action"/> on the UI dispatcher.</summary>
    protected static void OnUI(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }

    /// <summary>
    /// Executes <paramref name="work"/> safely:
    /// <list type="bullet">
    ///   <item><see cref="OperationCanceledException"/> → "Operação cancelada."</item>
    ///   <item><see cref="MemoryException"/> → <c>FriendlyMessage</c></item>
    ///   <item>Any other <see cref="Exception"/> → generic Portuguese message + log</item>
    /// </list>
    /// </summary>
    protected async Task SafeRunAsync(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
            ShowSnackbar("Operação cancelada.");
        }
        catch (MemoryException mex)
        {
            LogService.Instance.Warn(mex.ToString());
            ShowSnackbar(mex.FriendlyMessage);
        }
        catch (Exception ex)
        {
            LogService.Instance.Error(ex.ToString());
            ShowSnackbar($"Erro: {ex.Message}");
        }
    }
}
