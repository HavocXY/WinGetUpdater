using System.Diagnostics;
using System.IO;
using System.Text;

namespace WinGetUpdater.Services;

public sealed record WingetInfo(string ExePath, string Version, string HowFound);

/// <summary>
/// Findet winget.exe. Der blosse Aufruf von "winget" ueber PATH funktioniert nicht in
/// jedem Kontext: der Eintrag in WindowsApps ist ein App-Ausfuehrungsalias, den manche
/// Prozesse nicht aufloesen. Deshalb drei Stufen mit Rueckfallebene.
/// (Vorgehen entlehnt von Get-WingetCmd.ps1 aus Winget-AutoUpdate, MIT.)
/// </summary>
public static class WingetLocator
{
    public static WingetInfo? Locate()
    {
        var tried = new List<string>();

        foreach (var (path, how) in Candidates())
        {
            tried.Add(path);
            var version = TryGetVersion(path);
            if (version is not null)
            {
                ErrorLog.Instance.Info(nameof(WingetLocator), $"winget {version} gefunden über {how}: {path}");
                return new WingetInfo(path, version, how);
            }
        }

        // Erst das endgueltige Scheitern ist ein Fehler - dass einzelne Wege nichts ergeben,
        // ist der Normalfall und steht nur als Info im Protokoll.
        ErrorLog.Instance.Error(nameof(WingetLocator),
            "winget.exe wurde nicht gefunden. Die Anwendung kann ohne winget nichts ausführen.",
            "Geprüfte Pfade:" + Environment.NewLine + string.Join(Environment.NewLine, tried));
        return null;
    }

    private static IEnumerable<(string Path, string How)> Candidates()
    {
        // 1. Ueber PATH bzw. den App-Ausfuehrungsalias.
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var alias = Path.Combine(local, "Microsoft", "WindowsApps", "winget.exe");
        if (File.Exists(alias))
            yield return (alias, "App-Ausführungsalias");

        yield return ("winget.exe", "PATH");

        // 2. Direkt im Installationsordner des App-Installers, fuer Kontexte ohne Alias.
        foreach (var root in new[]
                 {
                     Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files",
                     Environment.GetEnvironmentVariable("ProgramW6432") ?? @"C:\Program Files"
                 }.Distinct())
        {
            var windowsApps = Path.Combine(root, "WindowsApps");
            string[] matches;
            try
            {
                matches = Directory.GetDirectories(windowsApps, "Microsoft.DesktopAppInstaller_*_8wekyb3d8bbwe");
            }
            catch (Exception ex)
            {
                // WindowsApps ist normalerweise ACL-geschuetzt - kein Grund zum Abbruch,
                // aber es soll nachvollziehbar bleiben, warum dieser Weg nichts ergab.
                ErrorLog.Instance.Info(nameof(WingetLocator),
                    $"Ordner nicht lesbar: {windowsApps} ({ex.GetType().Name})");
                continue;
            }

            // Neueste Version zuerst.
            foreach (var dir in matches.OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var exe = Path.Combine(dir, "winget.exe");
                if (File.Exists(exe))
                    yield return (exe, "WindowsApps-Paketordner");
            }
        }
    }

    private static string? TryGetVersion(string exePath)
    {
        try
        {
            var psi = new ProcessStartInfo(exePath, "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = new UTF8Encoding(false)
            };
            using var process = Process.Start(psi);
            if (process is null) return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            if (!process.WaitForExit(8000))
            {
                // Prozess explizit beenden - Dispose allein toetet ihn nicht.
                try { process.Kill(entireProcessTree: true); }
                catch (Exception ex)
                {
                    ErrorLog.Instance.Warn(nameof(WingetLocator),
                        "Beim Beenden des nicht antwortenden winget-Prozesses ist ein Fehler aufgetreten.", ex);
                }
                ErrorLog.Instance.Warn(nameof(WingetLocator),
                    $"\"{exePath} --version\" hat nach 8 Sekunden nicht geantwortet.");
                return null;
            }

            if (process.ExitCode != 0 || output.Length == 0)
            {
                ErrorLog.Instance.Info(nameof(WingetLocator),
                    $"\"{exePath} --version\" lieferte Exitcode {process.ExitCode}.");
                return null;
            }

            return output;
        }
        catch (Exception ex)
        {
            ErrorLog.Instance.Info(nameof(WingetLocator),
                $"Pfad nicht nutzbar: {exePath} ({ex.GetType().Name}: {ex.Message})");
            return null;
        }
    }
}
