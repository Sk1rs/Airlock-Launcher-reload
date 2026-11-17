using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Serilog;
using SS14.Launcher;

namespace Airlock.Features;

internal static class WindowsReleases
{
    private const string Repository = "Sk1rs/Airlock-Launcher-reload";
    private const string TagPrefix = "windows-v";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    public static Version InstalledVersion { get; } = new(Version.Parse(typeof(WindowsReleases).Assembly
        .GetCustomAttribute<AssemblyFileVersionAttribute>()!.Version).ToString(3));

    public static void Attach(Window window)
    {
        if (OperatingSystem.IsWindows())
            window.Opened += CheckOnOpen;
    }

    private static async void CheckOnOpen(object? sender, EventArgs args)
    {
        if (sender is not Window window)
            return;

        window.Opened -= CheckOnOpen;
        using var cancellation = new CancellationTokenSource();
        void CancelOnClose(object? sender, EventArgs args) => cancellation.Cancel();
        window.Closed += CancelOnClose;
        try
        {
            var latest = await FindUpdateAsync(InstalledVersion, cancellation.Token);
            if (latest is not null && window.IsVisible && !cancellation.IsCancellationRequested)
                await ShowUpdateAsync(window, latest);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException)
        {
            Log.Debug(error, "Could not check Windows launcher releases");
        }
        finally
        {
            window.Closed -= CancelOnClose;
        }
    }

    private static async Task<WindowsUpdate?> FindUpdateAsync(Version installed, CancellationToken cancellation)
    {
        WindowsUpdate? newest = null;
        for (var page = 1; ; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{Repository}/releases?per_page=100&page={page}");
            request.Headers.UserAgent.ParseAdd($"AirlockLauncher/{installed}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await Http.SendAsync(request, cancellation);
            if (!response.IsSuccessStatusCode)
                return newest;

            var releases = await response.Content.ReadFromJsonAsync<Release[]>(cancellationToken: cancellation) ?? [];
            foreach (var release in releases)
            {
                if (release.Draft || release.Prerelease || release.Tag is not { } tag ||
                    !tag.StartsWith(TagPrefix, StringComparison.Ordinal) ||
                    !Version.TryParse(tag[TagPrefix.Length..], out var version) ||
                    version.Build < 0 || tag != TagPrefix + version.ToString(3) ||
                    version <= (newest?.Version ?? installed))
                    continue;

                var assetName = $"AirlockLauncher-Setup-{version}-win-x64.exe";
                if (release.Assets?.Any(asset => asset.Name == assetName) == true)
                    newest = new(version, new Uri($"https://github.com/{Repository}/releases/download/{tag}/{assetName}"));
            }

            if (releases.Length < 100)
                return newest;
        }
    }

    private static async Task ShowUpdateAsync(Window owner, WindowsUpdate update)
    {
        var russian = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";
        var download = new Button { Content = russian ? "Скачать обновление" : "Download update" };
        var later = new Button { Content = russian ? "Позже" : "Later", IsCancel = true };
        var dialog = new Window
        {
            Title = russian ? "Обновление Airlock Launcher" : "Airlock Launcher update",
            Width = 390,
            CanResize = false,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = russian ? $"Доступна версия {update.Version} для Windows."
                            : $"Version {update.Version} is available for Windows.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { download, later } }
                }
            }
        };
        later.Click += (_, _) => dialog.Close(false);
        download.Click += (_, _) => dialog.Close(true);
        if (await dialog.ShowDialog<bool>(owner))
            Helpers.OpenUri(update.DownloadUrl);
    }

    private sealed record Release(
        [property: JsonPropertyName("tag_name")] string? Tag,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("assets")] Asset[]? Assets);

    private sealed record Asset([property: JsonPropertyName("name")] string? Name);
    private sealed record WindowsUpdate(Version Version, Uri DownloadUrl);
}
