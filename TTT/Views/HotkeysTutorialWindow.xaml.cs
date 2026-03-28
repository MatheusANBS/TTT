using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace TTT.Views;

public partial class HotkeysTutorialWindow : Window
{
    public HotkeysTutorialWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
