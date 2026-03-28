using System.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using TTT.ViewModels;

namespace TTT.Views;

public partial class ScannerView : UserControl
{
    public ScannerView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void ResultsGrid_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not ScannerViewModel vm)
            return;

        if (sender is not DataGrid grid)
            return;

        if (grid.SelectedItems is IList selected && selected.Count > 0)
            vm.AddResultToAddressListCommand.Execute(selected);
    }

    private void AddSelectedResultsButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ScannerViewModel vm)
            return;

        var grid = this.FindControl<DataGrid>("ResultsGrid");
        if (grid?.SelectedItems is IList selected && selected.Count > 0)
            vm.AddResultToAddressListCommand.Execute(selected);
    }
}
