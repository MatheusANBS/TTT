using System.Collections;
using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using TTT.ViewModels;

namespace TTT.Views;

public partial class PointerMapperView : UserControl
{
    public PointerMapperView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void PointerResultsGrid_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not PointerMapperViewModel vm)
            return;

        if (sender is not DataGrid grid)
            return;

        if (grid.SelectedItems is IList selected && selected.Count > 0)
            vm.AddSelectedPointersToListCommand.Execute(selected);
    }

    private void AddSelectedPointersButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not PointerMapperViewModel vm)
            return;

        var grid = this.FindControl<DataGrid>("PointerResultsGrid");
        if (grid?.SelectedItems is IList selected && selected.Count > 0)
            vm.AddSelectedPointersToListCommand.Execute(selected);
    }

    private async void SaveScanButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not PointerMapperViewModel vm)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        var storage = topLevel?.StorageProvider;
        if (storage is null)
            return;

        var suggestedName = $"scan_{DateTime.Now:yyyyMMdd_HHmmss}.pscan";
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Salvar scan de ponteiros",
            SuggestedFileName = suggestedName,
            DefaultExtension = "pscan",
            FileTypeChoices =
            [
                new FilePickerFileType("Pointer Scan") { Patterns = ["*.pscan"] },
            ]
        });

        if (file is null)
            return;

        await vm.SaveScanToPathAsync(file.Path.LocalPath);
    }

    private async void LoadScanButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not PointerMapperViewModel vm)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        var storage = topLevel?.StorageProvider;
        if (storage is null)
            return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Carregar scan de ponteiros",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Pointer Scan") { Patterns = ["*.pscan"] },
            ]
        });

        var selected = files.Count > 0 ? files[0] : null;
        if (selected is null)
            return;

        await vm.LoadScanFromPathAsync(selected.Path.LocalPath);
    }

    private async void CompareButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not PointerMapperViewModel vm)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        var storage = topLevel?.StorageProvider;
        if (storage is null)
            return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Selecionar scan base para comparar",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Pointer Scan") { Patterns = ["*.pscan"] },
            ]
        });

        var selected = files.Count > 0 ? files[0] : null;
        if (selected is null)
            return;

        await vm.CompareWithFileAsync(selected.Path.LocalPath);
    }
}
