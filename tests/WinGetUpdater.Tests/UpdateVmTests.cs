using System.IO;
using WinGetUpdater.Services;
using WinGetUpdater.ViewModels;
using Xunit;

namespace WinGetUpdater.Tests;

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

/// <summary>
/// winget-Ersatz, der einen Aufruf gezielt stehen lässt -
/// fuer die Pruefung, dass waehrenddessen nichts anderes laeuft.
/// </summary>
internal sealed class GateRunner : IWingetRunner
{
    private readonly Queue<Func<Task<RunResult>>> _responses = new();

    public List<IReadOnlyList<string>> Calls { get; } = new();
    public Action? Release { get; private set; }

    public GateRunner Returns(string output, int exitCode = 0)
    {
        _responses.Enqueue(() => Task.FromResult(new RunResult(exitCode, TimeSpan.Zero, false, output)));
        return this;
    }

    public GateRunner Stall()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _responses.Enqueue(() => gate.Task.ContinueWith(
            _ => new RunResult(0, TimeSpan.Zero, false, "ok"), TaskScheduler.Default));
        Release = () => gate.TrySetResult(true);
        return this;
    }

    public Task<RunResult> RunAsync(IReadOnlyList<string> args, bool elevated,
                                    Action<string, LineKind> onLine, CancellationToken cancellationToken)
    {
        Calls.Add(args);
        var next = _responses.Count > 0
            ? _responses.Dequeue()
            : () => Task.FromResult(new RunResult(0, TimeSpan.Zero, false, ""));
        return next();
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
    public async Task Zeilenumschaltung_halt_Selection_und_Zusammenfassung_konsistent()
    {
        // Der Zeilenklick in der View schaltet genau IsSelected um; der Rest - Zähler,
        // Text, Knopf-Erreichbarkeit - muss über SelectionChanged mitlaufen und aktuell
        // bleiben. Das ist der Vertrag, auf dem das anklickbare Zeilen-Border aufsetzt.
        var (vm, _) = Build(new FakeRunner().Returns(Fixture("upgrade-de.txt")));
        await vm.RefreshAsync();

        var item = vm.Items[0];
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName is not null) raised.Add(e.PropertyName); };

        Assert.True(item.IsSelected);
        Assert.Equal("1 von 1 ausgewählt", vm.SelectionText);

        item.IsSelected = !item.IsSelected;   // das macht der Klick in der Zeile

        Assert.False(item.IsSelected);
        Assert.Contains(nameof(UpdateVm.SelectionText), raised);
        Assert.Contains(nameof(UpdateVm.SelectedCount), raised);
        Assert.Contains(nameof(UpdateVm.RunButtonText), raised);
        Assert.Equal("0 von 1 ausgewählt", vm.SelectionText);
        Assert.False(vm.RunCommand.CanExecute(null));

        item.IsSelected = !item.IsSelected;   // zurück
        Assert.True(item.IsSelected);
        Assert.Equal("1 von 1 ausgewählt", vm.SelectionText);
        Assert.True(vm.RunCommand.CanExecute(null));
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

    [Fact]
    public async Task Nach_einem_Fehlschlag_ist_Zuruecksetzen_moeglich()
    {
        var runner = new FakeRunner()
            .Returns(Fixture("upgrade-de.txt"))
            .Returns("Installation fehlgeschlagen", exitCode: 5);
        var (vm, _) = Build(runner);

        await vm.RefreshAsync();
        await vm.RunAsync();

        Assert.True(vm.Items[0].CanRestore);
        Assert.True(vm.RestoreCommand.CanExecute(vm.Items[0]));
    }

    [Fact]
    public async Task Zuruecksetzen_baut_die_Befehlszeile_mit_der_Vorversion()
    {
        var runner = new FakeRunner()
            .Returns(Fixture("upgrade-de.txt"))
            .Returns("Installation fehlgeschlagen", exitCode: 5)
            .Returns("Erfolgreich installiert");
        var (vm, _) = Build(runner);

        await vm.RefreshAsync();
        await vm.RunAsync();
        await vm.RestoreAsync(vm.Items[0]);

        var line = CommandLineBuilder.ToDisplayLine(runner.Calls[^1]);
        Assert.Equal(
            "winget install --id StirlingTools.StirlingPDF --exact --version 2.14.0 " +
            "--silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
            line);
    }

    [Fact]
    public void Sprachwechsel_meldet_auch_die_vergessenen_Eigenschaften_neu()
    {
        // Vor dem Fix: OutputButtonText und SelectionText blieben beim Wechsel auf Englisch
        // deutsch, weil sie in der manuellen RefreshLanguage()-Liste fehlten.
        var (vm, _) = Build(new FakeRunner());
        var original = Localizer.Instance.Language;
        try
        {
            var raised = new List<string>();
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is not null) raised.Add(e.PropertyName);
            };

            Assert.Equal("Verlauf anzeigen", vm.OutputButtonText);   // Deutsch

            Localizer.Instance.Language = "en";

            var expected = new[]
            {
                "Headline", "SubLine", "SelectionText", "RunButtonText",
                "OptionSummary", "PreviewLine", "OutputButtonText"
            };
            foreach (var name in expected) Assert.Contains(name, raised);
            Assert.Equal("Show log", vm.OutputButtonText);           // Englisch
        }
        finally
        {
            Localizer.Instance.Language = original;
        }
    }

    [Fact]
    public void Sprachwechsel_meldet_auch_die_Optionen_von_CommandVm_neu()
    {
        // OptionVm und CommandVm durften ihre Labels sonst auch im alten Stand lassen.
        var store = TestSchema.Load();
        var command = new CommandVm(store.Find("install")!, store, new CommandLineBuilder(store), new FakeRunner());
        var option = command.PrimaryOptions.First();

        var original = Localizer.Instance.Language;
        try
        {
            var raised = new List<string>();
            option.PropertyChanged += (_, e) => { if (e.PropertyName is not null) raised.Add(e.PropertyName); };
            command.PropertyChanged += (_, e) => { if (e.PropertyName is not null) raised.Add(e.PropertyName); };

            Localizer.Instance.Language = Localizer.Instance.IsGerman ? "en" : "de";

            Assert.Contains(nameof(OptionVm.Label), raised);
            Assert.Contains(nameof(OptionVm.Description), raised);
            Assert.Contains(nameof(CommandVm.Title), raised);
            Assert.Contains(nameof(CommandVm.OptionsToggleText), raised);
        }
        finally
        {
            Localizer.Instance.Language = original;
        }
    }

    [Fact]
    public void Sprachwechsel_meldet_auch_RestoreHint_der_Zeile_neu()
    {
        var item = new UpdateItem
        {
            Name = "Stirling PDF",
            Id = "StirlingTools.StirlingPDF",
            CurrentVersion = "2.14.0",
            NewVersion = "2.14.3",
            Source = "winget"
        };

        var original = Localizer.Instance.Language;
        try
        {
            var raised = new List<string>();
            item.PropertyChanged += (_, e) => { if (e.PropertyName is not null) raised.Add(e.PropertyName); };

            Localizer.Instance.Language = Localizer.Instance.IsGerman ? "en" : "de";

            Assert.Contains(nameof(UpdateItem.RestoreHint), raised);
        }
        finally
        {
            Localizer.Instance.Language = original;
        }
    }

    [Fact]
    public async Task Ein_gelungenes_Zuruecksetzen_wird_als_solches_gemeldet()
    {
        var runner = new FakeRunner()
            .Returns(Fixture("upgrade-de.txt"))
            .Returns("Installation fehlgeschlagen", exitCode: 5)
            .Returns("Erfolgreich installiert");
        var (vm, _) = Build(runner);

        await vm.RefreshAsync();
        await vm.RunAsync();
        await vm.RestoreAsync(vm.Items[0]);

        Assert.Equal(ItemState.Restored, vm.Items[0].State);
        Assert.Contains("2.14.0", vm.Items[0].Note);
    }

    [Fact]
    public async Task Ein_fehlgeschlagenes_Zuruecksetzen_benannt_den_Exitcode()
    {
        var runner = new FakeRunner()
            .Returns(Fixture("upgrade-de.txt"))
            .Returns("Installation fehlgeschlagen", exitCode: 5)
            .Returns("Version nicht in der Quelle", exitCode: 4);
        var (vm, _) = Build(runner);

        await vm.RefreshAsync();
        await vm.RunAsync();
        await vm.RestoreAsync(vm.Items[0]);

        Assert.Equal(ItemState.Failed, vm.Items[0].State);
        Assert.Contains("4", vm.Items[0].Note);
    }

    [Fact]
    public async Task Waehrend_eines_Zuruecksetzens_ist_nichts_weiter_moeglich()
    {
        var runner = new GateRunner()
            .Returns(Fixture("upgrade-de.txt"))
            .Returns("Installation fehlgeschlagen", exitCode: 5)
            .Stall();
        var store = TestSchema.Load();
        var vm = new UpdateVm(store, new CommandLineBuilder(store), runner);

        await vm.RefreshAsync();
        await vm.RunAsync();

        var restore = vm.RestoreAsync(vm.Items[0]);
        Assert.True(vm.IsBusy);
        Assert.False(vm.RestoreCommand.CanExecute(vm.Items[0]));
        Assert.False(vm.RefreshCommand.CanExecute(null));

        runner.Release!();
        await restore;

        Assert.False(vm.IsBusy);
        Assert.True(vm.RefreshCommand.CanExecute(null));
        Assert.Equal(ItemState.Restored, vm.Items[0].State);
    }
}

public class UpdateItemTests
{
    private static UpdateItem Item(string currentVersion = "2.14.0") => new()
    {
        Name = "Stirling PDF",
        Id = "StirlingTools.StirlingPDF",
        CurrentVersion = currentVersion,
        NewVersion = "2.14.3",
        Source = "winget"
    };

    [Fact]
    public void Zuruecksetzen_ist_nach_einem_Fehlschlag_moeglich()
    {
        var item = Item();
        item.State = ItemState.Failed;
        Assert.True(item.CanRestore);
    }

    [Fact]
    public void Ohne_bekannte_Vorversion_geht_kein_Zuruecksetzen()
    {
        // winget zeigt bei unbekannten Versionen ein Gedankenstrich-Symbol an.
        var item = Item(currentVersion: "—");
        item.State = ItemState.Failed;
        Assert.False(item.CanRestore);
    }

    [Fact]
    public void Nur_ein_Fehlschlag_erlaubt_das_Zuruecksetzen()
    {
        var item = Item();
        Assert.False(item.CanRestore);

        item.State = ItemState.Succeeded;
        Assert.False(item.CanRestore);

        item.State = ItemState.Restored;
        Assert.False(item.CanRestore);
    }
}
