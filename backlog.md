# Backlog – Optik & UX

Zwei Pakete sind bereits abgeschlossen und stehen deshalb nicht mehr hier, nur noch in der
Commit-Historie:

* „Optik & UX" — Sprachumschaltung, klickbare Update-Zeilen, Kontrast Light-Theme,
  Start-Button-Text, Begriffskollision Zurücksetzen/Zurückrollen (Commits `6edcf93`..`116969c`).
* Die xhigh-Code-Review-Nachbesserungen daraus — Speicherleck, generisches `RegisterLocalized`,
  vollständige Regressionstests, tastaturbedienbare Update-Zeile, `RefreshButtonText`-Duplikat
  (Commits `55a5962`..`7139b1c`).

Erkenntnisse daraus, die über den jeweiligen Commit hinaus gelten, stehen in `CLAUDE.md`
(„Localisation split", „Non-obvious invariants") statt hier — das Backlog hält nur an, was
noch zu tun ist.

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
6. **Abschluss:** alle Punkte des Pakets erledigt → lokal nach `main` mergen → `git push`
   **nur auf ausdrückliche Aufforderung**.

## Die Punkte

### 1. Emoji durch eingebettete Vektor-Icons ersetzen

**Status:** Erledigt (Commit `8f32820`)

**Warum:** Emoji-Glyphen (🛡 ⚠ ✓ ✕ ● ↩ ◐) sehen je nach installierter Segoe-Emoji-Version
unterschiedlich aus und lassen sich nicht in den Theme-Farben einfärben — einer der
meistgenannten Sofort-Modernisierungsschritte laut Recherche (siehe Mockup-Artefakt vom
04.09.2026, „Icon-Modernisierung").

**Was:**
- `Resources/Icons.xaml`: sieben Lucide-Pfaddaten (ISC-Lizenz, wie Manrope ohne Paket
  eingebettet) als benannte `Geometry`-Ressourcen plus ein `Icon`-Style für `Path`
  (Stretch=Uniform, 2px Strich, runde Enden/Ecken, keine Füllung).
- Alle 15 Fundstellen in `ElevationPromptWindow`, `ShellWindow`, `UpdatePage`,
  `CommandPage` umgestellt.
- Theme-Umschalter zeigt jetzt Sonne/Mond je nach *Ziel*-Zustand (neue
  `ShellVm.IsDarkTheme`-Eigenschaft) statt eines neutralen Halbkreises immer gleich.
- „Läuft"-Punkt in der Update-Zeile ist jetzt ein echtes `Ellipse`-Element statt des
  Unicode-Zeichens „●".

**Verifikation:** Screenshots dunkel/hell (Hauptansicht, „Alle Befehle → Deinstallieren"
für Warn-Icons, Fehlerprotokoll für den Schließen-Button) zeigen alle Symbole scharf und
korrekt eingefärbt. `Icon.RotateCcw` (Zurückrollen-Zustand einer Zeile) ließ sich ohne
echten fehlgeschlagenen Lauf nicht screenshotten - Pfaddaten nach derselben Methode wie
die sechs sichtbar geprüften Icons hergeleitet, Build kompiliert die Geometrie-Syntax
bereits zur Baml-Übersetzungszeit.

### 2. Karten-/Listen-Radius anheben

**Status:** Erledigt (Commit `c0366f7`)

**Warum:** Aktuelle SaaS-Referenzen (Linear, Raycast) liegen für Flächen (Karten, Listen)
meist bei 8–12px Eckenradius; die App liegt bei 6px. Buttons bleiben bewusst kleiner
(5–7px), damit klickbare Elemente kompakter wirken als Flächen — dieser Unterschied ist
selbst Teil des modernen Musters, nicht nur ein einzelner Wert.

**Was:** `Card`-Style in `Controls.xaml` (`CornerRadius`) sowie die Radien der
Update-Liste/Ergebnisliste (`UpdatePage.xaml`, `CommandPage.xaml`, wo hart codiert statt
über den Style) auf 8-9px anheben. `Radius` (Buttons, 5px) unangetastet lassen.

**Verifikation:** Screenshots aller Karten-tragenden Ansichten dunkel/hell; nichts wirkt
merklich runder als die Buttons daneben.

### 3. Hover-Übergänge auf Buttons/Zeilen

**Status:** In Arbeit

**Warum:** Aktuell wechselt der Hover-Hintergrund hart, ohne Übergang - ein kurzer
Farb-Übergang (~120 ms) ist reines WPF (`ColorAnimation` im `Trigger`), braucht kein
Paket, und ist einer der kleinen Unterschiede, die eine Oberfläche „lebendig" wirken
lassen statt statisch.

**Was:** `EnterActions`/`ExitActions` mit `ColorAnimation` auf die Button- und
Zeilen-Hover-Trigger in `Controls.xaml` (Basis-Button-Style, `Btn.Primary`) und
`UpdatePage.xaml` (Zeilen-Border) ergänzen.

**Verifikation:** Manuell im echten Fenster geprüft (ein Screenshot kann eine Animation
nicht zeigen) - Übergang fühlbar, aber nicht behäbig; bestehende Klick-Tests bleiben grün.
