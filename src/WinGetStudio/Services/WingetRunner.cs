using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace WinGetStudio.Services;

public sealed record RunResult(int ExitCode, TimeSpan Duration, bool Canceled, string Output)
{
    public bool Succeeded => !Canceled && ExitCode == 0;
}

public enum LineKind { Output, Error, Info }

/// <summary>Der Vertrag, ueber den die Ansichtsmodelle winget aufrufen - austauschbar fuer Tests.</summary>
public interface IWingetRunner
{
    Task<RunResult> RunAsync(
        IReadOnlyList<string> args,
        bool elevated,
        Action<string, LineKind> onLine,
        CancellationToken cancellationToken);
}

/// <summary>
/// Fuehrt winget aus und liefert die Ausgabe zeilenweise waehrend des Laufs.
///
/// Erhoehte Aufrufe brauchen einen Sonderweg: ein per "runas" gestarteter Prozess laesst
/// sich nicht umleiten. Deshalb schreibt der erhoehte Aufruf in eine Protokolldatei, die
/// hier waehrend des Laufs mitgelesen wird - fuer die Oberflaeche sieht beides gleich aus.
/// </summary>
public sealed class WingetRunner : IWingetRunner
{
    private readonly string _exePath;

    public WingetRunner(string exePath) => _exePath = exePath;

    public async Task<RunResult> RunAsync(
        IReadOnlyList<string> args,
        bool elevated,
        Action<string, LineKind> onLine,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var commandLine = CommandLineBuilder.ToDisplayLine(args);
        var stderr = new List<string>();

        void Watch(string line, LineKind kind)
        {
            if (kind == LineKind.Error) stderr.Add(line);
            onLine(line, kind);
        }

        try
        {
            var result = elevated
                ? await RunElevatedAsync(args, Watch, stopwatch, cancellationToken)
                : await RunDirectAsync(args, Watch, stopwatch, cancellationToken);

            Report(commandLine, result, stderr, elevated);
            return result;
        }
        catch (OperationCanceledException)
        {
            ErrorLog.Instance.Info(nameof(WingetRunner), "Abgebrochen: " + commandLine);
            return new RunResult(-1, stopwatch.Elapsed, true, "");
        }
        catch (Exception ex)
        {
            // Ohne diesen Zweig wuerde ein Startfehler von winget die Anwendung beenden,
            // statt in der Oberflaeche zu erscheinen.
            ErrorLog.Instance.Error(nameof(WingetRunner), "winget ließ sich nicht ausführen: " + commandLine, ex);
            onLine(ex.Message, LineKind.Error);
            return new RunResult(-1, stopwatch.Elapsed, false, ex.Message);
        }
    }

    /// <summary>Jeder Lauf hinterlaesst eine Spur - erfolgreich als Notiz, sonst als Warnung.</summary>
    private static void Report(string commandLine, RunResult result, List<string> stderr, bool elevated)
    {
        var prefix = elevated ? "[erhöht] " : "";

        if (stderr.Count > 0)
            ErrorLog.Instance.Warn(nameof(WingetRunner),
                $"{prefix}winget meldete Fehlerausgaben: {commandLine}",
                string.Join(Environment.NewLine, stderr));

        if (result.Canceled)
            ErrorLog.Instance.Info(nameof(WingetRunner), $"{prefix}Abgebrochen: {commandLine}");
        else if (result.ExitCode != 0)
            ErrorLog.Instance.Warn(nameof(WingetRunner),
                $"{prefix}Exitcode {result.ExitCode}: {commandLine}",
                LastMeaningfulLine(result.Output));
        else
            ErrorLog.Instance.Info(nameof(WingetRunner),
                $"{prefix}Erfolgreich ({result.Duration.TotalSeconds:N1} s): {commandLine}");
    }

    private static string? LastMeaningfulLine(string output)
    {
        var lines = output.Split('\n')
                          .Select(l => l.TrimEnd('\r').Trim())
                          .Where(l => l.Length > 0)
                          .ToList();
        return lines.Count == 0 ? null : lines[^1];
    }

    private async Task<RunResult> RunDirectAsync(
        IReadOnlyList<string> args, Action<string, LineKind> onLine,
        Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(_exePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var collected = new StringBuilder();

        void Handle(string? line, LineKind kind)
        {
            if (line is null) return;
            lock (collected) collected.AppendLine(line);
            onLine(line, kind);
        }

        process.OutputDataReceived += (_, e) => Handle(e.Data, LineKind.Output);
        process.ErrorDataReceived += (_, e) => Handle(e.Data, LineKind.Error);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new RunResult(-1, stopwatch.Elapsed, true, collected.ToString());
        }

        return new RunResult(process.ExitCode, stopwatch.Elapsed, false, collected.ToString());
    }

    private async Task<RunResult> RunElevatedAsync(
        IReadOnlyList<string> args, Action<string, LineKind> onLine,
        Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        var logPath = Path.Combine(Path.GetTempPath(), $"wgstudio-{Guid.NewGuid():N}.log");
        // cmd.exe braucht die aeussere Klammerung, sobald Programmpfad und Ziel Anfuehrungszeichen tragen.
        var inner = new StringBuilder();
        inner.Append('"').Append(_exePath).Append('"');
        foreach (var a in args) inner.Append(' ').Append(QuoteForCmd(a));
        inner.Append(" > \"").Append(logPath).Append("\" 2>&1");

        var psi = new ProcessStartInfo("cmd.exe")
        {
            Arguments = "/c \"" + inner + "\"",
            UseShellExecute = true,
            Verb = "runas",
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED
        {
            const string message = "Die Abfrage der Administratorrechte wurde abgelehnt.";
            ErrorLog.Instance.Warn(nameof(WingetRunner), message);
            onLine(message, LineKind.Error);
            return new RunResult(1223, stopwatch.Elapsed, true, "");
        }

        if (process is null)
        {
            const string message = "Der erhöhte Vorgang konnte nicht gestartet werden.";
            ErrorLog.Instance.Error(nameof(WingetRunner), message, $"cmd.exe {psi.Arguments}");
            onLine(message, LineKind.Error);
            return new RunResult(-1, stopwatch.Elapsed, false, message);
        }

        using (process)
        {
            var collected = new StringBuilder();
            var offset = 0L;

            async Task PumpAsync()
            {
                offset = ReadNewLines(logPath, offset, line =>
                {
                    collected.AppendLine(line);
                    onLine(line, LineKind.Output);
                });
                await Task.Delay(200, CancellationToken.None);
            }

            try
            {
                while (!process.HasExited)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await PumpAsync();
                }
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                ReadNewLines(logPath, offset, line => onLine(line, LineKind.Output));
                TryDelete(logPath);
                return new RunResult(-1, stopwatch.Elapsed, true, collected.ToString());
            }

            // Nachlauf: was zwischen letztem Lesen und Prozessende geschrieben wurde.
            await Task.Delay(150, CancellationToken.None);
            ReadNewLines(logPath, offset, line =>
            {
                collected.AppendLine(line);
                onLine(line, LineKind.Output);
            });

            var exitCode = process.ExitCode;
            TryDelete(logPath);
            return new RunResult(exitCode, stopwatch.Elapsed, false, collected.ToString());
        }
    }

    /// <summary>Liest ab <paramref name="offset"/> alle vollstaendigen Zeilen und gibt den neuen Offset zurueck.</summary>
    private static long ReadNewLines(string path, long offset, Action<string> onLine)
    {
        try
        {
            if (!File.Exists(path)) return offset;

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                                              FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length <= offset) return offset;

            stream.Seek(offset, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, new UTF8Encoding(false));

            var text = reader.ReadToEnd();
            // Eine angefangene letzte Zeile bleibt fuer den naechsten Durchgang liegen.
            var lastBreak = text.LastIndexOf('\n');
            if (lastBreak < 0) return offset;

            var complete = text[..(lastBreak + 1)];
            foreach (var line in complete.Split('\n'))
                onLine(line.TrimEnd('\r'));

            return offset + new UTF8Encoding(false).GetByteCount(complete);
        }
        catch (IOException ex)
        {
            // Die Datei wird gerade vom erhöhten Prozess geschrieben - beim nächsten
            // Durchgang erneut versuchen. Nur vermerken, nicht stören.
            ErrorLog.Instance.Info(nameof(WingetRunner), "Protokolldatei kurzzeitig gesperrt: " + ex.Message);
            return offset;
        }
    }

    /// <summary>Quoting nach den Regeln der Windows-Kommandozeile, nicht nach PowerShell-Regeln.</summary>
    private static string QuoteForCmd(string argument)
    {
        if (argument.Length > 0 && !argument.Any(c => char.IsWhiteSpace(c) || c is '"'))
            return argument;

        var builder = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\') { backslashes++; continue; }
            if (c == '"') { builder.Append('\\', backslashes * 2 + 1).Append('"'); backslashes = 0; continue; }
            builder.Append('\\', backslashes);
            backslashes = 0;
            builder.Append(c);
        }
        builder.Append('\\', backslashes * 2).Append('"');
        return builder.ToString();
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            ErrorLog.Instance.Warn(nameof(WingetRunner),
                "Der laufende winget-Vorgang ließ sich nicht beenden. Er läuft möglicherweise weiter.", ex);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            ErrorLog.Instance.Info(nameof(WingetRunner),
                $"Temporäre Protokolldatei blieb liegen: {path} ({ex.GetType().Name})");
        }
    }
}
