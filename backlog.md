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

**Status:** Erledigt (Commit `6edcf93`)

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

**Status:** Erledigt (Commit `d742b2d`)

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

**Status:** Erledigt (Commit `ac69524`)

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

**Status:** Erledigt (Commit `b4c503b`)

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

**Status:** Erledigt (Commit `116969c`)

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

## Funde aus dem xhigh-Code-Review (03.09.2026, Commits `4350d086...HEAD`)

Die fünf Punkte oben sind vollständig umgesetzt; die Review deckt trotzdem sieben Punkte in
genau diesem Umbau auf. Hier als neues Paket, gleiches Schema, gleiche Reihenfolge-Regel
(erst der Bug, dann Feinschliff).

### 6. Speicherleck durch `RegisterLocalized` beheben · Bug

**Status:** Offen

**Warum:** `ObservableObject.RegisterLocalized` (`Core.cs:33-44`) abonniert eine Closure
dauerhaft am statischen, prozesslebenslangen Event `Localizer.Instance.LanguageChanged` –
ohne Abmeldung. Für `ShellVm`, `UpdateVm` sowie die in `ShellVm._pages` app-lebenslang
gecachten `CommandVm`/`OptionVm` ist das unschädlich. `UpdateItem` dagegen wird bei **jedem**
„Jetzt nach Updates suchen"/„Erneut prüfen"-Klick neu erzeugt (`UpdateVm.RefreshAsync`, ein
`UpdateItem` pro Zeile); `ClearItems()` (`UpdateVm.cs:589-594`) hängt zwar `SelectionChanged`
aus, aber nichts vom `LanguageChanged`-Abo. Jede verworfene Zeile bleibt über den statischen
`Localizer.Instance` für immer erreichbar – ein unbegrenzt wachsendes Leck an der
meistgenutzten Aktion der Hauptansicht. Von praktisch allen zehn Suchwinkeln der Review
unabhängig bestätigt.

**Was:**
- `UpdateItem` nicht mehr selbst `RegisterLocalized` aufrufen lassen. Stattdessen `UpdateVm`
  (existiert app-lebenslang) einmalig auf `LanguageChanged` abonnieren und darin über die
  *aktuellen* `Items` iterieren, um `RestoreHint` neu zu melden – die Zeilen selbst bleiben
  event-frei.
- Alternativ grundsätzlicher: `RegisterLocalized` auf ein abmeldbares Muster umstellen
  (`IDisposable` zurückgeben, oder `PropertyChangedEventManager`/`WeakEventManager` statt
  einer starken Closure), falls künftig weitere kurzlebige ViewModels lokalisierte
  Eigenschaften bekommen.

**Verifikation:** Test mit `WeakReference` auf ein `UpdateItem` nach `RefreshAsync` +
`ClearItems()` + `GC.Collect()`/`GC.WaitForPendingFinalizers()`: `IsAlive` muss `false`
werden. Bestehende Sprachumschaltungs-Tests bleiben grün.

### 7. `RegisterLocalized`: manuelle Eigenschaftenlisten durch generische Benachrichtigung ersetzen

**Status:** Offen

**Warum:** `RegisterLocalized(params string[] names)` verlangt weiterhin, dass jede
lokalisierte Eigenschaft von Hand pro Konstruktor aufgezählt wird (`CommandVm.cs:84-87`,
`OptionVm.cs:39`, `ShellVm.cs:58`, `UpdateVm.cs:133-136`) – exakt dieselbe Fehlerklasse, die
Punkt 1 eigentlich schließen sollte, nur von `RefreshLanguage()` in die Konstruktoren
verschoben. Der eigene Dokumentationskommentar (`Core.cs:26-31`) benennt das Risiko selbst:
„wer sie vergisst, verfestigt still die alte Sprache". Zusätzlich ist das begleitende
`_localizedPropertyNames`/`_subscribedToLanguage`-Gerüst (`Core.cs:23-24`) tote Flexibilität –
alle fünf Aufrufstellen sind `sealed` und rufen `RegisterLocalized` genau einmal auf.

**Was:**
- WPF interpretiert `PropertyChanged` mit `null`/leerem Eigenschaftsnamen als „alle
  Eigenschaften haben sich geändert". `ObservableObject` einmal auf `LanguageChanged`
  abonnieren (idealerweise dort, wo Punkt 6 das ohnehin anfasst) und
  `OnPropertyChanged((string?)null)` auslösen – keine Aufzählung mehr nötig, kein
  „Vergessen" mehr möglich.
- Damit entfallen `_localizedPropertyNames`, `_subscribedToLanguage` und alle
  `RegisterLocalized(nameof(...), ...)`-Aufrufe ersatzlos.

**Verifikation:** Bestehende Sprachumschaltungs-Tests bleiben unverändert grün; schließt
nebenbei die beiden Testlücken aus Punkt 8, weil es dann nichts mehr zu vergessen gibt.

### 8. Regressionstests für die Sprachumschaltung vervollständigen

**Status:** Offen

**Warum:** Die beiden aus Punkt 1 stammenden Regressionstests prüfen nicht alle
Eigenschaften, die ihre eigene `RegisterLocalized`-Aufrufstelle registriert:
- `Sprachwechsel_meldet_auch_die_vergessenen_Eigenschaften_neu`
  (`UpdateVmTests.cs:~384-415`) prüft `Headline, SubLine, SelectionText, RunButtonText,
  OptionSummary, PreviewLine, OutputButtonText` – `RefreshButtonText` fehlt, obwohl
  `UpdateVm.cs:133-136` es mit registriert.
- `Sprachwechsel_meldet_auch_die_Optionen_von_CommandVm_neu` (`UpdateVmTests.cs:~416-437`)
  prüft nur `Title` und `OptionsToggleText` von den acht Eigenschaften, die
  `CommandVm.cs:84-87` registriert (`Description`, `AdvancedHeader`, `GlobalHeader`,
  `ElevationNote`, `RowCountText`, `TableHint` fehlen).

Damit könnte künftig genau die Regression zurückkehren, die Punkt 1 beheben sollte, ohne dass
ein Test anschlägt.

**Was:** Beide Tests um die fehlenden `Assert.Contains(...)`-Zeilen ergänzen. Erledigt sich
von selbst, sobald Punkt 7 umgesetzt ist (dann gibt es keine Liste mehr, die unvollständig
sein kann) – bis dahin eigenständig nachziehen.

**Verifikation:** Test testweise um eine der genannten Eigenschaften aus der jeweiligen
`RegisterLocalized`-Liste kürzen – Test muss dann rot werden, danach zurücknehmen.

### 9. Zeilen-Selektion in der Update-Liste: robusteres Muster erwägen

**Status:** Offen

**Warum:** `OnRowToggled` (`UpdatePage.xaml.cs:69-74`) funktioniert nur, weil `CheckBox` und
der „Zurückrollen"-Button zufällig `ButtonBase` sind und `MouseLeftButtonDown` als behandelt
markieren, bevor es zum Zeilen-`Border` durchblubbert – ein stillschweigender Vertrag ohne
Testabsicherung. `CommandPage.xaml` löst dasselbe „Zeile anklicken"-Problem bereits robuster
über `DataGrid` mit `SelectionMode="Extended"`. Außerdem ist das Zeilen-`Border` nicht
`Focusable`; Tastaturnutzer erreichen die Auswahl nur über die einzelne Checkbox, nicht über
Zeilenfokus + Leertaste wie beim `DataGrid`-Vorbild.

**Was:** Prüfen, ob sich die Update-Liste ebenfalls über eine `DataGrid`/`ListBox`-Selektion
abbilden lässt (Konsistenz mit `CommandPage.xaml`, kostenlose Tastaturbedienung). Falls das
Layout dagegenspricht, mindestens `Focusable="True"` + Tastaturbehandlung am `Border`
nachrüsten, damit die Zeile selbst fokussierbar wird.

**Verifikation:** Tab-Reihenfolge manuell im echten Fenster prüfen (im Screenshot nicht
sichtbar); bestehende Klick-Tests bleiben grün.

### 10. `RefreshButtonText`: Bedingung nicht doppelt führen

**Status:** Offen

**Warum:** `RefreshButtonText` (`UpdateVm.cs:188-189`) prüft `_stage == UpdateStage.Start`
per eigener Ternary, obwohl `ShowWelcome` (`UpdateVm.cs:181`) drei Zeilen darüber exakt
dieselbe Bedingung schon als Eigenschaft anbietet. Bei einer künftigen Änderung, was als
„Startzustand" zählt, muss zwingend an beiden Stellen angepasst werden – leicht zu vergessen.

**Was:** `RefreshButtonText` auf `ShowWelcome` umstellen:
`Localizer.Instance[ShowWelcome ? "Update.Check" : "Update.CheckAgain"]`.

**Verifikation:** Bestehende Tests zu Punkt 4 bleiben unverändert grün.
