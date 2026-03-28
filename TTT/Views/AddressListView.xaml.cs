using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System;
using TTT.Models;
using TTT.ViewModels;

namespace TTT.Views;

public partial class AddressListView : UserControl
{
    private DataGrid? _activeGrid;

    public AddressListView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void AddressGrid_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not AddressListViewModel vm)
            return;

        var entry = FindRowEntryFromSource(e.Source);
        if (entry is null && sender is DataGrid { SelectedItem: MemoryEntry selectedEntry })
            entry = selectedEntry;

        if (entry is not null)
            vm.EditEntryValueCommand.Execute(entry);
    }

    private void AddressGrid_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _activeGrid = sender as DataGrid;

        var rowEntry = FindRowEntryFromSource(e.Source);

        if (rowEntry is not null && sender is DataGrid grid)
        {
            var pointer = e.GetCurrentPoint(grid);
            if (pointer.Properties.IsRightButtonPressed && !grid.SelectedItems.Contains(rowEntry))
            {
                grid.SelectedItems.Clear();
                grid.SelectedItems.Add(rowEntry);
                grid.SelectedItem = rowEntry;
            }
        }
    }

    private void AddressGrid_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid grid || DataContext is not AddressListViewModel vm)
            return;

        // Ignore events from grids that are no longer in the visual tree (stale after rebuild).
        if (grid.Parent is null)
            return;

        _activeGrid = grid;
        if (grid.SelectedItem is MemoryEntry selected)
            vm.SelectedEntry = selected;
    }

    private void AddressGrid_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || DataContext is not AddressListViewModel vm)
            return;

        if (sender is DataGrid grid)
            vm.DeleteSelectedCommand.Execute(grid.SelectedItems);
        else
            vm.DeleteSelectedCommand.Execute(_activeGrid?.SelectedItems ?? Array.Empty<object>());

        e.Handled = true;
    }

    private void EditSelectedValuesMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddressListViewModel vm)
            return;

        vm.EditSelectedValuesCommand.Execute(GetMenuGridSelectedItems(sender));
    }

    private void EditSelectedDescriptionsMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddressListViewModel vm)
            return;

        vm.EditSelectedDescriptionsCommand.Execute(GetMenuGridSelectedItems(sender));
    }

    private void EditSelectedGroupsMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddressListViewModel vm)
            return;

        vm.EditSelectedGroupsCommand.Execute(GetMenuGridSelectedItems(sender));
    }

    private void ToggleFreezeMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddressListViewModel vm)
            return;

        vm.ToggleFreezeCommand.Execute(GetMenuGridSelectedItems(sender));
    }

    private void DeleteSelectedMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddressListViewModel vm)
            return;

        vm.DeleteSelectedCommand.Execute(GetMenuGridSelectedItems(sender));
    }

    private void FindPointersMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddressListViewModel vm)
            return;

        vm.FindPointersCommand.Execute(null);
    }

    private void ExportCheatTableMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddressListViewModel vm)
            return;

        vm.ExportCheatTableCommand.Execute(null);
    }

    private object GetMenuGridSelectedItems(object? sender)
    {
        // Walk the logical tree to find the ContextMenu, then its PlacementTarget.
        if (sender is MenuItem menuItem)
        {
            var parent = menuItem.Parent;
            while (parent is MenuItem parentItem)
                parent = parentItem.Parent;

            if (parent is ContextMenu ctx && ctx.PlacementTarget is DataGrid grid)
                return grid.SelectedItems;
        }

        // Fallback: use the last-active grid if the menu tree lookup failed.
        if (_activeGrid?.Parent is not null)
            return _activeGrid.SelectedItems;

        return Array.Empty<object>();
    }

    private static MemoryEntry? FindRowEntryFromSource(object? source)
    {
        var element = source as StyledElement;

        while (element is not null)
        {
            if (element is DataGridRow { DataContext: MemoryEntry entry })
                return entry;

            element = element.Parent as StyledElement;
        }

        return null;
    }
}
