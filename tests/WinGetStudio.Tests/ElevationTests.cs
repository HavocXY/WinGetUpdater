using WinGetStudio.Services;
using WinGetStudio.ViewModels;
using Xunit;

namespace WinGetStudio.Tests;

public class ElevationTests
{
    private readonly SchemaStore _store = TestSchema.Load();

    [Fact]
    public void Maschinenweite_Installation_verlangt_Administratorrechte()
    {
        var advice = ElevationService.Advise(_store.Find("install")!,
            new Dictionary<string, object?> { ["scope"] = "machine" });

        Assert.True(advice.Recommended);
        Assert.Equal("Elevation.MachineScope", advice.ReasonKey);
    }

    [Fact]
    public void Benutzerinstallation_verlangt_sie_nicht()
    {
        var advice = ElevationService.Advise(_store.Find("install")!,
            new Dictionary<string, object?> { ["scope"] = "user" });

        Assert.False(advice.Recommended);
    }

    [Fact]
    public void Suchen_verlangt_nie_Administratorrechte()
    {
        var advice = ElevationService.Advise(_store.Find("search")!, new Dictionary<string, object?>());

        Assert.False(advice.Recommended);
        Assert.Equal("", advice.ReasonKey);
    }

    [Fact]
    public void Administratoreinstellung_verlangt_sie_immer()
    {
        var advice = ElevationService.Advise(_store.Find("settings.set")!, new Dictionary<string, object?>());

        Assert.True(advice.Recommended);
        Assert.Equal("Elevation.Always", advice.ReasonKey);
    }

    [Fact]
    public void Einstellungen_verlangen_sie_erst_beim_Aendern_einer_Administratoreinstellung()
    {
        var plain = ElevationService.Advise(_store.Find("settings")!, new Dictionary<string, object?>());
        Assert.False(plain.Recommended);

        var admin = ElevationService.Advise(_store.Find("settings")!,
            new Dictionary<string, object?> { ["adminEnable"] = "LocalManifestFiles" });
        Assert.True(admin.Recommended);
    }

    [Fact]
    public void Dsc_set_verlangt_sie_get_nicht()
    {
        var command = _store.Find("dscv3.package")!;

        Assert.True(ElevationService.Advise(command,
            new Dictionary<string, object?> { ["dscSet"] = true }).Recommended);
        Assert.False(ElevationService.Advise(command,
            new Dictionary<string, object?> { ["dscGet"] = true }).Recommended);
    }
}

public class ExtraArgumentsTests
{
    [Theory]
    [InlineData("", new string[0])]
    [InlineData("--custom /S", new[] { "--custom", "/S" })]
    [InlineData("--log \"C:\\Mit Leerzeichen\\x.log\"", new[] { "--log", @"C:\Mit Leerzeichen\x.log" })]
    [InlineData("   mehrfach    getrennt  ", new[] { "mehrfach", "getrennt" })]
    public void Freitextfeld_wird_wie_eine_Kommandozeile_zerlegt(string input, string[] expected) =>
        Assert.Equal(expected, CommandVm.SplitExtraArguments(input));
}
