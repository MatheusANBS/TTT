using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TTT.Views;

public partial class ProcessView : UserControl
{
    public ProcessView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
