using System.IO;
using System.Text.RegularExpressions;
using WinGetStudio.Services;
using Xunit;

namespace WinGetStudio.Tests;

public class ErrorLogTests
{
    [Fact]
    public void Fehler_und_Warnungen_werden_gezaehlt_Infos_nicht()
    {
        var log = ErrorLog.Instance;
        log.Clear();

        log.Info("Test", "belanglos");
        log.Warn("Test", "grenzwertig");
        log.Error("Test", "kaputt");

        Assert.Equal(1, log.ErrorCount);
        Assert.Equal(1, log.WarningCount);
        Assert.Equal(3, log.Entries.Count);
        Assert.True(log.HasErrors);
        Assert.Equal("2", log.BadgeText);

        log.Clear();
        Assert.False(log.HasProblems);
    }

    [Fact]
    public void Der_neueste_Eintrag_steht_oben()
    {
        var log = ErrorLog.Instance;
        log.Clear();

        log.Info("Test", "erster");
        log.Info("Test", "zweiter");

        Assert.Equal("zweiter", log.Entries[0].Message);
        log.Clear();
    }

    [Fact]
    public void Ausnahmen_landen_vollstaendig_im_Detail()
    {
        var log = ErrorLog.Instance;
        log.Clear();

        try { throw new InvalidOperationException("Beispielfehler"); }
        catch (Exception ex) { log.Error("Test", "etwas ging schief", ex); }

        var entry = log.Entries[0];
        Assert.True(entry.HasDetail);
        Assert.Contains("InvalidOperationException", entry.Detail);
        Assert.Contains("Beispielfehler", entry.Detail);
        log.Clear();
    }
}

/// <summary>
/// Haelt die Grundregel dieser Anwendung fest: es gibt keinen leeren catch-Block.
/// Jeder abgefangene Fehler muss irgendwo landen, sonst ist er verschwunden.
/// </summary>
public class NoSwallowedErrorsTests
{
    // Einzige Ausnahme: der Logger selbst. Scheitert das Schreiben der Protokolldatei,
    // kann er sich nicht bei sich selbst beschweren, ohne sich zu verhaken.
    private static readonly string[] Exempt = ["ErrorLog.cs"];

    private static readonly Regex EmptyCatch = new(
        @"catch\s*(\([^)]*\))?\s*\{\s*(//[^\r\n]*\s*)*\}",
        RegexOptions.Compiled);

    private static string SourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "src", "WinGetStudio");
    }

    [Fact]
    public void Kein_catch_Block_ist_leer_oder_enthaelt_nur_einen_Kommentar()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(SourceRoot(), "*.cs", SearchOption.AllDirectories))
        {
            if (Exempt.Contains(Path.GetFileName(file))) continue;

            var text = File.ReadAllText(file);
            foreach (Match match in EmptyCatch.Matches(text))
            {
                var line = text[..match.Index].Count(c => c == '\n') + 1;
                offenders.Add($"{Path.GetFileName(file)}:{line}  {match.Value.Replace("\r\n", " ").Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Fehler würden hier stillschweigend verschwinden:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void Jede_Quelldatei_mit_catch_kennt_den_Logger()
    {
        // Grobe, aber wirksame Gegenprobe: wo gefangen wird, muss der Logger bekannt sein.
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(SourceRoot(), "*.cs", SearchOption.AllDirectories))
        {
            if (Exempt.Contains(Path.GetFileName(file))) continue;

            var text = File.ReadAllText(file);
            if (!text.Contains("catch")) continue;
            if (!text.Contains("ErrorLog")) offenders.Add(Path.GetFileName(file));
        }

        Assert.True(offenders.Count == 0,
            "Fängt Fehler ab, ohne sie zu protokollieren: " + string.Join(", ", offenders));
    }
}
