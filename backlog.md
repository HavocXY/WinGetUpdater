# Backlog – Optik & UX

Drei Pakete sind bereits abgeschlossen und stehen deshalb nicht mehr hier, nur noch in der
Commit-Historie:

* „Optik & UX" — Sprachumschaltung, klickbare Update-Zeilen, Kontrast Light-Theme,
  Start-Button-Text, Begriffskollision Zurücksetzen/Zurückrollen (Commits `6edcf93`..`116969c`).
* Die xhigh-Code-Review-Nachbesserungen daraus — Speicherleck, generisches `RegisterLocalized`,
  vollständige Regressionstests, tastaturbedienbare Update-Zeile, `RefreshButtonText`-Duplikat
  (Commits `55a5962`..`7139b1c`).
* Modernisierung (Administratorrechte-Abfrage beim Start, Manrope-Schrift, eingebettete
  Vektor-Icons, Karten-Radius, Hover-Übergänge, ClearType- und Theme-Cache-Fehler aus der
  echten Nutzung) — Commits `3c8c662`..`541d752`.

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

_Aktuell leer. Das nächste Paket kommt hier rein – nach demselben Schema wie die
abgeschlossenen Pakete oben: Status, Warum, Was, Verifikation._
