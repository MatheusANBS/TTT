using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace TTT;

public sealed class ViewLocator : IDataTemplate
{
    public Control Build(object? data)
    {
        if (data is null)
            return new TextBlock { Text = "ViewModel nulo." };

        var viewModelType = data.GetType();
        var viewName = viewModelType.FullName?.Replace("ViewModel", "View", StringComparison.Ordinal);
        if (viewName is null)
            return new TextBlock { Text = $"ViewModel invalido: {viewModelType.Name}" };

        var viewType = Type.GetType(viewName);
        if (viewType is not null && Activator.CreateInstance(viewType) is Control control)
            return control;

        return new TextBlock { Text = $"View nao encontrada: {viewName}" };
    }

    public bool Match(object? data)
        => data?.GetType().Name.EndsWith("ViewModel", StringComparison.Ordinal) == true;
}
