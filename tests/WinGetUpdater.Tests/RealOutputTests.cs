using System.IO;
using WinGetUpdater.Services;
using Xunit;

namespace WinGetUpdater.Tests;

/// <summary>
/// Prueft den Tabellenparser gegen unveraenderte Ausgaben eines echten deutschen Windows.
/// Beide Dateien stehen fuer je einen Fall, an dem eine naheliegende Umsetzung scheitert.
/// </summary>
public class RealOutputTests
{
    private static string Fixture(string name)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Fixtures", name),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", name)
        };
        var path = candidates.FirstOrDefault(File.Exists)
                   ?? throw new FileNotFoundException($"Testdatei fehlt: {name}");
        return File.ReadAllText(path);
    }

    // ---------------------------------------------------------------- winget upgrade

    [Fact]
    public void Upgrade_Zusammenfassung_wird_nicht_als_Paket_gelesen()
    {
        // "1 Aktualisierungen verfügbar." haengt ohne Leerzeile direkt an der Tabelle.
        var table = TableParser.Parse(Fixture("upgrade-de.txt"))!;

        Assert.Single(table.Rows);
        Assert.Equal("Stirling PDF", table.Cell(0, 0));
        Assert.Contains(table.Trailer, t => t.Contains("Aktualisierungen"));
    }

    [Fact]
    public void Upgrade_trennt_die_eng_gesetzten_Spalten()
    {
        // Der Kopf lautet "Version Verfügbar Quelle" - mit je einem Leerzeichen.
        var table = TableParser.Parse(Fixture("upgrade-de.txt"))!;

        Assert.Equal(5, table.Columns.Count);
        Assert.Equal("Verfügbar", table.Columns[3]);
        Assert.Equal("Quelle", table.Columns[4]);
        Assert.Equal("2.14.0", table.Cell(0, 2));
        Assert.Equal("2.14.3", table.Cell(0, 3));
        Assert.Equal("winget", table.Cell(0, 4));
    }

    // ---------------------------------------------------------------- winget list

    [Fact]
    public void Doppelt_breites_Zeichen_verschiebt_die_Zeile_nicht()
    {
        // Der Paketname enthaelt "㙘". winget fuellt nach Darstellungsbreite, das Zeichen
        // belegt zwei Spalten aber nur ein char. Wer in Zeichen rechnet, liest die Zeile
        // um eine Stelle verschoben - die ID beginnt dann mitten im Namen.
        var table = TableParser.Parse(Fixture("list-de-wide.txt"))!;

        Assert.Equal(3, table.Rows.Count);
        Assert.Equal("Autodesk Advance Steel 2025 – Deutsch (German) 㙘", table.Cell(0, 0));
        Assert.StartsWith(@"ARP\Machine\X64\{DB1E901E", table.Cell(0, 1));
    }

    [Fact]
    public void Zeilen_ohne_Sonderzeichen_bleiben_unveraendert_richtig()
    {
        var table = TableParser.Parse(Fixture("list-de-wide.txt"))!;

        Assert.Equal("Advance Steel 2025.0.3 Hotfix", table.Cell(1, 0));
        Assert.StartsWith(@"ARP\Machine\X64\{909C33DD", table.Cell(1, 1));
        Assert.Equal("29.0.329.0", table.Cell(1, 2));
    }

    [Fact]
    public void Keine_echte_Zeile_landet_im_Anhang()
    {
        var table = TableParser.Parse(Fixture("list-de-wide.txt"))!;
        Assert.Empty(table.Trailer);
    }
}
