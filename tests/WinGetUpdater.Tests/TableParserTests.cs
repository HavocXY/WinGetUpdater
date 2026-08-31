using WinGetUpdater.Services;
using Xunit;

namespace WinGetUpdater.Tests;

public class TableParserTests
{
    // Echte Ausgabe von "winget search vscode" auf einem deutschen Windows.
    private const string GermanSearch =
        "Name                                      ID                                        Version      Übereinstimmung                    Quelle\r\n" +
        "------------------------------------------------------------------------------------------------------------------------------------------\r\n" +
        "Microsoft Visual Studio Code              Microsoft.VisualStudioCode                1.135.0      Moniker: vscode                    winget\r\n" +
        "Visual Studio / Code for Command Palette  15722UsefulApp.WorkspaceLauncherForVSCode 1.30.0.0     Tag: vscode                        winget\r\n" +
        "VSCodium                                  VSCodium.VSCodium                         1.126.04524  Tag: vscode                        winget\r\n";

    // Dieselbe Abfrage auf einem englischen Windows - andere Spaltennamen, gleiche Struktur.
    private const string EnglishSearch =
        "Name                          Id                          Version   Match            Source\r\n" +
        "----------------------------------------------------------------------------------------\r\n" +
        "Microsoft Visual Studio Code  Microsoft.VisualStudioCode  1.135.0   Moniker: vscode  winget\r\n";

    [Fact]
    public void Deutsche_Ausgabe_wird_erkannt()
    {
        var table = TableParser.Parse(GermanSearch);

        Assert.NotNull(table);
        Assert.Equal(5, table!.Columns.Count);
        Assert.Equal(3, table.Rows.Count);
        Assert.Equal("Übereinstimmung", table.Columns[3]);
    }

    [Fact]
    public void Spaltenzuordnung_haengt_nicht_an_der_Sprache()
    {
        var german = TableParser.Parse(GermanSearch)!;
        var english = TableParser.Parse(EnglishSearch)!;

        Assert.Equal(1, german.IdColumn);
        Assert.Equal(1, english.IdColumn);
        Assert.Equal(2, german.VersionColumn);
        Assert.Equal(2, english.VersionColumn);
        Assert.Equal(4, german.SourceColumn);
        Assert.Equal(4, english.SourceColumn);
    }

    [Fact]
    public void Werte_mit_Leerzeichen_bleiben_zusammen()
    {
        var table = TableParser.Parse(GermanSearch)!;

        Assert.Equal("Visual Studio / Code for Command Palette", table.Cell(1, 0));
        Assert.Equal("Moniker: vscode", table.Cell(0, 3));
    }

    [Fact]
    public void Lange_Bezeichner_werden_nicht_beschnitten()
    {
        var table = TableParser.Parse(GermanSearch)!;
        Assert.Equal("15722UsefulApp.WorkspaceLauncherForVSCode", table.Cell(1, 1));
    }

    [Fact]
    public void Text_nach_einer_Leerzeile_gehoert_nicht_mehr_zur_Tabelle()
    {
        var withSummary = GermanSearch + "\r\n3 Pakete gefunden.\r\n";
        var table = TableParser.Parse(withSummary)!;

        Assert.Equal(3, table.Rows.Count);
    }

    [Fact]
    public void Fortschrittszeilen_vor_der_Tabelle_stoeren_nicht()
    {
        var noisy = "Es wurden mehrere Installationspakete gefunden...\r\n\r\n" + GermanSearch;
        var table = TableParser.Parse(noisy);

        Assert.NotNull(table);
        Assert.Equal(3, table!.Rows.Count);
    }

    [Fact]
    public void Nicht_tabellarische_Ausgabe_liefert_null()
    {
        Assert.Null(TableParser.Parse("Gefunden 7-Zip [7zip.7zip]\r\nErfolgreich installiert.\r\n"));
        Assert.Null(TableParser.Parse(""));
    }

    [Fact]
    public void Id_Spalte_wird_auch_ohne_passenden_Spaltenkopf_gefunden()
    {
        // Falls winget den Kopf einmal anders benennt, entscheidet die Form der Werte.
        var odd =
            "Anwendung        Bezeichner                  Fassung\r\n" +
            "-------------------------------------------------------\r\n" +
            "7-Zip            7zip.7zip                   24.09\r\n" +
            "Notepad++        Notepadplusplus.Notepadplusplus 8.7\r\n";

        var table = TableParser.Parse(odd)!;
        Assert.Equal(1, table.IdColumn);
    }
}

public class WingetListHeaderTests
{
    // "winget list" schreibt den Kopf als "Verfügbar Quelle" - mit nur einem Leerzeichen,
    // weil die Spalte genau so breit ist wie ihr eigener Name. Aus der Kopfzeile allein
    // ist die Grenze nicht zu erkennen; erst die Datenzeilen verraten sie.
    //
    // Die Zeilen werden aus Spaltenbreiten gebaut statt von Hand ausgerichtet, damit der
    // Test die Eigenschaft wirklich abbildet und nicht an einem Tippfehler in den
    // Leerzeichen scheitert. Die Breiten entsprechen echter winget-Ausgabe, nur gekuerzt.
    private static string Row(string name, string id, string version, string available, string source) =>
        name.PadRight(32) + id.PadRight(26) + version.PadRight(12) + available.PadRight(10) + source;

    private static readonly string ListOutput = string.Join("\r\n",
        Row("Name", "ID", "Version", "Verfügbar", "Quelle"),
        new string('-', 86),
        Row("Advance Steel 2025.0.3 Hotfix", @"ARP\Machine\X64\{909C33D}", "29.0.329.0", "", ""),
        Row("Affinity", "Canva.Affinity", "3.2.3.4646", "", "winget"),
        Row("Git", "Git.Git", "2.47.0", "2.48.1", "winget")) + "\r\n";

    [Fact]
    public void Einzeln_getrennte_Spaltenkoepfe_werden_ueber_die_Datenzeilen_getrennt()
    {
        var table = TableParser.Parse(ListOutput);

        Assert.NotNull(table);
        Assert.Equal(5, table!.Columns.Count);
        Assert.Equal("Verfügbar", table.Columns[3]);
        Assert.Equal("Quelle", table.Columns[4]);
    }

    [Fact]
    public void Werte_landen_in_der_richtigen_Spalte()
    {
        var table = TableParser.Parse(ListOutput)!;

        Assert.Equal(3, table.AvailableColumn);
        Assert.Equal(4, table.SourceColumn);
        Assert.Equal("2.48.1", table.Cell(2, 3));
        Assert.Equal("winget", table.Cell(2, 4));
        Assert.Equal("", table.Cell(0, 4));
        Assert.Equal("Advance Steel 2025.0.3 Hotfix", table.Cell(0, 0));
    }

    [Fact]
    public void Namen_mit_Leerzeichen_werden_nicht_faelschlich_zerlegt()
    {
        // Gegenprobe: die Namensspalte enthaelt Leerzeichen, darf aber nicht
        // in mehrere Spalten zerfallen.
        var table = TableParser.Parse(ListOutput)!;
        Assert.Equal(5, table.Columns.Count);
        Assert.Equal("Affinity", table.Cell(1, 0));
    }
}
