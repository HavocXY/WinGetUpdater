# Backlog – Optik & UX

Folgt auf das Feature-Paket „Optik & UX" (Sprachumschaltung, klickbare Update-Zeilen,
Kontrast Light-Theme, Start-Button-Text, Begriffskollision Zurücksetzen/Zurückrollen –
erledigt, Commits `6edcf93`..`116969c`). Die xhigh-Code-Review dieses Pakets hat die
folgenden fünf Punkte ergeben; sie bilden das neue Paket. Abgearbeitet wird strikt von
1 nach 5.

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

### 1. Speicherleck durch `RegisterLocalized` beheben · Bug

**Status:** Erledigt (Commit `55a5962`)

**Warum:** `ObservableObject.RegisterLocalized` (`Core.cs:33-44`) abonniert eine Closure
dauerhaft am statischen, prozesslebenslangen Event `Localizer.Instance.LanguageChanged` –
ohne Abmeldung. Für `ShellVm`, `UpdateVm` sowie die in `ShellVm._pages` app-lebenslang
gecachten `CommandVm`/`OptionVm` ist das unschädlich. `UpdateItem` dagegen wird bei **jedem**
„Jetzt nach Updates suchen"/„Erneut prüfen"-Klick neu erzeugt (`UpdateVm.RefreshAsync`, ein
`UpdateItem` pro Zeile); `ClearItems()` (`UpdateVm.cs:589-594`) hängt zwar `SelectionChanged`
aus, aber nichts vom `LanguageChanged`-Abo. Jede verworfene Zeile bleibt über den statischen
`Localizer.Instance` für immer erreichbar – ein unbegrenzt wachsendes Leck an der
meistgenutzten Aktion der Hauptansicht. Von praktisch allen zehn Suchwinkeln der
xhigh-Code-Review unabhängig bestätigt.

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

### 2. `RegisterLocalized`: manuelle Eigenschaftenlisten durch generische Benachrichtigung ersetzen

**Status:** In Arbeit

**Warum:** `RegisterLocalized(params string[] names)` verlangt weiterhin, dass jede
lokalisierte Eigenschaft von Hand pro Konstruktor aufgezählt wird (`CommandVm.cs:84-87`,
`OptionVm.cs:39`, `ShellVm.cs:58`, `UpdateVm.cs:133-136`) – exakt dieselbe Fehlerklasse, die
der ursprüngliche Sprachumschaltungs-Fix (Commit `6edcf93`) eigentlich schließen sollte, nur
von `RefreshLanguage()` in die Konstruktoren verschoben. Der eigene Dokumentationskommentar
(`Core.cs:26-31`) benennt das Risiko selbst: „wer sie vergisst, verfestigt still die alte
Sprache". Zusätzlich ist das begleitende `_localizedPropertyNames`/`_subscribedToLanguage`-
Gerüst (`Core.cs:23-24`) tote Flexibilität – alle fünf Aufrufstellen sind `sealed` und rufen
`RegisterLocalized` genau einmal auf.

**Was:**
- WPF interpretiert `PropertyChanged` mit `null`/leerem Eigenschaftsnamen als „alle
  Eigenschaften haben sich geändert". `ObservableObject` einmal auf `LanguageChanged`
  abonnieren (idealerweise dort, wo Punkt 1 das ohnehin anfasst) und
  `OnPropertyChanged((string?)null)` auslösen – keine Aufzählung mehr nötig, kein
  „Vergessen" mehr möglich.
- Damit entfallen `_localizedPropertyNames`, `_subscribedToLanguage` und alle
  `RegisterLocalized(nameof(...), ...)`-Aufrufe ersatzlos.

**Verifikation:** Bestehende Sprachumschaltungs-Tests bleiben unverändert grün; schließt
nebenbei die beiden Testlücken aus Punkt 3, weil es dann nichts mehr zu vergessen gibt.

### 3. Regressionstests für die Sprachumschaltung vervollständigen

**Status:** Erledigt (Commit `a6e13c9`)

**Warum:** Die beiden Regressionstests aus dem ursprünglichen Sprachumschaltungs-Fix
(Commit `6edcf93`) prüfen nicht alle Eigenschaften, die ihre eigene
`RegisterLocalized`-Aufrufstelle registriert:
- `Sprachwechsel_meldet_auch_die_vergessenen_Eigenschaften_neu`
  (`UpdateVmTests.cs:~384-415`) prüft `Headline, SubLine, SelectionText, RunButtonText,
  OptionSummary, PreviewLine, OutputButtonText` – `RefreshButtonText` fehlt, obwohl
  `UpdateVm.cs:133-136` es mit registriert.
- `Sprachwechsel_meldet_auch_die_Optionen_von_CommandVm_neu` (`UpdateVmTests.cs:~416-437`)
  prüft nur `Title` und `OptionsToggleText` von den acht Eigenschaften, die
  `CommandVm.cs:84-87` registriert (`Description`, `AdvancedHeader`, `GlobalHeader`,
  `ElevationNote`, `RowCountText`, `TableHint` fehlen).

Damit könnte künftig genau die Regression zurückkehren, die dieser Fix beheben sollte, ohne
dass ein Test anschlägt.

**Was:** Beide Tests um die fehlenden `Assert.Contains(...)`-Zeilen ergänzen. Erledigt sich
von selbst, sobald Punkt 2 umgesetzt ist (dann gibt es keine Liste mehr, die unvollständig
sein kann) – bis dahin eigenständig nachziehen.

**Verifikation:** Test testweise um eine der genannten Eigenschaften aus der jeweiligen
`RegisterLocalized`-Liste kürzen – Test muss dann rot werden, danach zurücknehmen.

### 4. Zeilen-Selektion in der Update-Liste: robusteres Muster erwägen

**Status:** Erledigt (Commit `3e9ef3e`) – Tab-Reihenfolge/Fokusring noch manuell im echten
Fenster gegenzuprüfen, das ließ sich für die nicht installierte Dev-Build-EXE nicht
automatisieren.

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

### 5. `RefreshButtonText`: Bedingung nicht doppelt führen

**Status:** Erledigt (Commit `7139b1c`)

**Warum:** `RefreshButtonText` (`UpdateVm.cs:188-189`) prüft `_stage == UpdateStage.Start`
per eigener Ternary, obwohl `ShowWelcome` (`UpdateVm.cs:181`) drei Zeilen darüber exakt
dieselbe Bedingung schon als Eigenschaft anbietet. Bei einer künftigen Änderung, was als
„Startzustand" zählt, muss zwingend an beiden Stellen angepasst werden – leicht zu vergessen.

**Was:** `RefreshButtonText` auf `ShowWelcome` umstellen:
`Localizer.Instance[ShowWelcome ? "Update.Check" : "Update.CheckAgain"]`.

**Verifikation:** Bestehende Tests zum Start-Button-Text (Commit `b4c503b`) bleiben
unverändert grün.
