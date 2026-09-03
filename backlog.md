# Backlog – Optik & UX

Folgt auf dem Feature-Paket „Zurücksetzen auf die Vorversion" (erledigt). Die Punkte sind
nach Reihenfolge sortiert: erst der Bug, dann der größte Bediengewinn, dann die Feinschliff-
Punkte. Abgearbeitet wird strikt von 1 nach 5.

## So arbeiten wir ab

1. **Ein Branch für das ganze Paket:** `bionic/optik-ux-paket` (Präfix `bionic/`, nicht auf `main` entwickeln).
2. **Pro Punkt:** zuerst Test(en) schreiben, dann ändern, dann `dotnet test` grün. Kleine,
   beschreibende Commits auf Deutsch, ohne Conventional-Commit-Präfix.
3. **Status hier pflegen:** `Offen → In Arbeit → Erledigt (Commit)`. Die Datei gehört zu jedem
   Commit dazwischen, wenn sich der Status ändert.
4. **Jeden Punkt visuell prüfen:** `.\build.ps1 -SkipTests` bauen, dann
   `WinGetUpdater.exe --screenshot <name>.png` (ggf. `--light --english` für die zweite Ansicht)
   und das PNG anschauen. Farbveränderungen zusätzlich **nachmessen** (Kontrast ≥ 4,5:1,
   beide Themes, gleiche 26 Keys).
5. **Nicht anfassen:** leere `catch`-Blöcke, NuGet-Pakete im App-Projekt, XAML pro Befehl
   (Schema ist das Programm), `--force`/`--accept-*` automatisieren.
6. **Abschluss:** alle 5 Punkte erledigt → lokal nach `main` mergen → `git push` **nur auf
   ausdrückliche Aufforderung**.

## Die Punkte

### 1. Sprachumschaltung komplett machen · Bug

**Status:** Offen

**Warum:** Nach dem Wechsel auf Englisch bleibt „Verlauf anzeigen" deutsch – alles andere ist
übersetzt (reproduzierbar, Screenshot `main-en.png`). Ursache: Jede ViewModel führt eine
manuelle „erneut auswerfen"-Liste in `RefreshLanguage()`, und `OutputButtonText` fehlt dort.
Jede neu hinzukommende lokalisierte Eigenschaft bricht stillschweigend.

**Was:**
- `Services/Localizer.cs`: neues Event `LanguageChanged`, wird am Ende des `Language`-Setters
  ausgelöst (nach `OnPropertyChanged("Item[]")`, damit die neue Sprache schon aktiv ist).
- `ViewModels/ObservableObject.cs` (bzw. dort, wo die Basisklasse liegt): Helper
  `RegisterLocalized(params string[] names)` – abonniert das Event und feuert bei Sprachwechsel
  `PropertyChanged` für genau diese Eigenschaften.
- `UpdateVm`: `RegisterLocalized(...)` für alle lokalisierten Eigenschaften, **inklusive**
  `OutputButtonText` und `SelectionText`; bisherige `RefreshLanguage()` entfällt.
- `CommandVm`/`OptionVm`: `RefreshLanguage()` durch `RegisterLocalized(...)` ersetzen
  (`Title`, `Description`, `AdvancedHeader`, `GlobalHeader`, `ElevationNote`, `RowCountText`,
  `OptionsToggleText`, `TableHint`, `Label` …).
- `UpdateItem`: `RestoreHint` registrieren.
- `ShellVm.ToggleLanguage()`: manuellen Fan-out (`RefreshLanguage`-Aufrufe) streichen,
  `BuildNavigation()` bleibt.

**Verifikation:** Neuer Test „Sprachwechsel wertet `OutputButtonText` neu aus" (vorher EN setzen,
`PropertyChanged` abhören); Screenshot EN zeigt durchgehend „Show log".

### 2. Ganze Zeilen in der Update-Liste anklickbar machen

**Status:** Offen

**Warum:** Heute wählt man nur über die 16×16-Checkbox an. Kleiner Klickbereich, fühlt sich
träge an – genau das Gegenteil von „einfach zu bedienen".

**Was:**
- `Views/UpdatePage.xaml`: Das Zeilen-`Border` im `UpdateItemTemplate` bekommt
  `MouseLeftButtonDown` (bubbling) + `Cursor="Hand"`. Klick auf Name, Version oder Leerraum
  schaltet `IsSelected` um.
- `Views/UpdatePage.xaml.cs`: Handler `OnRowToggled` – liest `UpdateItem` aus dem
  `DataContext` und toggle `IsSelected`. Buttons/CheckBox in der Zeile sind `ButtonBase` und
  markieren die Maus-Events als behandelt, sie bleiben davon unberührt (inkl. „Zurückrollen").
- Hover-Zustand: Zeilenhintergrund wird bei `IsMouseOver` zu `Bg.Hover` (Style-Trigger,
  beide Themes haben die Key bereits).

**Verifikation:** Test, dass Toggle `SelectionChanged` feuert und `SelectionText` aktuell bleibt;
Screenshots dunkel/hell zeigen Zeile unverändert (Hover per Mausklick im echten Fenster prüfen).

### 3. Kontrast Light-Theme: `Fg.Muted` auf Sidebar anheben

**Status:** Offen

**Warum:** `Fg.Muted #71706A` auf `Bg.Sidebar #F2F0E9` misst **4,36:1** – unter dem
4,5:1-Minimum. Betrifft die Sidebar-Fußzeile („39 Befehle") und die Gruppen-Kopfzeilen.
CLAUDE.md behauptet, alle Paare lägen ≥ 4,5:1 – stimmt fast, aber nicht hier.

**Was:**
- `Resources/Themes/Light.xaml`: `Fg.Muted` minimal abdunkeln (warmer Grauton beibehalten,
  z. B. Richtung `#66655F`). Nicht `Bg.Sidebar` ändern – das würde die ganze Seitenfläche
  betreffen.
- Kommentar in `Light.xaml` um den gemessenen Wert ergänzen.
- Dark.xaml bleibt unverändert (dort alles ≥ 4,7:1).

**Verifikation:** Alle Light-Paare neu nachmessen (Kleinstskript oder Rechnung), jede Zeile
≥ 4,5:1; Light-Screenshot der Expertenansicht.

### 4. Startzustand: korrekten Button-Text zeigen

**Status:** Offen

**Warum:** Vor der ersten Prüfung steht oben rechts „Erneut prüfen", obwohl noch nichts
geprüft wurde. Der Localizer enthält dafür bereits den ungenutzten Key
`Update.Check` = „Jetzt nach Updates suchen" / „Check for updates now".

**Was:**
- `UpdateVm`: neue Eigenschaft `RefreshButtonText` →
  `Stage == Start ? ["Update.Check"] : ["Update.CheckAgain"]`; der `Stage`-Setter feuert sie mit.
- `Views/UpdatePage.xaml`: Kopfleisten-Button bindet `RefreshButtonText` statt des
  festen `[Update.CheckAgain]`.
- Mit Punkt 1: in `RegisterLocalized(...)` aufnehmen.

**Verifikation:** Test für beide Stufen in DE und EN (Start → „Jetzt nach Updates suchen",
danach → „Erneut prüfen"/„Check again").

### 5. Begriffskollision „Zurücksetzen" auflösen

**Status:** Offen

**Warum:** Zwei unterschiedliche Aktionen tragen im Deutschen denselben Namen: der
Rollback-Button in der Update-Zeile (`Update.Restore`) und der Header-Button der
Befehlsseite, der die Eingabefelder leert (`Options.Reset`). Im Englischen sind sie bereits
unterschieden („Revert"/„Reset").

**Was:**
- Rollback-Button wird präzise: `Update.Restore` → „Zurückrollen" / „Roll back",
  zugehörige Keys `Update.Restoring`, `Update.RestoredNote`, `Update.RestoreFailed`
  konsistent mit anpassen. `Update.RestoreHint` bleibt (er erklärt die Wirkung).
- `Options.Reset` („Zurücksetzen"/„Reset") bleibt – das ist der Standard-Begriff fürs
  Zurücksetzen von Formularen.
- `README.md` und eventuelle Testnamen/Texte, die den alten Namen zitieren, mit anpassen
  (historische Plans unter `docs/` bleiben so, wie sie sind).

**Verifikation:** Tests grün, README ohne veraltete Bezeichnung, Screenshot der
Update-Zeile mit fehlgeschlagenem Eintrag (ggf. per Test-Fixture nur im Test, nicht erzwingbar
headless).
