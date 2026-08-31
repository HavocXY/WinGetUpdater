using WinGetStudio.Models;
using WinGetStudio.Services;
using Xunit;

namespace WinGetStudio.Tests;

public class SchemaTests
{
    private readonly SchemaStore _store = TestSchema.Load();

    [Fact]
    public void Jede_von_einem_Befehl_genannte_Option_ist_definiert()
    {
        var unknown = _store.Commands
            .SelectMany(c => new[] { c.Positional }.Concat(c.Primary).Concat(c.Advanced))
            .Concat(_store.Globals)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .Where(id => !_store.TryGetOption(id!, out _))
            .ToList();

        Assert.Empty(unknown);
    }

    [Fact]
    public void Keine_Optionsdefinition_liegt_ungenutzt_herum()
    {
        var used = _store.Commands
            .SelectMany(c => new[] { c.Positional }.Concat(c.Primary).Concat(c.Advanced))
            .Concat(_store.Globals)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet(StringComparer.Ordinal);

        var orphans = _store.Schema.Options.Keys.Where(id => !used.Contains(id)).ToList();

        Assert.Empty(orphans);
    }

    [Fact]
    public void Innerhalb_eines_Befehls_kommt_keine_Option_doppelt_vor()
    {
        foreach (var command in _store.Commands)
        {
            var ids = command.Primary.Concat(command.Advanced).ToList();
            var duplicates = ids.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.True(duplicates.Count == 0, $"{command.Id}: {string.Join(", ", duplicates)}");
        }
    }

    [Fact]
    public void Innerhalb_eines_Befehls_ist_jedes_Flag_eindeutig()
    {
        // winget belegt manche Kurzform doppelt (-h, -o, -a, -e). Innerhalb eines
        // einzelnen Befehls darf dieselbe Langform aber nur einmal vorkommen,
        // sonst erzeugt die Vorschau eine mehrdeutige Zeile.
        foreach (var command in _store.Commands)
        {
            var ids = new[] { command.Positional }
                .Concat(command.Primary).Concat(command.Advanced).Concat(_store.Globals)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct();

            var clashes = ids.Select(id => _store.Option(id!).Cli)
                             .GroupBy(c => c)
                             .Where(g => g.Count() > 1)
                             .Select(g => g.Key)
                             .ToList();

            Assert.True(clashes.Count == 0, $"{command.Id}: {string.Join(", ", clashes)}");
        }
    }

    [Fact]
    public void Jede_Option_traegt_Beschriftung_und_Beschreibung_in_beiden_Sprachen()
    {
        foreach (var (id, option) in _store.Schema.Options)
        {
            Assert.False(string.IsNullOrWhiteSpace(option.Label.De), $"{id}: Label.De fehlt");
            Assert.False(string.IsNullOrWhiteSpace(option.Label.En), $"{id}: Label.En fehlt");
            Assert.False(string.IsNullOrWhiteSpace(option.Desc.De), $"{id}: Desc.De fehlt");
            Assert.False(string.IsNullOrWhiteSpace(option.Desc.En), $"{id}: Desc.En fehlt");
        }
    }

    [Fact]
    public void Jeder_Befehl_traegt_Titel_und_Beschreibung_in_beiden_Sprachen()
    {
        foreach (var command in _store.Commands)
        {
            Assert.False(string.IsNullOrWhiteSpace(command.Title.De), $"{command.Id}: Title.De fehlt");
            Assert.False(string.IsNullOrWhiteSpace(command.Title.En), $"{command.Id}: Title.En fehlt");
            Assert.False(string.IsNullOrWhiteSpace(command.Desc.De), $"{command.Id}: Desc.De fehlt");
            Assert.False(string.IsNullOrWhiteSpace(command.Desc.En), $"{command.Id}: Desc.En fehlt");
        }
    }

    [Fact]
    public void Jeder_Befehl_gehoert_zu_einer_bekannten_Gruppe()
    {
        var groups = _store.Groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var command in _store.Commands)
            Assert.True(groups.Contains(command.Group), $"{command.Id}: Gruppe '{command.Group}' unbekannt");
    }

    [Fact]
    public void Auswahllisten_haben_Werte_und_Freitextfelder_keine()
    {
        foreach (var (id, option) in _store.Schema.Options)
        {
            if (option.ParsedKind == OptionKind.Enum)
                Assert.True(option.Values is { Count: > 0 }, $"{id}: enum ohne Werte");
            else
                Assert.True(option.Values is null, $"{id}: Werte an einer Option, die keine Auswahl ist");
        }
    }

    [Fact]
    public void Positionsargumente_sind_als_solche_markiert()
    {
        foreach (var command in _store.Commands.Where(c => c.Positional is not null))
            Assert.True(_store.Option(command.Positional!).Positional,
                        $"{command.Id}: {command.Positional} ist nicht als positional markiert");
    }

    [Fact]
    public void Befehle_mit_Administratorpflicht_sind_erfasst()
    {
        Assert.Equal(ElevationNeed.Always, _store.Find("settings.set")!.ParsedElevation);
        Assert.Equal(ElevationNeed.Always, _store.Find("source.add")!.ParsedElevation);
        Assert.Equal(ElevationNeed.Auto, _store.Find("install")!.ParsedElevation);
        Assert.Equal(ElevationNeed.Never, _store.Find("search")!.ParsedElevation);
    }
}
