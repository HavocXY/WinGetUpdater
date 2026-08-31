using WinGetUpdater.Services;
using Xunit;

namespace WinGetUpdater.Tests;

public class CommandLineBuilderTests
{
    private readonly SchemaStore _store = TestSchema.Load();

    private CommandLineBuilder Builder => new(_store);

    [Fact]
    public void Flags_erscheinen_nur_wenn_gesetzt()
    {
        var install = _store.Find("install")!;

        var withoutFlag = Builder.Build(install, new Dictionary<string, object?> { ["exact"] = false });
        Assert.DoesNotContain("--exact", withoutFlag);

        var withFlag = Builder.Build(install, new Dictionary<string, object?> { ["exact"] = true });
        Assert.Contains("--exact", withFlag);
    }

    [Fact]
    public void Leere_Textwerte_werden_weggelassen()
    {
        var search = _store.Find("search")!;
        var args = Builder.Build(search, new Dictionary<string, object?>
        {
            ["query"] = "   ",
            ["id"] = "7zip.7zip"
        });

        Assert.DoesNotContain("--query", args);
        Assert.Equal(["search", "--id", "7zip.7zip"], args);
    }

    [Fact]
    public void Positionsargument_steht_vorn_und_traegt_sein_Flag()
    {
        var hash = _store.Find("hash")!;
        var args = Builder.Build(hash, new Dictionary<string, object?>
        {
            ["msix"] = true,
            ["hashFile"] = @"C:\tmp\setup.exe"
        });

        Assert.Equal(["hash", "--file", @"C:\tmp\setup.exe", "--msix"], args);
    }

    [Fact]
    public void Es_wird_immer_die_Langform_erzeugt_nie_die_Kurzform()
    {
        // Kurzformen sind bei winget mehrdeutig: -h ist bei install "silent",
        // bei configure "history". Die Langform ist eindeutig.
        var install = _store.Find("install")!;
        var args = Builder.Build(install, new Dictionary<string, object?> { ["silent"] = true });

        Assert.Contains("--silent", args);
        Assert.DoesNotContain("-h", args);
    }

    [Fact]
    public void Wiederholbare_Optionen_erzeugen_je_Wert_ein_Flag()
    {
        var list = _store.Find("list")!;
        var args = Builder.Build(list, new Dictionary<string, object?>
        {
            ["sort"] = new List<string> { "name", "version" }
        });

        Assert.Equal(["list", "--sort", "name", "--sort", "version"], args);
    }

    [Fact]
    public void Zusatzargumente_haengen_hinten_an()
    {
        var upgrade = _store.Find("upgrade")!;
        var args = Builder.Build(upgrade, new Dictionary<string, object?> { ["recurse"] = true },
                                 ["--custom", "/norestart"]);

        Assert.Equal(["upgrade", "--all", "--custom", "/norestart"], args);
    }

    [Theory]
    [InlineData("einfach", "einfach")]
    [InlineData(@"C:\Program Files\App", "\"C:\\Program Files\\App\"")]
    [InlineData("mit\"Anfuehrung", "\"mit\"\"Anfuehrung\"")]
    [InlineData("", "\"\"")]
    public void Quoting_folgt_PowerShell_Regeln(string input, string expected) =>
        Assert.Equal(expected, CommandLineBuilder.QuoteForPowerShell(input));

    [Fact]
    public void Vorschau_ist_reproduzierbar()
    {
        var install = _store.Find("install")!;
        var values = new Dictionary<string, object?>
        {
            ["query"] = "7zip",
            ["exact"] = true,
            ["scope"] = "machine",
            ["location"] = @"C:\Program Files\Mit Leerzeichen"
        };

        var first = CommandLineBuilder.ToDisplayLine(Builder.Build(install, values));
        var second = CommandLineBuilder.ToDisplayLine(Builder.Build(install, values));

        Assert.Equal(first, second);
        Assert.Equal(
            "winget install --query 7zip --exact --scope machine --location \"C:\\Program Files\\Mit Leerzeichen\"",
            first);
    }
}
