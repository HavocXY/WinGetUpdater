using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using WinGetUpdater.Services;
using WinGetUpdater.ViewModels;

namespace WinGetUpdater;

public partial class App : Application
{
    private static readonly string CrashLog =
        Path.Combine(Path.GetTempPath(), "wingetupdater-crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--selftest", StringComparer.OrdinalIgnoreCase))
        {
            var exitCode = RunSelfTest();
            Shutdown(exitCode);
            return;
        }

        // Drei Wege, auf denen eine Ausnahme entkommen kann - alle drei enden im Protokoll.
        DispatcherUnhandledException += OnDispatcherException;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var exception = args.ExceptionObject as Exception;
            ErrorLog.Instance.Error("AppDomain", "Nicht behandelter Fehler außerhalb der Oberfläche.", exception);
            Write(exception);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ErrorLog.Instance.Error("TaskScheduler",
                "Ein Hintergrundvorgang ist fehlgeschlagen, ohne dass jemand das Ergebnis abgeholt hat.",
                args.Exception);
            Write(args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);

        var window = new Views.ShellWindow();
        MainWindow = window;
        window.Show();

        var screenshotIndex = Array.FindIndex(e.Args,
            a => string.Equals(a, "--screenshot", StringComparison.OrdinalIgnoreCase));
        if (screenshotIndex >= 0 && screenshotIndex + 1 < e.Args.Length)
            _ = CaptureAndExitAsync(window, e.Args, screenshotIndex);
    }

    /// <summary>Fenster aufbauen, optional einen Befehl ausfuehren, aufnehmen, beenden.</summary>
    private async Task CaptureAndExitAsync(Views.ShellWindow window, string[] args, int screenshotIndex)
    {
        try
        {
            var shell = (ShellVm)window.DataContext;

            if (args.Contains("--light", StringComparer.OrdinalIgnoreCase))
                shell.ToggleThemeCommand.Execute(null);
            if (args.Contains("--english", StringComparer.OrdinalIgnoreCase))
                shell.ToggleLanguageCommand.Execute(null);

            var commandIndex = Array.FindIndex(args,
                a => string.Equals(a, "--command", StringComparison.OrdinalIgnoreCase));
            if (commandIndex >= 0 && commandIndex + 1 < args.Length)
            {
                shell.Select(args[commandIndex + 1]);
                shell.Mode = AppMode.Advanced;
            }

            var queryIndex = Array.FindIndex(args,
                a => string.Equals(a, "--query", StringComparison.OrdinalIgnoreCase));
            if (queryIndex >= 0 && queryIndex + 1 < args.Length)
                shell.Current?.Preset("query", args[queryIndex + 1]);

            if (args.Contains("--hide-options", StringComparer.OrdinalIgnoreCase) && shell.Current is not null)
                shell.Current.OptionsVisible = false;

            var run = args.Contains("--run", StringComparer.OrdinalIgnoreCase);

            if (shell.Mode == AppMode.Updates)
            {
                // Die Update-Ansicht prueft beim Anzeigen von selbst. Abgewartet wird beides:
                // dass die Pruefung ueberhaupt angelaufen ist und dass sie fertig wird.
                await WaitWhileAsync(
                    () => shell.Updates is { } u && (u.Stage == UpdateStage.Start || u.IsBusy),
                    TimeSpan.FromSeconds(120));
                if (args.Contains("--update", StringComparer.OrdinalIgnoreCase) && shell.Updates is not null)
                    await shell.Updates.RunAsync();
            }
            else if (run && shell.Current is not null)
            {
                await shell.Current.RunAsync();
            }

            if (args.Contains("--log", StringComparer.OrdinalIgnoreCase))
                shell.ShowLog = true;
            if (args.Contains("--options", StringComparer.OrdinalIgnoreCase) && shell.Updates is not null)
                shell.Updates.ShowOptions = true;
            if (args.Contains("--output", StringComparer.OrdinalIgnoreCase) && shell.Updates is not null)
                shell.Updates.ShowOutput = true;

            if (screenshotIndex >= 0 && !string.Equals(args[screenshotIndex + 1], "none",
                                                       StringComparison.OrdinalIgnoreCase))
                await Views.Screenshot.CaptureAsync(window, args[screenshotIndex + 1]);

            // Kurzbericht fuer automatisierte Durchlaeufe ueber viele Befehle.
            var reportIndex = Array.FindIndex(args,
                a => string.Equals(a, "--report", StringComparison.OrdinalIgnoreCase));
            if (reportIndex >= 0 && reportIndex + 1 < args.Length && shell.Current is not null)
            {
                var page = shell.Current;
                var firstLine = page.Output.FirstOrDefault()?.Text ?? "";
                File.AppendAllText(args[reportIndex + 1],
                    $"{page.Spec.Id}\t{page.State}\t{page.StatusText}\t{page.RowCount}\t" +
                    $"{page.PreviewLine}\t{firstLine}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
        }
        catch (Exception ex)
        {
            Write(ex);
        }
        finally
        {
            Shutdown(0);
        }
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ErrorLog.Instance.Error("Oberfläche", e.Exception.Message, e.Exception);
        Write(e.Exception);

        // Die Anwendung laeuft weiter; der Eintrag steht im Protokoll und die Anzeige
        // in der Kopfzeile macht darauf aufmerksam. Kein Dialog, der den Ablauf unterbricht.
        e.Handled = true;
    }

    private static void Write(Exception? exception)
    {
        if (exception is null) return;
        try
        {
            File.AppendAllText(CrashLog,
                $"{DateTime.Now:u}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}",
                new UTF8Encoding(true));
        }
        catch { /* Ein Fehler beim Protokollieren darf den Absturz nicht verdoppeln. */ }
    }

    /// <summary>Wartet, solange die Bedingung gilt - laengstens die angegebene Zeit.</summary>
    private static async Task WaitWhileAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (condition() && DateTime.UtcNow < deadline)
            await Task.Delay(120);
    }

    /// <summary>
    /// Prueft ohne Fenster, ob Schema, Befehlsaufbau und winget-Suche zusammenspielen.
    /// Aufruf: WinGetUpdater.exe --selftest
    /// </summary>
    private static int RunSelfTest()
    {
        var report = new StringBuilder();
        var failures = 0;

        void Check(string label, bool ok, string detail = "")
        {
            if (!ok) failures++;
            report.AppendLine($"[{(ok ? "OK" : "FEHLER")}] {label}{(detail.Length > 0 ? " - " + detail : "")}");
        }

        try
        {
            var store = SchemaStore.Load();
            Check("Schema geladen", store.Commands.Count > 0,
                  $"{store.Commands.Count} Befehle, {store.Schema.Options.Count} Optionen");

            var missing = store.Commands
                .SelectMany(c => new[] { c.Positional }.Concat(c.Primary).Concat(c.Advanced))
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .Where(id => !store.TryGetOption(id!, out _))
                .ToList();
            Check("Alle Options-Ids aufloesbar", missing.Count == 0, string.Join(", ", missing));

            var winget = WingetLocator.Locate();
            Check("winget gefunden", winget is not null,
                  winget is null ? "" : $"{winget.Version} über {winget.HowFound}");

            var builder = new CommandLineBuilder(store);
            var install = store.Find("install")!;
            var args = builder.Build(install, new Dictionary<string, object?>
            {
                ["query"] = "7zip",
                ["exact"] = true,
                ["scope"] = "machine",
                ["location"] = @"C:\Program Files\Mit Leerzeichen",
                ["disableInteractivity"] = true
            });
            var line = CommandLineBuilder.ToDisplayLine(args);
            // Reihenfolge folgt der Schemareihenfolge (primary, dann advanced, dann global),
            // damit dieselben Eingaben immer dieselbe Zeile ergeben.
            var expected = "winget install --query 7zip --exact --scope machine " +
                           "--location \"C:\\Program Files\\Mit Leerzeichen\" --disable-interactivity";
            Check("Befehlszeile korrekt", line == expected, line);

            var advice = ElevationService.Advise(install,
                new Dictionary<string, object?> { ["scope"] = "machine" });
            Check("Rechteerhoehung erkannt", advice.Recommended, advice.ReasonKey);

            var table = TableParser.Parse(
                "Name              ID                    Version\n" +
                "-----------------------------------------------\n" +
                "7-Zip             7zip.7zip             24.09\n" +
                "Visual Studio Co  Microsoft.VisualStud  1.135.0\n");
            Check("Tabelle geparst", table is { Rows.Count: 2 } && table.IdColumn == 1,
                  table is null ? "nicht erkannt" : $"{table.Rows.Count} Zeilen, ID-Spalte {table.IdColumn}");
            Check("Name mit Leerzeichen erhalten",
                  table is not null && table.Cell(1, 0) == "Visual Studio Co",
                  table?.Cell(1, 0) ?? "");
        }
        catch (Exception ex)
        {
            failures++;
            report.AppendLine("[FEHLER] Ausnahme: " + ex);
        }

        report.AppendLine();
        report.AppendLine(failures == 0 ? "Selbsttest bestanden." : $"{failures} Pruefung(en) fehlgeschlagen.");

        var target = Path.Combine(Path.GetTempPath(), "wingetupdater-selftest.txt");
        File.WriteAllText(target, report.ToString(), new UTF8Encoding(false));
        Console.Write(report.ToString());
        return failures == 0 ? 0 : 1;
    }
}
