using System.IO;
using WinGetStudio.Services;
using WinGetStudio.ViewModels;
using Xunit;

namespace WinGetStudio.Tests;

/// <summary>winget-Ersatz fuer Tests: liefert vorbereitete Ausgaben statt einen Prozess zu starten.</summary>
internal sealed class FakeRunner : IWingetRunner
{
    private readonly Queue<(string Output, int ExitCode)> _responses = new();

    public List<IReadOnlyList<string>> Calls { get; } = new();

    public FakeRunner Returns(string output, int exitCode = 0)
    {
        _responses.Enqueue((output, exitCode));
        return this;
    }

    public Task<RunResult> RunAsync(IReadOnlyList<string> args, bool elevated,
                                    Action<string, LineKind> onLine, CancellationToken cancellationToken)
    {
        Calls.Add(args);

        var (output, exitCode) = _responses.Count > 0 ? _responses.Dequeue() : ("", 0);
        foreach (var line in output.Replace("\r\n", "\n").Split('\n'))
            onLine(line, LineKind.Output);

        return Task.FromResult(new RunResult(exitCode, TimeSpan.FromSeconds(1), false, output));
    }
}

public class UpdateVmTests
{
    private static string Fixture(string name)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Fixtures", name),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", name)
        };
        return File.ReadAllText(candidates.First(File.Exists));
    }

    private static (UpdateVm Vm, FakeRunner Runner) Build(FakeRunner runner)
    {
        var store = TestSchema.Load();
        return (new UpdateVm(store, new CommandLineBuilder(store), runner), runner);
    }

    [Fact]
    public async Task Echte_Upgrade_Ausgabe_wird_zu_Eintraegen()
    {
        var (vm, _) = Build(new FakeRunner().Returns(Fixture("upgrade-de.txt")));

        await vm.RefreshAsync();

        var item = Assert.Single(vm.Items);
        Assert.Equal("Stirling PDF", item.Name);
        Assert.Equal("StirlingTools.StirlingPDF", item.Id);
        Assert.Equal("2.14.0", item.CurrentVersion);
        Assert.Equal("2.14.3", item.NewVersion);
        Assert.Equal("winget", item.Source);
        Assert.Equal(UpdateStage.Ready, vm.Stage);
    }

    [Fact]
    public async Task Alles_aktuell_wenn_winget_keine_Tabelle_liefert()
    {
        var (vm, _) = Build(new FakeRunner().Returns("Es sind keine Aktualisierungen verfügbar.\r\n"));

        await vm.RefreshAsync();

        Assert.Empty(vm.Items);
        Assert.Equal(UpdateStage.UpToDate, vm.Stage);
        Assert.False(vm.HasProblem);
    }

    [Fact]
    public async Task Fehlercode_ohne_Ergebnis_wird_sichtbar_gemeldet()
    {
        // Genau der Fall, der sonst still verschwindet: kein Ergebnis und ein Fehlercode.
        var (vm, _) = Build(new FakeRunner().Returns("Die Quelle ist nicht erreichbar.\r\n", exitCode: 23));

        await vm.RefreshAsync();

        Assert.True(vm.HasProblem);
        Assert.Contains("23", vm.Problem);
        Assert.Contains("nicht erreichbar", vm.ProblemDetail);
        Assert.True(vm.ShowOutput);
    }

    [Fact]
    public async Task Erfolgreiche_Pruefung_ohne_Treffer_meldet_kein_Problem()
    {
        var (vm, _) = Build(new FakeRunner().Returns("", exitCode: 0));

        await vm.RefreshAsync();

        Assert.False(vm.HasProblem);
    }

    [Fact]
    public async Task Aktualisieren_ruft_je_ausgewaehltem_Programm_einmal_auf()
    {
        var runner = new FakeRunner()
            .Returns(Fixture("upgrade-de.txt"))
            .Returns("Erfolgreich installiert");
        var (vm, _) = Build(runner);

        await vm.RefreshAsync();
        await vm.RunAsync();

        Assert.Equal(2, runner.Calls.Count);   // einmal prüfen, einmal aktualisieren
        Assert.Equal(ItemState.Succeeded, vm.Items[0].State);
        Assert.Equal(UpdateStage.Finished, vm.Stage);
        Assert.False(vm.HasProblem);
    }

    [Fact]
    public async Task Nicht_ausgewaehlte_Programme_bleiben_unangetastet()
    {
        var (vm, runner) = Build(new FakeRunner().Returns(Fixture("upgrade-de.txt")));

        await vm.RefreshAsync();
        vm.Items[0].IsSelected = false;
        await vm.RunAsync();

        Assert.Single(runner.Calls);           // nur die Prüfung
        Assert.Equal(ItemState.Waiting, vm.Items[0].State);
    }

    [Fact]
    public async Task Ein_gescheitertes_Programm_wird_benannt_und_nicht_verschwiegen()
    {
        var runner = new FakeRunner()
            .Returns(Fixture("upgrade-de.txt"))
            .Returns("Installation fehlgeschlagen", exitCode: 5);
        var (vm, _) = Build(runner);

        await vm.RefreshAsync();
        await vm.RunAsync();

        Assert.Equal(ItemState.Failed, vm.Items[0].State);
        Assert.True(vm.HasProblem);
        Assert.Contains("Stirling PDF", vm.Problem);
    }

    [Fact]
    public async Task Die_Befehlszeile_je_Programm_folgt_den_Optionen()
    {
        var runner = new FakeRunner()
            .Returns(Fixture("upgrade-de.txt"))
            .Returns("ok");
        var (vm, _) = Build(runner);

        await vm.RefreshAsync();
        vm.Silent = true;
        vm.AcceptAgreements = true;
        await vm.RunAsync();

        var line = CommandLineBuilder.ToDisplayLine(runner.Calls[1]);
        Assert.Equal(
            "winget upgrade --id StirlingTools.StirlingPDF --exact --silent " +
            "--accept-package-agreements --accept-source-agreements --disable-interactivity",
            line);
    }

    [Fact]
    public async Task Ohne_Automatik_wird_interaktiv_aktualisiert()
    {
        var runner = new FakeRunner()
            .Returns(Fixture("upgrade-de.txt"))
            .Returns("ok");
        var (vm, _) = Build(runner);

        await vm.RefreshAsync();
        vm.Silent = false;
        vm.AcceptAgreements = false;
        await vm.RunAsync();

        var line = CommandLineBuilder.ToDisplayLine(runner.Calls[1]);
        Assert.Contains("--interactive", line);
        Assert.DoesNotContain("--silent", line);
        Assert.DoesNotContain("--accept-package-agreements", line);
    }

    [Fact]
    public async Task Einzahl_und_Mehrzahl_stimmen()
    {
        var (vm, _) = Build(new FakeRunner().Returns(Fixture("upgrade-de.txt")));
        await vm.RefreshAsync();

        Assert.Equal("1 Programm kann aktualisiert werden", vm.Headline);
        Assert.Equal("1 Programm aktualisieren", vm.RunButtonText);

        vm.Items[0].IsSelected = false;
        Assert.Equal("Nichts ausgewählt", vm.RunButtonText);
    }

    [Fact]
    public async Task Die_Zusammenfassung_der_Einstellungen_nennt_was_wirklich_gilt()
    {
        var (vm, _) = Build(new FakeRunner().Returns(Fixture("upgrade-de.txt")));
        await vm.RefreshAsync();

        Assert.Equal("ohne Rückfragen · Lizenzen angenommen", vm.OptionSummary);

        vm.IncludeUnknown = true;
        Assert.Contains("inkl. unbekannter Versionen", vm.OptionSummary);
    }
}
