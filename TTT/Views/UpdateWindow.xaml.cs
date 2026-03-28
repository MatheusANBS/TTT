using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TTT.Services;

namespace TTT.Views;

public partial class UpdateWindow : Window
{
    private readonly GitHubRelease _release;
    private bool _isDownloading;

    public UpdateWindow()
        : this(new GitHubRelease())
    {
    }

    public UpdateWindow(GitHubRelease release)
    {
        InitializeComponent();
        _release = release;

        var newVersion = release.TagName.TrimStart('v', 'V');
        VersionText.Text = $"v{UpdateService.CurrentVersionString} -> v{newVersion}";

        foreach (var asset in release.Assets)
        {
            if (!asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                continue;

            VersionText.Text += $"  •  {UpdateService.FormatSize(asset.Size)}";
            break;
        }

        var changelog = string.IsNullOrWhiteSpace(release.Body)
            ? "Nenhuma nota de versao disponivel."
            : release.Body
                .Replace("## ", string.Empty)
                .Replace("### ", string.Empty)
                .Replace("**", string.Empty)
                .Replace("- ", "• ")
                .Replace("* ", "• ");

        ChangelogText.Text = changelog;
    }

    private async void Update_Click(object? sender, RoutedEventArgs e)
    {
        if (_isDownloading)
            return;

        _isDownloading = true;
        ProgressPanel.IsVisible = true;
        UpdateButton.IsEnabled = false;
        UpdateButton.Content = "Baixando...";
        CancelButton.IsEnabled = false;

        try
        {
            var progress = new Progress<double>(percent => Dispatcher.UIThread.Post(() =>
            {
                DownloadProgress.Value = percent;
                ProgressPercentText.Text = $"{percent:F0}%";
            }));

            var installerPath = await UpdateService.DownloadInstallerAsync(_release, progress);
            if (installerPath is null)
            {
                ProgressStatusText.Text = "Falha no download. Tente novamente.";
                UpdateButton.Content = "Tentar novamente";
                UpdateButton.IsEnabled = true;
                CancelButton.IsEnabled = true;
                _isDownloading = false;
                return;
            }

            ProgressStatusText.Text = "Instalando...";
            ProgressPercentText.Text = "100%";
            DownloadProgress.Value = 100;

            await Task.Delay(500);
            UpdateService.InstallAndRestart(installerPath);
        }
        catch (Exception ex)
        {
            LogService.Instance.Warn($"Update install failed: {ex.Message}");
            ProgressStatusText.Text = $"Erro: {ex.Message}";
            UpdateButton.Content = "Tentar novamente";
            UpdateButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
            _isDownloading = false;
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
