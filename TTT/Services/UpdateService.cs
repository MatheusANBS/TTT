using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace TTT.Services;

public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    [JsonPropertyName("assets")]
    public GitHubAsset[] Assets { get; set; } = [];

    public Version? ParsedVersion
    {
        get
        {
            var tag = TagName.TrimStart('v', 'V');
            return Version.TryParse(tag, out var version) ? version : null;
        }
    }
}

public sealed class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public static class UpdateService
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/MatheusANBS/TTT/releases/latest";
    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TTT-Updater");
        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
        return client;
    }

    public static Version CurrentVersion
    {
        get
        {
            var version = Assembly.GetEntryAssembly()?.GetName().Version;
            return version is not null
                ? new Version(version.Major, version.Minor, Math.Max(version.Build, 0))
                : new Version(1, 0, 0);
        }
    }

    public static string CurrentVersionString =>
        $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    public static async Task<GitHubRelease?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var release = await Http.GetFromJsonAsync<GitHubRelease>(LatestReleaseApiUrl, cancellationToken);
            if (release?.ParsedVersion is null)
                return null;

            var remoteVersion = new Version(
                release.ParsedVersion.Major,
                release.ParsedVersion.Minor,
                Math.Max(release.ParsedVersion.Build, 0));

            return remoteVersion > CurrentVersion ? release : null;
        }
        catch (Exception ex)
        {
            LogService.Instance.Warn($"Update check failed: {ex.Message}");
            return null;
        }
    }

    public static async Task<string?> DownloadInstallerAsync(
        GitHubRelease release,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var asset = release.Assets.FirstOrDefault(candidate =>
                            candidate.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                            candidate.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                        ?? release.Assets.FirstOrDefault(candidate =>
                            candidate.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

            if (asset is null)
            {
                LogService.Instance.Warn("No installer asset found in release.");
                return null;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "TTT", "Updates");
            Directory.CreateDirectory(tempDir);

            var installerPath = Path.Combine(tempDir, asset.Name);
            if (File.Exists(installerPath))
                File.Delete(installerPath);

            using var response = await Http.GetAsync(
                asset.BrowserDownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? asset.Size;
            var downloadedBytes = 0L;

            await using var remoteStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var localStream = new FileStream(
                installerPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                8192,
                true);

            var buffer = new byte[8192];
            int bytesRead;
            while ((bytesRead = await remoteStream.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
            {
                await localStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                downloadedBytes += bytesRead;

                if (totalBytes > 0)
                    progress?.Report(downloadedBytes * 100.0 / totalBytes);
            }

            return installerPath;
        }
        catch (Exception ex)
        {
            LogService.Instance.Warn($"Update download failed: {ex.Message}");
            return null;
        }
    }

    public static void InstallAndRestart(string installerPath)
    {
        var currentExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(currentExe))
            throw new InvalidOperationException("Unable to resolve current executable path for restart.");

        const string installerArgs = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS";
        var cmdArgs = $"/C \"\"{installerPath}\" {installerArgs} & start \"\" \"{currentExe}\"\"";

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = cmdArgs,
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        });

        Dispatcher.UIThread.InvokeAsync(() =>
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown());
    }

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";

        if (bytes < 1_048_576)
            return $"{bytes / 1024.0:F1} KB";

        return $"{bytes / 1_048_576.0:F1} MB";
    }
}
