using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Serilog;
using SharpZstd.Interop;
using Splat;
using SS14.Launcher;
using SS14.Launcher.Models;
using SS14.Launcher.Models.ContentManagement;
using SS14.Launcher.Utility;
using SS14.Launcher.ViewModels;
using SS14.Launcher.ViewModels.MainWindowTabs;
using SS14.Launcher.Views;
using SS14.Launcher.Views.MainWindowTabs;

namespace Airlock.Features;

public static class LauncherFeatures
{
    public static void AttachReleaseCheck(Window window) => WindowsReleases.Attach(window);
    public static string DisplayReleaseVersion() => $"v{WindowsReleases.InstalledVersion}";

    private static readonly ConditionalWeakTable<ConnectingViewModel, ServerAttempt> Attempts = new();
    private static readonly FieldInfo ServerWindowField = GetPrivateField(typeof(ServerEntryViewModel), "_windowVm");
    private static readonly FieldInfo ConnectorField = GetPrivateField(typeof(ConnectingViewModel), "_connector");
    private static readonly FieldInfo RecoveryWindowField = GetPrivateField(typeof(ConnectingViewModel), "_windowVm");
    private static readonly MethodInfo GetServerInfo = typeof(Connector).GetMethod("GetServerInfoAsync",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static bool _operationInProgress;

    private sealed record ServerAttempt(string Address);
    public static bool OperationInProgress() => _operationInProgress;
    public static void RememberServer(ConnectingViewModel vm, string address) => Attempts.Add(vm, new(address));

    public static void AddCookingGuide(GuidesTabViewModel guides)
    {
        const string url = "https://station14.ru/wiki/%D0%93%D0%BE%D1%82%D0%BE%D0%B2%D0%BA%D0%B0";
        const int rolesSectionIndex = 1;
        var cookingGuide = new GuideLink("guides-role-cooking", "RU", url);
        var roleLinks = (List<GuideLinkViewModel>)guides.Sections[rolesSectionIndex].Links;
        roleLinks.Add(new GuideLinkViewModel(Text("Готовка", "Cooking"), cookingGuide));
    }

    public static void AttachServerCard(ServerEntryView card)
    {
        if (card.FindNameScope()?.Find<Expander>("Expando")?.Header is not Grid header ||
            header.Children.OfType<Border>().FirstOrDefault(border =>
                Grid.GetColumn(border) == 4 && border.Classes.Contains("GridCell")) is not { Child: Button connect } cell)
            return;

        var preload = new Button
        {
            Width = 28,
            MinWidth = 0,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 4, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            Content = new PathIcon
            {
                Width = 14,
                Height = 14,
                Data = Geometry.Parse("M6,1 H8 V7 H11 L7,11 L3,7 H6 Z M1,11 H3 V13 H11 V11 H13 V15 H1 Z")
            }
        };
        preload.Bind(Visual.IsVisibleProperty, new Binding(nameof(ServerEntryViewModel.IsOnline)));
        void UpdateDownloadLabel()
        {
            var label = Text("Скачать заранее", "Pre-download");
            ToolTip.SetTip(preload, label);
            AutomationProperties.SetName(preload, label);
        }
        card.DataContextChanged += (_, _) => UpdateDownloadLabel();
        UpdateDownloadLabel();
        preload.Click += async (_, _) =>
        {
            if (card.DataContext is ServerEntryViewModel { IsOnline: true } server &&
                ServerWindowField.GetValue(server) is MainWindowViewModel { ConnectingVM: null } window)
                await PrepareServerAsync(window, server.ServerAddress, repair: false);
        };
        cell.Child = null;
        var actions = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(connect, 1);
        actions.Children.Add(preload);
        actions.Children.Add(connect);
        cell.Child = actions;
    }

    public static void AttachRecovery(ConnectingOverlay overlay)
    {
        var dismiss = overlay.GetLogicalDescendants().OfType<Button>().FirstOrDefault(button =>
            button.Content is string label && label == "Ok");
        if (dismiss?.GetLogicalParent() is not Panel parent)
            return;

        var retry = new Button();
        var report = new Button();
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        retry.Margin = report.Margin = new Thickness(4, 0);
        parent.Children.Remove(dismiss);
        buttons.Children.Add(dismiss);
        buttons.Children.Add(retry);
        buttons.Children.Add(report);
        buttons.Bind(Visual.IsVisibleProperty, new Binding(nameof(ConnectingViewModel.IsErrored)));
        parent.Children.Insert(0, buttons);

        void UpdateButtons()
        {
            retry.IsVisible = overlay.DataContext is ConnectingViewModel vm && Attempts.TryGetValue(vm, out _);
            retry.Content = Text("Повторить", "Retry");
            report.Content = Text("Скопировать отчёт", "Copy report");
            if (overlay.DataContext is ConnectingViewModel model && ConnectorField.GetValue(model) is Connector connector &&
                (connector.Status == Connector.ConnectionStatus.UpdateError ||
                 connector.Status == Connector.ConnectionStatus.ClientExited))
                retry.Content = Locator.Current.GetRequiredService<Updater>().UpdateException is DownloadStalledException
                    ? Text("Продолжить загрузку", "Resume download")
                    : Text("Проверить и повторить", "Verify and retry");
        }

        ConnectingViewModel? observed = null;
        void StatusChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(ConnectingViewModel.IsErrored))
                UpdateButtons();
        }
        overlay.DataContextChanged += (_, _) =>
        {
            if (observed is not null) observed.PropertyChanged -= StatusChanged;
            observed = overlay.DataContext as ConnectingViewModel;
            if (observed is not null) observed.PropertyChanged += StatusChanged;
            UpdateButtons();
        };
        UpdateButtons();

        retry.Click += async (_, _) =>
        {
            if (_operationInProgress || overlay.DataContext is not ConnectingViewModel vm ||
                !Attempts.TryGetValue(vm, out var attempt) ||
                RecoveryWindowField.GetValue(vm) is not MainWindowViewModel window ||
                ConnectorField.GetValue(vm) is not Connector connector)
                return;
            if (connector.Status == Connector.ConnectionStatus.ConnectionFailed ||
                Locator.Current.GetRequiredService<Updater>().UpdateException is DownloadStalledException)
            {
                ConnectingViewModel.StartConnect(window, attempt.Address);
                return;
            }
            await PrepareServerAsync(window, attempt.Address, repair: true);
        };
        report.Click += async (_, _) =>
        {
            if (overlay.DataContext is not ConnectingViewModel vm ||
                ConnectorField.GetValue(vm) is not Connector connector ||
                TopLevel.GetTopLevel(overlay)?.Clipboard is not { } clipboard)
                return;
            var updater = Locator.Current.GetRequiredService<Updater>();
            var reportText = $"Airlock Launcher {WindowsReleases.InstalledVersion}\nOS: {Environment.OSVersion.Version}\nConnection: {connector.Status}\nUpdate: {updater.Status}\nError: {updater.UpdateException?.GetType().Name ?? "—"}";
            var recent = ReadShareableLogEvents();
            if (recent.Length != 0)
                reportText += "\n\nRecent launcher events:\n" + recent;

            try
            {
                await clipboard.SetTextAsync(reportText);
            }
            catch (Exception error)
            {
                Log.Warning(error, "Could not copy the connection report");
                report.Content = Text("Не удалось скопировать", "Copy failed");
                return;
            }
            report.Content = Text("Скопировано", "Copied");
        };
    }

    private static async Task PrepareServerAsync(MainWindowViewModel model, string address, bool repair)
    {
        if (_operationInProgress || model.Control is not Window owner)
            return;
        _operationInProgress = true;
        using var cancel = new CancellationTokenSource();
        var stage = new TextBlock
        {
            Text = Text("Получение данных сервера…", "Fetching server information…"),
            TextWrapping = TextWrapping.Wrap
        };
        var progress = new ProgressBar { IsIndeterminate = true, Minimum = 0, Maximum = 100 };
        var action = new Button { Content = Text("Отмена", "Cancel") };
        var dialog = new Window
        {
            Title = repair ? Text("Проверка файлов сервера", "Verify server files") : Text("Предварительная загрузка", "Pre-download"),
            Width = 450,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Thickness(18), Spacing = 12, Children = { stage, progress, action } }
        };
        bool finished = false;
        void Cancel()
        {
            action.IsEnabled = false;
            stage.Text = Text("Отмена…", "Cancelling…");
            cancel.Cancel();
        }
        action.Click += (_, _) =>
        {
            if (finished)
                dialog.Close();
            else
                Cancel();
        };
        dialog.Closing += (_, args) =>
        {
            if (finished)
                return;

            args.Cancel = true;
            Cancel();
        };
        var updater = Locator.Current.GetRequiredService<Updater>();
        bool downloading = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        timer.Tick += (_, _) =>
        {
            if (!downloading || cancel.IsCancellationRequested)
                return;

            stage.Text = updater.Status switch
            {
                Updater.UpdateStatus.DownloadingClientUpdate => Text("Загрузка файлов сервера…", "Downloading server files…"),
                Updater.UpdateStatus.DownloadingEngineVersion or Updater.UpdateStatus.DownloadingEngineModules => Text("Загрузка движка и зависимостей…", "Downloading engine and dependencies…"),
                Updater.UpdateStatus.Verifying => Text("Проверка загрузки…", "Verifying download…"),
                _ => Text("Подготовка файлов…", "Preparing files…")
            };
            var current = updater.Progress;
            progress.IsIndeterminate = current is null || current.Value.total <= 0;
            if (current is { } value && value.total > 0)
                progress.Value = Math.Clamp(100d * value.downloaded / value.total, 0, 100);
        };

        Task? closed = null;
        try
        {
            closed = dialog.ShowDialog(owner);
            timer.Start();
            var connector = new Connector();
            var fetch = (Task<(ServerInfo, Uri, Uri)>)GetServerInfo.Invoke(connector, [address, cancel.Token])!;
            var (info, _, _) = await fetch;
            var build = info.BuildInformation ?? throw new InvalidDataException("Server did not provide build information.");
            downloading = true;
            var content = await updater.RunUpdateForLaunchAsync(build, cancel.Token)
                ?? throw updater.UpdateException ?? new InvalidDataException("Update failed.");
            downloading = false;
            if (repair)
            {
                stage.Text = Text("Проверка контрольных сумм…", "Checking file hashes…");
                progress.IsIndeterminate = true;
                bool changed = await Task.Run(() => RepairCachedVersion(content.Version, cancel.Token), cancel.Token);
                if (changed)
                {
                    downloading = true;
                    _ = await updater.RunUpdateForLaunchAsync(build, cancel.Token)
                        ?? throw updater.UpdateException ?? new InvalidDataException("Repair download failed.");
                }
            }
            cancel.Token.ThrowIfCancellationRequested();
            finished = true;
            if (repair)
            {
                dialog.Close();
                await closed;
                _operationInProgress = false;
                ConnectingViewModel.StartConnect(model, address);
                return;
            }
            stage.Text = Text("Файлы сервера и движок скачаны.", "Server files and engine are ready.");
            progress.IsIndeterminate = false;
            progress.Value = 100;
            action.Content = Text("Закрыть", "Close");
            await closed;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            finished = true;
            dialog.Close();
        }
        catch (Exception error)
        {
            Log.Error(error, "Airlock server preparation failed");
            timer.Stop();
            finished = true;
            stage.Text = error is InvalidDataException
                ? Text("Не удалось подготовить файлы сервера. Подробности в журнале лаунчера.", "Could not prepare the server files. See the launcher log.")
                : Text("Не удалось завершить операцию. Проверьте подключение и журнал лаунчера.", "The operation failed. Check your connection and the launcher log.");
            progress.IsVisible = false;
            action.IsEnabled = true;
            action.Content = Text("Закрыть", "Close");
            if (closed is not null)
                await closed;
        }
        finally
        {
            timer.Stop();
            _operationInProgress = false;
        }
    }

    private static bool RepairCachedVersion(long version, CancellationToken cancel)
    {
        using var connection = ContentManager.GetSqliteConnection();
        using var transaction = connection.BeginTransaction();
        using var query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = """
            SELECT c.Id, MIN(m.Path), c.Hash
            FROM ContentManifest m JOIN Content c ON c.Id = m.ContentId
            WHERE m.VersionId = $version GROUP BY c.Id, c.Hash
            """;
        query.Parameters.AddWithValue("$version", version);
        var files = new List<(long Id, string Path, byte[] Hash)>();
        using (var rows = query.ExecuteReader())
            while (rows.Read())
                files.Add((rows.GetInt64(0), rows.GetString(1), (byte[])rows.GetValue(2)));

        var damaged = new List<long>();
        foreach (var file in files)
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                using var data = ContentManager.OpenBlob(connection, version, file.Path);
                if (data is null || !Blake2B.HashStream(data, 32).AsSpan().SequenceEqual(file.Hash))
                    damaged.Add(file.Id);
            }
            catch (Exception error) when (error is InvalidDataException or ZStdException)
            {
                damaged.Add(file.Id);
            }
        }

        if (damaged.Count == 0)
            return false;

        using var remove = connection.CreateCommand();
        remove.Transaction = transaction;
        remove.Parameters.AddWithValue("$version", version);
        remove.CommandText = """
            DELETE FROM ContentVersion
            WHERE Id = $version AND NOT EXISTS (SELECT 1 FROM RunningClient WHERE UsedVersion = $version)
            """;
        if (remove.ExecuteNonQuery() != 1)
            throw new InvalidDataException("The server version is in use.");
        remove.Parameters.Clear();
        var id = remove.Parameters.AddWithValue("$id", 0L);
        foreach (var blob in damaged)
        {
            id.Value = blob;
            remove.CommandText = """
                DELETE FROM Content WHERE Id = $id
                  AND NOT EXISTS (SELECT 1 FROM ContentManifest WHERE ContentId = $id)
                  AND NOT EXISTS (SELECT 1 FROM InterruptedDownloadContent WHERE ContentId = $id)
                """;
            if (remove.ExecuteNonQuery() != 1)
                throw new InvalidDataException("A damaged file is shared by other cached versions or interrupted downloads.");
        }
        cancel.ThrowIfCancellationRequested();
        transaction.Commit();
        return true;
    }

    private static string ReadShareableLogEvents()
    {
        string[] allowed =
        [
            "Exception while trying to run updates", "Exception while starting client",
            "Failed to verify module signature!", "Every engine mirror stalled",
            "Failed to download manifest", "Failed to download engine module manifest",
            "Failed to connect:"
        ];
        try
        {
            var latest = Directory.EnumerateFiles(LauncherPaths.DirLogs, "launcher-*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (latest is null)
                return "";

            using var stream = new FileStream(latest, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Position = Math.Max(0, stream.Length - 65536);
            using var reader = new StreamReader(stream);
            var events = new Queue<string>();
            while (reader.ReadLine() is { } line)
            {
                if (line.Length < 38 || !DateTimeOffset.TryParseExact(line[..30],
                    "yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var when))
                    continue;
                var marker = allowed.FirstOrDefault(message => line.AsSpan(37).TrimStart().StartsWith(message, StringComparison.Ordinal));
                if (marker is null)
                    continue;

                events.Enqueue($"{when:HH:mm:ss} {marker.TrimEnd(':')}");
                if (events.Count > 8)
                    events.Dequeue();
            }
            return string.Join(Environment.NewLine, events);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }
    private static FieldInfo GetPrivateField(Type type, string name) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(type.FullName, name);
    private static string Text(string ru, string en) =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? ru : en;
}
