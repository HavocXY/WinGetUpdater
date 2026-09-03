# Zurücksetzen auf die Vorversion — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Nach einem fehlgeschlagenen Update in der Hauptansicht bekommt jede betroffene Zeile einen Knopf „Zurücksetzen", der die zuvor installierte Version per `winget install <id> <Version>` neu installiert.

**Architecture:** `UpdateVm` kennt neu den Befehl `install` (schon im Schema, `version` ist eine primäre Option). Der Rollback ist eine Einzel-Zeilen-Aktion auf `UpdateItem`: neue Zustände `RollingBack`/`Restored` im bestehenden `ItemState`, eine VM-Befehlsmethode `RestoreAsync` über den gleichen `CommandLineBuilder` wie alles andere, und `IsBusy` deckt auch das Zurücksetzen ab, damit „Erneut prüfen" und „Aktualisieren" nicht parallel laufen können. Die UI-Bindungen folgen dem vorhandenen DataTemplate-Muster in `UpdatePage.xaml`.

**Tech Stack:** .NET 10 / WPF, `System.Text.Json` (nur Schema), xunit im Testprojekt. Keine neuen Pakete.

**Spec:** Kein separates Spec-Dokument. Die Anforderung steht im Goal oben; der Kontext ist die README-Abschnitte „Die Hauptansicht" und „Fehler verschwinden nicht".

## Global Constraints

- **Keine NuGet-Pakete** im App-Projekt (Testprojekt ist die einzige Ausnahme, xunit). — CLAUDE.md „Dependencies"
- **Kein einziger leerer `catch`-Block**; jeder `catch` muss `ErrorLog` erwähnen. `NoSwallowedErrorsTests` lässt den Build sonst scheitern. — CLAUDE.md „No swallowed errors"
- **Oberflächentexte** (de/en) kommen in `Services/Localizer.cs` als Indexer-Tabelle — nie in XAML hart. — CLAUDE.md „Localisation split"
- **Befehlszeilen entstehen nur über `CommandLineBuilder`** gegen das Schema; immer Langform; `--disable-interactivity` bleibt gesetzt. — CLAUDE.md „The schema is the program"
- **Kein XAML-Code pro winget-Befehl.** Neue Zeilenaktionen gehören in `UpdateVm`, die Zeile bleibt Datenobjekt. — CLAUDE.md „Two modes, one engine"
- Kommentare auf Deutsch, Identifikatoren auf Englisch (bestehende Konvention).
- Commit-Nachweise auf Deutsch, kurz, beschreibend, ohne Conventional-Commit-Präfix (siehe `git log`).
- Tests: `dotnet test tests/WinGetUpdater.Tests/WinGetUpdater.Tests.csproj`; Testnamen auf Deutsch (bestehender Stil in `UpdateVmTests.cs`).
- Zustände tragen immer eigenes Glyph **und** Wort; Farbe allein signalisiert nie. — CLAUDE.md „Colour rules"

---

### Task 1: Zustände und Bedingung in der Update-Zeile

**Files:**
- Modify: `src/WinGetUpdater/ViewModels/UpdateVm.cs` (Enum `ItemState` Zeile ~6, Klasse `UpdateItem` Zeile ~9-70)
- Modify: `src/WinGetUpdater/Services/Localizer.cs` (Tabelle, nach dem `Update.SeeLog`-Eintrag ~Zeile 155)
- Test: `tests/WinGetUpdater.Tests/UpdateVmTests.cs` (neue Klasse `UpdateItemTests` ans Dateiende)

**Interfaces:**
- Consumes: nichts Neues.
- Produziert: `ItemState.RollingBack`, `ItemState.Restored`; `UpdateItem.IsRestored`, `UpdateItem.CanRestore`, `UpdateItem.RestoreHint`; Localizer-Keys `Update.Restore`, `Update.RestoreHint`, `Update.Restoring`, `Update.RestoredNote`, `Update.RestoreFailed`. Task 2 und 3 hängen von genau diesen Namen ab.

- [ ] **Step 1: Failing Tests schreiben**

An `tests/WinGetUpdater.Tests/UpdateVmTests.cs` ans Dateiende anhängen:

```csharp
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
```

- [ ] **Step 2: Test läuft und schlägt fehl**

Run: `dotnet test tests/WinGetUpdater.Tests/WinGetUpdater.Tests.csproj`

Expected: Build-Fehler — `UpdateItem` hat weder `CanRestore` noch `ItemState.Restored`.

- [ ] **Step 3: Minimale Implementierung**

In `src/WinGetUpdater/ViewModels/UpdateVm.cs`:

Enum (Zeile 6) ersetzen:

```csharp
public enum ItemState { Waiting, Running, Succeeded, Failed, RollingBack, Restored }
```

In `UpdateItem` die bestehende Zeile

```csharp
public bool IsRunning => _state == ItemState.Running;
```

ersetzen durch:

```csharp
public bool IsRunning => _state is ItemState.Running or ItemState.RollingBack;
```

und nach `IsFailed` ergänzen:

```csharp
public bool IsRestored => _state == ItemState.Restored;

/// <summary>Ob die zuvor installierte Version bekannt ist - sonst lässt sich nichts zurücksetzen.</summary>
public bool HasPreviousVersion =>
    !string.IsNullOrWhiteSpace(CurrentVersion) && CurrentVersion != "—";

/// <summary>Ob der „Zurücksetzen"-Knopf an dieser Zeile erscheinen darf.</summary>
public bool CanRestore => _state == ItemState.Failed && HasPreviousVersion;

/// <summary>Knopf-Hinweis mit der Version, die neu installiert würde.</summary>
public string RestoreHint => Localizer.Instance.Format("Update.RestoreHint", CurrentVersion);
```

Im `State`-Setter die Raise-Liste erweitern (bestehende drei Zeilen ergänzen):

```csharp
        if (!Set(ref _state, value)) return;
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(IsRestored));
        OnPropertyChanged(nameof(CanRestore));
```

In `src/WinGetUpdater/Services/Localizer.cs` nach dem `Update.SeeLog`-Eintrag einfügen:

```csharp
        ["Update.Restore"] = ("Zurücksetzen", "Revert"),
        ["Update.RestoreHint"] = ("Installiert die zuletzt installierte Version ({0}) erneut.",
                                  "Reinstalls the last installed version ({0})."),
        ["Update.Restoring"] = ("wird zurückgesetzt …", "rolling back …"),
        ["Update.RestoredNote"] = ("zurückgesetzt auf {0}", "rolled back to {0}"),
        ["Update.RestoreFailed"] = ("Zurücksetzen fehlgeschlagen (Code {0})",
                                    "rollback failed (code {0})"),
```

- [ ] **Step 4: Tests laufen und bestehen**

Run: `dotnet test tests/WinGetUpdater.Tests/WinGetUpdater.Tests.csproj`

Expected: alle Tests grün, darunter die drei neuen `UpdateItemTests`.

- [ ] **Step 5: Commit**

```bash
git add src/WinGetUpdater/ViewModels/UpdateVm.cs src/WinGetUpdater/Services/Localizer.cs tests/WinGetUpdater.Tests/UpdateVmTests.cs
git commit -m "Update-Zeile kennt den Zustand 'zurückgesetzt'"
```

---

### Task 2: Zurücksetzen-Ablauf in der UpdateVm

**Files:**
- Modify: `src/WinGetUpdater/ViewModels/UpdateVm.cs` (Klasse `UpdateVm`: Felder ~Zeile 90, Konstruktor ~Zeile 100, `IsBusy` ~Zeile 148, Ablauf-Bereich ~Zeile 380)
- Test: `tests/WinGetUpdater.Tests/UpdateVmTests.cs` (neue Klasse `GateRunner` neben `FakeRunner`, neue Facts in `UpdateVmTests`)

**Interfaces:**
- Consumes: Task 1 — `ItemState.RollingBack`, `ItemState.Restored`, `UpdateItem.CanRestore`; Localizer-Keys aus Task 1.
- Produziert: `UpdateVm.RestoreCommand` (`RelayCommand`, Parameter: `UpdateItem`), `UpdateVm.RestoreAsync(UpdateItem?)` (public, `Task`), verändertes `UpdateVm.IsBusy` (true auch während eines RollingBack). Task 3 bindet `RestoreCommand` per `CommandParameter` an den Zeilenknopf.

- [ ] **Step 1: Failing Tests schreiben**

In `tests/WinGetUpdater.Tests/UpdateVmTests.cs` neben `FakeRunner` (nach dessen Klasse) die Klasse `GateRunner` einfügen:

```csharp
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
        // TrySetResult() ohne Parameter existiert nicht bei TaskCompletionSource<bool>.
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
```

In die bestehende Klasse `UpdateVmTests` (vor dem schließenden `}`) die Facts einfügen:

```csharp
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
```

- [ ] **Step 2: Tests laufen und schlagen fehl**

Run: `dotnet test tests/WinGetUpdater.Tests/WinGetUpdater.Tests.csproj`

Expected: Build-Fehler — `UpdateVm` hat weder `RestoreCommand` noch `RestoreAsync`.

- [ ] **Step 3: Implementierung**

In `src/WinGetUpdater/ViewModels/UpdateVm.cs`, Klasse `UpdateVm`:

Neben dem bestehenden Feld `private readonly CommandSpec _upgrade;` ergänzen:

```csharp
    private readonly CommandSpec _install;
```

Im Konstruktor, direkt nach der `_upgrade`-Zeile:

```csharp
        _install = store.Find("install")
                   ?? throw new InvalidOperationException("Der Befehl 'install' fehlt im Schema.");
```

und neben `RunCommand = …` den Befehl:

```csharp
        RestoreCommand = new RelayCommand(
            p => _ = RestoreAsync(p as UpdateItem),
            p => p is UpdateItem i && i.CanRestore && !IsBusy);
```

Neben `public AsyncRelayCommand RunCommand { get; }` die Eigenschaft:

```csharp
    public RelayCommand RestoreCommand { get; }
```

Die Eigenschaft `IsBusy` ersetzen (Rollback zählt dazu, sonst würde „Erneut prüfen"
parallel zum Zurücksetzen laufen und den gemeinsamen CancellationToken überschreiben):

```csharp
    public bool IsBusy =>
        _stage is UpdateStage.Checking or UpdateStage.Updating
        || Items.Any(i => i.State == ItemState.RollingBack);
```

In den Bereich „Ablauf" (nach `Cancel()`) die Methode:

```csharp
    /// <summary>
    /// Installiert fuer ein einzelnes fehlgeschlagenes Programm die zuvor installierte
    /// Version neu. Die Version kommt aus der Update-Liste und ist damit genau die,
    /// die vor dem fehlgeschlagenen Update installiert war.
    /// </summary>
    public async Task RestoreAsync(UpdateItem? item)
    {
        if (item is null || !item.CanRestore || IsBusy) return;

        item.State = ItemState.RollingBack;
        item.Note = Localizer.Instance["Update.Restoring"];
        Append($"── {item.Name}: Version {item.CurrentVersion} wird wiederhergestellt ──", LineKind.Info);
        RaiseBusyState();

        _cancellation = new CancellationTokenSource();
        try
        {
            var result = await _runner.RunAsync(
                BuildRestoreArgs(item), _elevated, Append, _cancellation.Token);

            if (result.Canceled)
            {
                item.State = ItemState.Failed;
                item.Note = Localizer.Instance["Update.ItemCanceled"];
            }
            else if (result.Succeeded)
            {
                item.State = ItemState.Restored;
                item.Note = Localizer.Instance.Format("Update.RestoredNote", item.CurrentVersion);
            }
            else
            {
                item.State = ItemState.Failed;
                item.Note = Localizer.Instance.Format("Update.RestoreFailed", result.ExitCode);
            }
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            RaiseBusyState();
        }
    }

    /// <summary>Argumente fuer die Neuinstallation der Vorversion - gleiche Optionen wie der Update-Lauf.</summary>
    private List<string> BuildRestoreArgs(UpdateItem item) =>
        _builder.Build(_install, new Dictionary<string, object?>
        {
            ["id"] = item.Id,
            ["exact"] = true,
            ["version"] = item.CurrentVersion,
            ["silent"] = _silent ? true : null,
            ["interactive"] = _silent ? null : true,
            ["acceptPackageAgreements"] = _acceptAgreements ? true : null,
            ["acceptSourceAgreements"] = _acceptAgreements ? true : null,
            ["disableInteractivity"] = true
        });

    /// <summary>
    /// Busy bedeutet auch „ein Zurücksetzen läuft". Deshalb melden alle Befehle hier
    /// ihre Erreichbarkeit, wenn sich dieser Teil umdreht.
    /// </summary>
    private void RaiseBusyState()
    {
        OnPropertyChanged(nameof(IsBusy));
        RefreshCommand.RaiseCanExecuteChanged();
        RunCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        RestoreCommand.RaiseCanExecuteChanged();
    }
```

Kein `try/catch` um den Runner-Aufruf: `WingetRunner` fängt selbst ab und liefert
immer ein `RunResult` — ein eigener `catch` wäre hier ein geschluckter Fehler.

- [ ] **Step 4: Alle Tests laufen und bestehen**

Run: `dotnet test tests/WinGetUpdater.Tests/WinGetUpdater.Tests.csproj`

Expected: alle Tests grün (bestehende + 5 neue). Bestehende Tests wie
`Die_Befehlszeile_je_Programm_folgt_den_Optionen` müssen unverändert weiterlaufen —
die `IsBusy`-Erweiterung darf keinen bestehenden Zustand ändern.

- [ ] **Step 5: Commit**

```bash
git add src/WinGetUpdater/ViewModels/UpdateVm.cs tests/WinGetUpdater.Tests/UpdateVmTests.cs
git commit -m "Fehlgeschlagene Programme lassen sich auf die Vorversion zurücksetzen"
```

---

### Task 3: Knopf in der Update-Liste + Dokumentation

**Files:**
- Modify: `src/WinGetUpdater/Views/UpdatePage.xaml` (DataTemplate `UpdateItemTemplate`, StackPanel in Grid.Column 4, ~Zeile 60-80)
- Modify: `README.md` (Tabelle „Die Hauptansicht" ~Zeile 30; Abschnitt „Bekannte Grenzen")

**Interfaces:**
- Consumes: Task 1 — `CanRestore`, `IsRestored`, `RestoreHint`; Localizer-Key `Update.Restore`. Task 2 — `RestoreCommand`.
- Produziert: sichtbares Ergebnis. Keine neuen Programme.

- [ ] **Step 1: Zustands-Glyph „↩" ergänzen**

In `src/WinGetUpdater/Views/UpdatePage.xaml`, im `UpdateItemTemplate`, zwischen dem
`✕`-TextBlock (`IsFailed`) und dem `●`-TextBlock (`IsRunning`) einfügen:

```xml
              <TextBlock Text="↩" FontSize="15" Margin="0,0,6,0" VerticalAlignment="Center"
                         Foreground="{DynamicResource State.Ok}"
                         Visibility="{Binding IsRestored, Converter={StaticResource Vis}}" />
```

Das Glyph ist wie die anderen Zustände ein eigenes Zeichen mit eigener Bedeutung
(zurückgesetzt) und nutzt die Erfolgsfarbe, weil der gewollte Zustand erreicht ist.

- [ ] **Step 2: Zurücksetzen-Knopf an die Zeile hängen**

Im selben StackPanel, nach dem `Note`-TextBlock (letztes Element vor dem schließenden
`</StackPanel>`) einfügen:

```xml
              <Button Style="{StaticResource Btn.Ghost}" FontSize="12" Padding="8,3" Margin="10,0,0,0"
                      VerticalAlignment="Center"
                      Command="{Binding DataContext.RestoreCommand,
                                  RelativeSource={RelativeSource AncestorType=UserControl}}"
                      CommandParameter="{Binding}"
                      Content="{Binding [Update.Restore], Source={x:Static svc:Localizer.Instance}}"
                      ToolTip="{Binding RestoreHint}"
                      Visibility="{Binding CanRestore, Converter={StaticResource Vis}}" />
```

Der Knopf ist also nur in einem Fall sichtbar: Zeile fehlgeschlagen **und** Vorversion
bekannt. `CanRestore` ist false während `RollingBack`/`Restored`, und `RestoreCommand`
verweigert zusätzlich, wenn die Vm beschäftigt ist.

- [ ] **Step 3: Build + Tests + Selftest**

```bash
dotnet build src/WinGetUpdater/WinGetUpdater.csproj
dotnet test tests/WinGetUpdater.Tests/WinGetUpdater.Tests.csproj
dotnet run --project src/WinGetUpdater -- --selftest
```

Expected: Build grün, alle Tests grün, Selftest Exitcode 0.

- [ ] **Step 4: Screenshot der Hauptansicht als Regressionscheck**

```bash
dotnet run --project src/WinGetUpdater -- --screenshot shot-updates.png
```

Bild ansehen (es liegt im aktuellen Verzeichnis): Die Hauptansicht muss unverändert
aussiehen — in einem fertigen Check-Zustand gibt es keine fehlgeschlagene Zeile, also
ist weder `↩` noch der Knopf zu sehen. Danach `shot-updates.png` wieder löschen
(`del shot-updates.png`), damit keine Artefakte im Repo liegen.

- [ ] **Step 5: README aktualisieren**

In `README.md`, Tabelle „Die Hauptansicht", die Zeile

```
| **Wenn etwas schiefgeht** | Ein roter Kasten benennt das Programm und den Exitcode. Nichts verschwindet stillschweigend. |
```

ersetzen durch:

```
| **Wenn etwas schiefgeht** | Ein roter Kasten benennt das Programm und den Exitcode. An jeder fehlgeschlagenen Zeile steht ein Knopf „Zurücksetzen", der die zuvor installierte Version neu installiert. Nichts verschwindet stillschweigend. |
```

Im Abschnitt „Bekannte Grenzen" einen weiteren Aufzählungspunkt ergänzen:

```markdown
* Das Zurücksetzen setzt die zuletzt installierte Version neu. Ist diese Version nicht
  bekannt — winget zeigt dann einen Gedankenstrich an — ist der Knopf an dieser Zeile
  nicht vorhanden, und es bleibt die manuelle Option unter „Alle Befehle → install".
```

- [ ] **Step 6: Commit**

```bash
git add src/WinGetUpdater/Views/UpdatePage.xaml README.md
git commit -m "Zurücksetzen-Knopf in der Update-Liste, README ergänzt"
```

---

## Self-Review (erledigt)

**1. Spec coverage:** Goal = Rollback-Knopf bei Fehlschlag. Task 1 = Zustände/Bedingung,
Task 2 = Ablauf + Befehlszeile + Sperre, Task 3 = UI + Doku. Abdeckung vollständig;
keine Aufgabe ohne Ziel, kein Ziel ohne Aufgabe.

**2. Placeholder-Scan:** Keine TBDs, keine „analog zu Task N", alle Code-Blöcke
vollständig, alle Referenzierten existieren in einer früheren Aufgabe.

**3. Typ-Konsistenz:** `CanRestore` / `IsRestored` / `RestoreHint` (UpdateItem),
`RestoreCommand` / `RestoreAsync(UpdateItem?)` / `RaiseBusyState` (UpdateVm),
`GateRunner` (Tests) — in allen Tasks identisch verwendet. Die erwartete
Befehlszeile im Test stimmt mit der Optionsreihenfolge aus
`winget-schema.json` (install: `id, exact, version, … silent …` + advanced
`acceptPackageAgreements, acceptSourceAgreements` + global `disableInteractivity`) überein.

## Bewusst nicht im Scope

- **Kein Abbrechen des laufenden Rollbacks über den globalen Abbrechen-Knopf** —
  `IsBusy` zeigt währenddessen den Abbrechen-Knopf (bestehendes Verhalten), und
  `RestoreAsync` behandelt `Canceled` wie `RunAsync`. Damit ist es doch abgedeckt.
- **Kein „Zurücksetzen" für mehrere Zeilen auf einmal** — YAGNI; jede Zeile hat ihren
  Knopf, und die Sperre verhindert Kollisionen.
- **Kein Test für den erhöhten Pfad** — wie `RunAsync` braucht auch `RestoreAsync`
  eine echte UAC-Bestätigung dafür; das ist die bekannte Grenze aus der README und
  ändert sich mit diesem Feature nicht.
- **Kein Schema-Änderung** — `install`, `id`, `exact`, `version` und die
  Accept-Schalter sind bereits da; `Check-Schema.ps1` bleibt unberührt.
