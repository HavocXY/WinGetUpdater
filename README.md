# WinGetUpdater

A Windows desktop front end for `winget`. One thing on the surface — **update installed
programs** — and everything `winget` can do underneath.

[![License: MIT](https://img.shields.io/github/license/HavocXY/WinGetUpdater)](LICENSE)
[![Latest release](https://img.shields.io/github/v/release/HavocXY/WinGetUpdater)](https://github.com/HavocXY/WinGetUpdater/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/HavocXY/WinGetUpdater/total)](https://github.com/HavocXY/WinGetUpdater/releases)
![Platform](https://img.shields.io/badge/platform-Windows%2010%20%2F%2011-0078D6)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)

**[English](#english)** · **[Deutsch](#deutsch)**

![Main view](docs/01-updates.png)

---

## English

WinGetUpdater checks for updates the moment it opens. What you do: tick boxes, press the
button. No menu, no command line, no prior knowledge required.

Need more? Switch to **All commands** at the top and get the complete winget surface:
**39 executable commands with 99 options**, every single one a real control. That is not a
claim — it is checked; see [Completeness](#completeness).

### Contents

- [Features](#features)
- [The main view](#the-main-view)
- [Errors are never swallowed](#errors-are-never-swallowed)
- [All commands](#all-commands)
- [Installation](#installation)
- [Completeness](#completeness)
- [Architecture](#architecture)
- [What's not included](#whats-not-included)
- [Known limitations](#known-limitations)
- [Contributing](#contributing)
- [License](#license)

### Features

- **Updates, one click** — the check runs automatically on launch; you tick boxes and press
  one button.
- **Program by program, not all-or-nothing** — each update runs and reports on its own, so one
  failure is traceable to exactly one line instead of hiding behind nine successes.
- **One-click rollback** — a failed row gets a "Roll back" button that reinstalls the
  previously installed version.
- **The full winget CLI, when you want it** — 39 commands, 99 options, every one a real
  control, none of it hidden behind a free-text field.
- **Schema-verified completeness** — a script diffs the UI's options against `winget --help`
  for every command; the build fails the moment they disagree.
- **Nothing fails silently** — every caught exception is logged; a test scans the source for
  empty `catch` blocks and fails the build if it finds one.
- **German and English**, switchable at runtime, no restart required.
- **Accessible by default** — every foreground/background color pairing is measured at ≥4.5:1
  contrast; state is never conveyed by color alone.
- **Single-file EXE** — self-contained, no .NET install required, ~59 MB.

---

## The main view

| | |
|---|---|
| **On open** | The check starts immediately. You see either a list or "Everything is up to date." |
| **The list** | Program name, package ID, old → new version, source. Everything pre-selected, each row deselectable. |
| **The button** | Labeled with what it does: "Update 3 programs." Not "OK." |
| **While it runs** | Every row gets its own state: running, updated, failed. |
| **When something fails** | A red box names the program and the exit code. Every failed row gets a "Roll back" button that reinstalls the previously installed version. Nothing disappears quietly. |

Next to the button, plain text states what currently applies — "no prompts · licenses
accepted." To change that, expand **Options**. Every switch there gets a sentence explaining
what it does, and at the bottom the exact command line that will actually run.

![Options](docs/02-optionen.png)

### Deliberate choices

| Decision | Reasoning |
|---|---|
| The check starts on its own | Anyone opening an update app wants to know if something is pending — not hunt for a button first. |
| Updates run program by program | A failure then affects exactly one row, and you see which one. `winget upgrade --all` would lump everything together. |
| "No prompts" and "Accept licenses" are pre-selected | Without them, an invisible process hangs on a prompt nobody can see. Both sit visibly next to the button and can be turned off. |
| `--force` is never set automatically | It defeats checks winget performs deliberately. It's only available under "All commands." |
| Only the long form is ever emitted (`--source`, never `-s`) | winget reuses short forms for different meanings: `-h` is "silent" on `install`, "history" on `configure`. |
| A genuine unattended start without admin rights asks once | Most installs and updates fail — silently or loudly — without elevated rights. Decline (or cancel the UAC prompt) and you don't get a second dialog forced on you; instead a persistent warning chip appears top-right, with the same restart attempt one click away. |

---

## Errors are never swallowed

The application has **not a single empty `catch` block**. Every caught error lands in the log
— including the harmless ones that didn't disrupt anything. The header indicator shows at all
times whether something occurred.

![Log](docs/03-protokoll.png)

- **In the window**: every winget call with command line, exit code, and duration; every line
  on the error channel; every caught exception with its full stack.
- **In the file**: `%LOCALAPPDATA%\WinGetUpdater\wingetupdater.log`, written continuously and
  rotated once at 1 MB. Survives a crash too.
- **Enforced**: the `NoSwallowedErrorsTests` test scans the source for empty or
  comment-only `catch` blocks and fails the build the moment one shows up. The only exception
  is the logger itself — if writing the file fails, it has no one left to complain to.

Startup failures are covered too: if the UI can't build, you get the error text and the log
file path instead of a blank window.

---

## All commands

![All commands](docs/04-alle-befehle.png)

The complete surface, unabridged: all 39 commands in eight groups, all 99 options as real
controls, grouped into *Commonly used*, *Advanced*, and *Global*. Above the Run button sits
the exact command line, always — you see what will happen before every click, and can copy it
or save it as a `.ps1`. Results from `search`, `list`, `upgrade`, `pin list`, and `source list`
show up as a sortable, filterable table with multi-selection.

---

## Installation

### Download the release

Grab `WinGetUpdater.exe` from the [latest release](https://github.com/HavocXY/WinGetUpdater/releases/latest)
— a single file, about 59 MB, self-contained. It runs without .NET installed and without
companion files. Built for **win-x64**; for ARM devices, build from source with
`-Runtime win-arm64` (below).

The file is **not code-signed**, so Windows SmartScreen will warn on first launch — confirm via
*More info → Run anyway*. To verify it first, compare its SHA-256 against the hash published on
the release page, or run `winget hash --file WinGetUpdater.exe`.

### Build from source

Requires the .NET 10 SDK. Tested with 10.0.400 on Windows 11.

```powershell
.\build.ps1
```

This checks the schema against your installed winget version, runs the tests, and publishes a
self-contained single file:

```
dist\win-x64\WinGetUpdater.exe    (~59 MB, runs without .NET installed)
```

For ARM devices: `.\build.ps1 -Runtime win-arm64`.
For day-to-day development, `dotnet run --project src\WinGetUpdater` is enough.
Want the console window to stay open after the build finishes instead of closing itself?
Double-click [build-interactive.cmd](build-interactive.cmd) — same build, it just doesn't exit.

### Hidden flags

An application without a console is otherwise only checkable by looking at it. These three
flags make it verifiable by automation instead:

| Call | Purpose |
|---|---|
| `--selftest` | Checks schema, command building, rights detection, and the table parser without a window. Exit code 0 = fine. |
| `--screenshot image.png [--command <id>] [--query <text>] [--run] [--options] [--output] [--log] [--light] [--english]` | Captures the window as a PNG, optionally after an actual run. `--screenshot none` skips the image. |
| `--report file.tsv --command <id> --run --screenshot none` | Appends command, state, exit code, and row count to a file — for sweeping many commands in one pass. |

Unhandled exceptions additionally land in `%TEMP%\wingetupdater-crash.log`.

---

## Completeness

The claim "supports every option" is only worth as much as its verification.

```powershell
.\tools\Check-Schema.ps1
```

The script calls `winget <command> --help` for **every one** of the 39 commands, extracts every
option documented there by regex, and diffs it against the schema. It reports three kinds of
mismatch: `MISSING-FROM-SCHEMA`, `NOT-IN-WINGET`, and `UNUSED-OPTION`. Exit code 0 means they
match exactly; `build.ps1` aborts if the script comes back red.

**When winget updates**, running the script and adding the options it reports to
`src\WinGetUpdater\Resources\winget-schema.json` is all that's needed. The UI itself doesn't
change — it's generated entirely from the schema. If you don't want to rebuild, drop an adjusted
`winget-schema.json` into a `Resources` folder next to the EXE; such a file takes precedence over
the embedded one.

---

## Architecture

```
src\WinGetUpdater\
├─ Resources\winget-schema.json   All 39 commands and 99 options, described in both languages
├─ Resources\Fonts\Manrope\       Bundled typeface, seven weights (SIL Open Font License)
├─ Models\Schema.cs               Data types for it
├─ Services\
│  ├─ SchemaStore.cs              Loads the schema (embedded or external)
│  ├─ WingetLocator.cs            Finds winget.exe, even outside PATH
│  ├─ CommandLineBuilder.cs       Builds arguments and the preview line
│  ├─ WingetRunner.cs             Runs it, streams the output, logs every run
│  ├─ TableParser.cs              Reads the column tables language-independently
│  ├─ ElevationService.cs         Decides on and justifies rights elevation
│  ├─ ErrorLog.cs                 Collection point for everything that goes wrong
│  └─ Localizer.cs                UI text in DE and EN
├─ ViewModels\                    ShellVm (two modes), UpdateVm, CommandVm, OptionVm
└─ Views\                         ShellWindow, UpdatePage, CommandPage, converters
tools\Check-Schema.ps1            The completeness check
tests\WinGetUpdater.Tests\         81 tests, 5 of them against unmodified winget output
```

No NuGet packages at all in the application itself — WPF, `System.Text.Json`, and `Process`
are enough. The project builds offline and without installed workloads because of it.

The main view is not a shortcut with its own rules: `UpdateVm` builds its command lines through
the same `CommandLineBuilder` as the full UI and is bound to the same schema.

### The table parser, and why it looks like this

winget prints its tables as text. Three quirks had to be handled individually, each uncovered
by actual output from a real machine:

1. **Column headers are localized** — "Übereinstimmung" instead of "Match." Column boundaries
   are therefore determined from positions, not from names.
2. **winget pads by display width, not character count.** An installed package with an East
   Asian character in its name occupies two columns but only one `char` — counting in
   characters reads that row shifted by one position, and the package ID starts mid-name. The
   parser counts in display columns.
3. **`winget upgrade` appends its summary with no blank line before the table**, and prints
   `Version Verfügbar Quelle` with single spaces when columns are tight. A single space
   therefore also counts as a column boundary when every data row has one there too; rows that
   violate the grid go to the trailer instead of the table.

The fixture files under `tests\WinGetUpdater.Tests\Fixtures\` are unmodified original output.

### The color palette

Warm neutrals — near-black `#141413`, off-white `#faf9f5`, plus two grays — with a teal accent.
Three deliberate choices, all measured:

- **The accent has its own value per theme:** `#3fa294` in dark, `#1c6e62` in light. A single
  mid-tone cannot clear 4.5:1 against both a near-white and a near-black ground at once — that's
  arithmetic, not taste. The text on top of it flips accordingly: dark on the light accent,
  light on the dark one.
- **The accent is the action color and never signals state.** Success, warning, and error have
  their own colors — and every state additionally carries its own glyph (✓ / ✕ / ●) and its own
  wording. Meaning never rests on color alone, which keeps it legible for color-blind users too.
- **Every foreground/background pairing in use clears 4.5:1** — including the accent as text
  and the state colors on tinted grounds. Measured, not eyeballed: two values fell short on the
  first attempt.

Typeface: [Manrope](https://github.com/sharanda/manrope) (SIL Open Font License), bundled into
the application — WPF cannot fetch web fonts, so the weight files themselves live under
`Resources/Fonts/Manrope` and run without installation on the target machine. Numbers and
command lines stay in a real monospace font (Cascadia Mono/Consolas) — Manrope has no
fixed-width cut, and a proportional font there would break column alignment.

### Elevation

A process started via `runas` cannot be redirected. The elevated call therefore writes to a log
file that is tailed while it runs. To the UI, that looks like any other run.

---

## What's not included

Planned next: a schedule for unattended updates via Windows Task Scheduler, with an allow and
block list.

[Winget-AutoUpdate](https://github.com/Romanitho/Winget-AutoUpdate) (MIT) by Romanitho already
covers exactly that job — a PowerShell service that runs updates in the SYSTEM context. **One**
concept from it has been adopted so far, rewritten in C#: locating `winget.exe` outside PATH
(`Get-WingetCmd.ps1` → `WingetLocator.cs`). Three more are noted but **not yet implemented**:
checking for pending restarts, detecting metered connections, and the allow/block-list model.

[UniGetUI](https://github.com/Devolutions/UniGetUI) is also a common front end. It covers eight
package managers and therefore pursues the opposite goal: lowest common denominator instead of
full depth. Concretely, it calls 9 of the 39 winget commands and exposes 14 of the 41 options on
`install`; the rest is reachable there only through a free-text field.

---

## Known limitations

- **The elevated execution path is not covered by tests** — it requires a genuine UAC
  confirmation. A machine-wide update is the first case where it shows up.
- Rollback reinstalls the previously installed version. If that version is unknown — winget then
  shows a dash — the button is absent on that row, and the manual route stays available under
  "All commands → install."
- Messages in the error log are always in German, even with an English UI. They're diagnostic
  text for troubleshooting, not user-facing copy.
- winget's own output follows the Windows display language, regardless of the configured UI
  language. That's winget behavior and can't be changed.
- Very long values can be truncated by winget with `…` in its tables. The raw output under
  "All commands → Output" shows them in full.
- `dscv3` expects its input via standard input. The UI provides the switches but doesn't pass a
  payload through yet.

---

## Contributing

This started as a personal project, but issues and pull requests are welcome. A few things
worth knowing before sending one:

- **The schema is the source of truth.** Adding or changing a winget option means editing
  `src\WinGetUpdater\Resources\winget-schema.json`, never the XAML — see
  [Architecture](#architecture). `tools\Check-Schema.ps1` has to agree with the installed
  winget version.
- **No empty `catch` blocks, ever.** `NoSwallowedErrorsTests` enforces it; every caught error
  reports to `ErrorLog.Instance`.
- **UI text is bilingual.** Chrome text goes through `Services\Localizer.cs`; winget's own
  vocabulary (option labels, descriptions) goes into the schema JSON as `{"de": …, "en": …}`.
- `.\build.ps1` has to pass — schema check, tests, publish, self-test — before a change counts
  as done.

For the full set of project conventions and non-obvious invariants, see `CLAUDE.md` in the repo
root.

---

## License

[MIT](LICENSE) — © 2026 Stefan Dohr.

Locating `winget.exe` outside PATH is conceptually borrowed from
[Winget-AutoUpdate](https://github.com/Romanitho/Winget-AutoUpdate) (MIT, © Romanitho); the code
itself is written independently in C#.

---

## Deutsch

Eine Windows-Anwendung für den Paketmanager `winget`. Sie kann auf den ersten Blick genau eine
Sache — **installierte Programme aktualisieren** — und auf den zweiten alles, was `winget` kann.

Beim Start prüft die Anwendung von selbst, wofür es neuere Versionen gibt. Was man tun muss:
Haken setzen, auf den Knopf drücken. Kein Menü, keine Kommandozeile, keine Vorkenntnisse.

Wer mehr braucht, wechselt oben auf **Alle Befehle** und bekommt die vollständige
winget-Oberfläche: **39 ausführbare Befehle mit 99 Optionen**, jede einzelne als Bedienelement.
Das ist nicht behauptet, sondern geprüft — siehe [Vollständigkeit](#vollständigkeit).

### Inhalt

- [Funktionen](#funktionen)
- [Die Hauptansicht](#die-hauptansicht)
- [Fehler verschwinden nicht](#fehler-verschwinden-nicht)
- [Alle Befehle](#alle-befehle)
- [Installation](#installation-1)
- [Vollständigkeit](#vollständigkeit)
- [Aufbau](#aufbau)
- [Was nicht enthalten ist](#was-nicht-enthalten-ist)
- [Bekannte Grenzen](#bekannte-grenzen)
- [Mitwirken](#mitwirken)
- [Lizenz](#lizenz)

### Funktionen

- **Updates per Klick** — die Prüfung läuft beim Start automatisch, du setzt Haken und drückst
  einen Knopf.
- **Programm für Programm, nicht alles auf einmal** — jedes Update läuft und meldet sich
  einzeln, ein Fehlschlag ist damit genau einer Zeile zuzuordnen statt hinter neun Erfolgen zu
  verschwinden.
- **Zurückrollen per Klick** — eine fehlgeschlagene Zeile bekommt einen Knopf „Zurückrollen",
  der die zuvor installierte Version neu installiert.
- **Die vollständige winget-Oberfläche, wenn nötig** — 39 Befehle, 99 Optionen, jede einzelne
  ein echtes Bedienelement, nichts davon hinter einem Freitextfeld versteckt.
- **Gegen das Schema geprüfte Vollständigkeit** — ein Skript vergleicht die Optionen der
  Oberfläche mit `winget --help` für jeden Befehl; der Build scheitert, sobald sie voneinander
  abweichen.
- **Kein Fehler verschwindet still** — jede abgefangene Ausnahme landet im Protokoll; ein Test
  durchsucht den Quelltext nach leeren `catch`-Blöcken und lässt den Build scheitern, sobald
  einer auftaucht.
- **Deutsch und Englisch**, umschaltbar zur Laufzeit, ohne Neustart.
- **Barrierearm von Haus aus** — jede Vordergrund-/Hintergrundpaarung erreicht mindestens
  4,5:1 Kontrast; ein Zustand wird nie allein über Farbe vermittelt.
- **Einzelne EXE-Datei** — eigenständig, kein installiertes .NET nötig, ~59 MB.

---

## Die Hauptansicht

| | |
|---|---|
| **Beim Öffnen** | Die Prüfung läuft sofort los. Man sieht entweder eine Liste oder „Alles ist aktuell". |
| **Die Liste** | Programmname, Paket-ID, alte → neue Version, Quelle. Alles vorausgewählt, einzeln abwählbar. |
| **Der Knopf** | Beschriftet mit dem, was er tut: „3 Programme aktualisieren". Nicht „OK". |
| **Während des Laufs** | Jede Zeile bekommt ihren eigenen Zustand: läuft, aktualisiert, fehlgeschlagen. |
| **Wenn etwas schiefgeht** | Ein roter Kasten benennt das Programm und den Exitcode. An jeder fehlgeschlagenen Zeile steht ein Knopf „Zurückrollen", der die zuvor installierte Version neu installiert. Nichts verschwindet stillschweigend. |

Neben dem Knopf steht in Klartext, was gerade gilt — „ohne Rückfragen · Lizenzen angenommen".
Wer das ändern will, klappt **Optionen** auf. Dort steht zu jedem Schalter ein Satz, was er
bewirkt, und ganz unten die fertige Befehlszeile, die tatsächlich ausgeführt wird.

![Optionen](docs/02-optionen.png)

### Bewusste Festlegungen

| Entscheidung | Begründung |
|---|---|
| Die Prüfung startet ohne Zutun | Wer eine Update-Anwendung öffnet, will wissen, ob etwas ansteht — und nicht erst einen Knopf suchen. |
| Aktualisiert wird Programm für Programm | Ein Fehlschlag betrifft dann nur eine Zeile, und man sieht genau welche. `winget upgrade --all` würde alles in einen Topf werfen. |
| „Ohne Rückfragen" und „Lizenzen annehmen" sind vorausgewählt | Ohne sie bleibt ein unsichtbarer Vorgang an einer Abfrage hängen, die niemand sehen kann. Beide stehen sichtbar neben dem Knopf und lassen sich abschalten. |
| `--force` wird nie automatisch gesetzt | Es hebelt Prüfungen aus, die winget bewusst vornimmt. Es steht nur unter „Alle Befehle" zur Verfügung. |
| Es wird immer die Langform erzeugt (`--source`, nie `-s`) | winget belegt Kurzformen doppelt: `-h` ist bei `install` „silent", bei `configure` „history". |
| Ein echter Start ohne Administratorrechte fragt einmal nach | Die meisten Installationen und Updates schlagen ohne erhöhte Rechte fehl oder still. Wer ablehnt (oder die UAC-Abfrage abbricht), bekommt keinen zweiten Dialog aufgedrängt, sondern einen dauerhaften Hinweis-Chip oben rechts — mit demselben Neustart-Versuch einen Klick entfernt. |

---

## Fehler verschwinden nicht

Die Anwendung hat **keinen einzigen leeren `catch`-Block**. Jeder abgefangene Fehler landet im
Protokoll — auch die harmlosen, die den Ablauf nicht gestört haben. Die Anzeige in der Kopfzeile
zeigt jederzeit, ob etwas vorgefallen ist.

![Protokoll](docs/03-protokoll.png)

- **Im Fenster**: jeder winget-Aufruf mit Befehlszeile, Exitcode und Laufzeit; jede Ausgabe auf
  dem Fehlerkanal; jede abgefangene Ausnahme mit vollständigem Stapel.
- **In der Datei**: `%LOCALAPPDATA%\WinGetUpdater\wingetupdater.log`, fortlaufend geschrieben
  und bei 1 MB einmal umgewälzt. Übersteht auch einen Absturz.
- **Abgesichert**: der Test `NoSwallowedErrorsTests` durchsucht den Quelltext nach leeren oder
  nur kommentierten `catch`-Blöcken und lässt den Build scheitern, sobald einer auftaucht.
  Einzige Ausnahme ist der Logger selbst — scheitert das Schreiben der Datei, kann er sich nicht
  bei sich selbst beschweren.

Auch die Startfehler sind abgedeckt: lässt sich die Oberfläche nicht aufbauen, erscheint statt
eines leeren Fensters der Fehlertext samt Pfad zur Protokolldatei.

---

## Alle Befehle

![Alle Befehle](docs/04-alle-befehle.png)

Die vollständige Oberfläche, unverändert: alle 39 Befehle in acht Gruppen, alle 99 Optionen als
Bedienelement, gruppiert in *Häufig verwendet*, *Erweitert* und *Global*. Über dem
Ausführen-Knopf steht immer die exakte Befehlszeile — man sieht vor jedem Klick, was passiert,
kann sie kopieren oder als `.ps1` speichern. Ergebnisse von `search`, `list`, `upgrade`,
`pin list` und `source list` erscheinen als sortier- und filterbare Tabelle mit
Mehrfachauswahl.

---

## Installation

### Fertige EXE herunterladen

`WinGetUpdater.exe` aus dem [letzten Release](https://github.com/HavocXY/WinGetUpdater/releases/latest)
laden — eine einzelne Datei, rund 59 MB, eigenständig. Läuft ohne installiertes .NET und ohne
Begleitdateien. Gebaut für **win-x64**; für ARM-Geräte selbst aus dem Quelltext bauen, mit
`-Runtime win-arm64` (unten).

Die Datei ist **nicht signiert**, Windows SmartScreen warnt daher beim ersten Start — über
*Weitere Informationen → Trotzdem ausführen* bestätigen. Wer vorher prüfen will, vergleicht den
SHA-256-Hash mit dem auf der Release-Seite veröffentlichten, oder führt
`winget hash --file WinGetUpdater.exe` aus.

### Aus dem Quelltext bauen

Voraussetzung: .NET 10 SDK. Getestet mit 10.0.400 unter Windows 11.

```powershell
.\build.ps1
```

Das prüft das Schema gegen die installierte winget-Version, führt die Tests aus und legt eine
eigenständige Einzeldatei ab:

```
dist\win-x64\WinGetUpdater.exe    (~59 MB, läuft ohne installiertes .NET)
```

Für ARM-Geräte: `.\build.ps1 -Runtime win-arm64`.
Während der Entwicklung genügt `dotnet run --project src\WinGetUpdater`.
Soll das Fenster nach dem Build offen bleiben statt sich selbst zu schließen? Dann
[build-interactive.cmd](build-interactive.cmd) doppelklicken — derselbe Build, nur ohne
Selbstschließen.

### Verborgene Schalter

Eine Anwendung ohne Konsole lässt sich sonst nur durch Hinsehen prüfen. Diese drei Schalter
machen sie automatisiert nachvollziehbar:

| Aufruf | Zweck |
|---|---|
| `--selftest` | Prüft Schema, Befehlsaufbau, Rechteerkennung und Tabellenparser ohne Fenster. Exitcode 0 = in Ordnung. |
| `--screenshot bild.png [--command <id>] [--query <text>] [--run] [--options] [--output] [--log] [--light] [--english]` | Nimmt das Fenster als PNG auf, wahlweise nach einem echten Lauf. `--screenshot none` überspringt das Bild. |
| `--report datei.tsv --command <id> --run --screenshot none` | Hängt Befehl, Zustand, Exitcode und Zeilenzahl an eine Datei — für Durchläufe über viele Befehle. |

Nicht behandelte Ausnahmen stehen zusätzlich in `%TEMP%\wingetupdater-crash.log`.

---

## Vollständigkeit

Der Anspruch „unterstützt alle Optionen" ist nur so viel wert wie seine Prüfung.

```powershell
.\tools\Check-Schema.ps1
```

Das Skript ruft für **jeden** der 39 Befehle `winget <befehl> --help` auf, liest per Regex jede
dort dokumentierte Option aus und vergleicht sie mit dem Schema. Es meldet drei Arten von
Abweichung: `FEHLT-IM-SCHEMA`, `NICHT-IN-WINGET` und `UNBENUTZTE-OPTION`. Exitcode 0 bedeutet
deckungsgleich; `build.ps1` bricht ab, wenn das Skript rot ist.

**Bei einer neuen winget-Version** genügt es, das Skript laufen zu lassen und die gemeldeten
Optionen in `src\WinGetUpdater\Resources\winget-schema.json` nachzutragen. Die Oberfläche ändert
sich dabei nicht — sie wird vollständig aus dem Schema erzeugt. Wer nicht neu bauen will, legt
eine angepasste `winget-schema.json` in einen Ordner `Resources` neben die EXE; eine solche Datei
hat Vorrang vor der eingebetteten.

---

## Aufbau

```
src\WinGetUpdater\
├─ Resources\winget-schema.json   Alle 39 Befehle und 99 Optionen, zweisprachig beschrieben
├─ Resources\Fonts\Manrope\       Eingebettete Schrift, sieben Schnitte (SIL Open Font License)
├─ Models\Schema.cs               Datentypen dazu
├─ Services\
│  ├─ SchemaStore.cs              Lädt das Schema (eingebettet oder extern)
│  ├─ WingetLocator.cs            Findet winget.exe, auch außerhalb des PATH
│  ├─ CommandLineBuilder.cs       Baut Argumente und die Vorschauzeile
│  ├─ WingetRunner.cs             Führt aus, streamt die Ausgabe, protokolliert jeden Lauf
│  ├─ TableParser.cs              Liest die Spaltentabellen sprachunabhängig
│  ├─ ElevationService.cs         Entscheidet und begründet die Rechteerhöhung
│  ├─ ErrorLog.cs                 Sammelstelle für alles, was schiefgeht
│  └─ Localizer.cs                Texte der Oberfläche in DE und EN
├─ ViewModels\                    ShellVm (zwei Betriebsarten), UpdateVm, CommandVm, OptionVm
└─ Views\                         ShellWindow, UpdatePage, CommandPage, Konverter
tools\Check-Schema.ps1            Der Vollständigkeitsnachweis
tests\WinGetUpdater.Tests\         81 Tests, davon 5 gegen unveränderte winget-Ausgaben
```

Kein einziges NuGet-Paket in der Anwendung selbst — WPF, `System.Text.Json` und `Process`
reichen. Das Projekt baut damit auch offline und ohne installierte Workloads.

Die Hauptansicht ist keine Abkürzung mit eigenen Regeln: `UpdateVm` erzeugt seine Befehlszeilen
über denselben `CommandLineBuilder` wie die vollständige Oberfläche und ist damit an dasselbe
Schema gebunden.

### Der Tabellenparser, und warum er so aussieht

winget gibt seine Tabellen als Text aus. Drei Eigenheiten mussten dabei einzeln behandelt
werden, alle drei durch echte Ausgaben dieses Rechners aufgedeckt:

1. **Die Spaltenköpfe sind lokalisiert** — „Übereinstimmung" statt „Match". Die Spaltengrenzen
   werden deshalb aus Positionen bestimmt, nicht aus Namen.
2. **winget füllt nach Darstellungsbreite, nicht nach Zeichenanzahl.** Ein installiertes Paket
   mit einem ostasiatischen Zeichen im Namen belegt zwei Spalten, aber nur ein `char` — wer in
   Zeichen rechnet, liest diese Zeile um eine Stelle verschoben, und die Paket-ID beginnt mitten
   im Namen. Der Parser rechnet in Darstellungsspalten.
3. **`winget upgrade` hängt seine Zusammenfassung ohne Leerzeile an die Tabelle** und schreibt
   bei engen Spalten `Version Verfügbar Quelle` mit je einem Leerzeichen. Ein einzelnes
   Leerzeichen gilt deshalb zusätzlich als Spaltengrenze, wenn an derselben Stelle in jeder
   Datenzeile ebenfalls eines steht; Zeilen, die das Raster verletzen, landen im Anhang statt in
   der Tabelle.

Die Testdateien unter `tests\WinGetUpdater.Tests\Fixtures\` sind unveränderte Originalausgaben.

### Die Farbwelt

Warme Neutraltöne — nahezu Schwarz `#141413`, gebrochenes Weiß `#faf9f5`, dazu zwei Grautöne —
mit einem Blaugrün als Akzent. Drei Festlegungen, alle nachgemessen:

- **Der Akzent hat je Design einen eigenen Wert:** `#3fa294` im dunklen, `#1c6e62` im hellen.
  Ein einzelner mittlerer Ton kann nicht gleichzeitig auf nahezu weißem und auf nahezu schwarzem
  Grund 4,5:1 erreichen — das ist Arithmetik, keine Geschmacksfrage. Entsprechend kehrt sich auch
  die Schrift darauf um: dunkel auf dem hellen Akzent, hell auf dem dunklen.
- **Der Akzent ist die Aktionsfarbe und bedeutet nie einen Zustand.** Erfolg, Warnung und Fehler
  haben eigene Farben — und jeder Zustand trägt zusätzlich sein eigenes Zeichen (✓ / ✕ / ●) und
  einen Text. Die Bedeutung hängt damit nie an der Farbe allein, was sie auch bei
  Farbfehlsichtigkeit lesbar hält.
- **Jede verwendete Vordergrund-/Hintergrundpaarung erreicht mindestens 4,5:1** — einschließlich
  des Akzents als Textfarbe und der Zustandsfarben auf getönten Flächen. Nachgemessen, nicht
  geschätzt: zwei Werte lagen im ersten Anlauf darunter.

Schrift: [Manrope](https://github.com/sharanda/manrope) (SIL Open Font License), fest in die
Anwendung eingebettet — WPF kann keine Webschriften nachladen, deshalb liegen die Gewichtsdateien
selbst unter `Resources/Fonts/Manrope` und laufen ohne Installation auf dem Zielrechner. Zahlen
und Befehlszeilen bleiben in einer echten Monospace-Schrift (Cascadia Mono/Consolas) — Manrope
hat keinen Festbreiten-Schnitt, und eine Proportionalschrift dort würde die Spaltenausrichtung
zerstören.

### Die Rechteerhöhung

Ein per `runas` gestarteter Prozess lässt sich nicht umleiten. Der erhöhte Aufruf schreibt daher
in eine Protokolldatei, die während des Laufs mitgelesen wird. Für die Oberfläche sieht das aus
wie jeder andere Lauf.

---

## Was nicht enthalten ist

Geplante Erweiterung: ein Zeitplan für unbeaufsichtigte Updates über die Windows-Aufgabenplanung,
mit Allow- und Blocklist.

Für genau diese Aufgabe gibt es bereits
[Winget-AutoUpdate](https://github.com/Romanitho/Winget-AutoUpdate) (MIT) von Romanitho — ein
PowerShell-Dienst, der Updates im SYSTEM-Kontext fährt. Übernommen ist daraus bisher **ein**
Konzept, in C# neu geschrieben: das Auffinden von `winget.exe` außerhalb des PATH
(`Get-WingetCmd.ps1` → `WingetLocator.cs`). Drei weitere sind vorgemerkt, aber **noch nicht
umgesetzt**: Prüfung auf ausstehende Neustarts, Erkennung getakteter Verbindungen und das
Allow-/Blocklist-Modell.

Als Frontend ist außerdem [UniGetUI](https://github.com/Devolutions/UniGetUI) verbreitet. Es
deckt acht Paketmanager ab und verfolgt damit das entgegengesetzte Ziel: kleinster gemeinsamer
Nenner statt vollständige Tiefe. Konkret ruft es 9 der 39 winget-Befehle auf und erzeugt bei
`install` 14 der 41 Optionen; der Rest ist dort nur über ein Freitextfeld erreichbar.

---

## Bekannte Grenzen

- **Der erhöhte Ausführungspfad ist nicht durch Tests abgedeckt** — er verlangt eine echte
  UAC-Bestätigung. Ein maschinenweites Update ist der erste Fall, an dem er sich zeigt.
- Das Zurückrollen installiert die zuletzt installierte Version neu. Ist diese Version nicht
  bekannt — winget zeigt dann einen Gedankenstrich an — ist der Knopf an dieser Zeile
  nicht vorhanden, und es bleibt die manuelle Option unter „Alle Befehle → install".
- Die Meldungen im Fehlerprotokoll sind immer auf Deutsch, auch bei englischer Oberfläche. Sie
  sind Diagnosetext für die Fehlersuche, keine Bedienoberfläche.
- Die Ausgabe von winget erscheint in der Sprache von Windows, unabhängig von der eingestellten
  Oberflächensprache. Das ist winget-Verhalten und nicht änderbar.
- Sehr lange Werte kann winget in seinen Tabellen mit `…` kürzen. Die Rohausgabe unter
  „Alle Befehle → Ausgabe" zeigt sie vollständig.
- `dscv3` erwartet seine Eingabe über die Standardeingabe. Die Oberfläche stellt die Schalter
  bereit, reicht aber noch keine Nutzlast durch.

---

## Mitwirken

Das Projekt ist als persönliches Vorhaben entstanden, Issues und Pull Requests sind trotzdem
willkommen. Ein paar Dinge vorab:

- **Das Schema ist die Wahrheit.** Eine winget-Option hinzufügen oder ändern heißt
  `src\WinGetUpdater\Resources\winget-schema.json` bearbeiten, nie die XAML — siehe
  [Aufbau](#aufbau). `tools\Check-Schema.ps1` muss mit der installierten winget-Version
  übereinstimmen.
- **Kein leerer `catch`-Block, niemals.** `NoSwallowedErrorsTests` setzt das durch; jeder
  abgefangene Fehler meldet sich bei `ErrorLog.Instance`.
- **Oberflächentexte sind zweisprachig.** Chrome-Text läuft über `Services\Localizer.cs`,
  wingets eigenes Vokabular (Optionsnamen, Beschreibungen) steht in der Schema-JSON als
  `{"de": …, "en": …}`.
- `.\build.ps1` muss durchlaufen — Schemaprüfung, Tests, Publish, Selbsttest — bevor eine
  Änderung als fertig gilt.

Die vollständigen Projektkonventionen und nicht offensichtlichen Zusammenhänge stehen in
`CLAUDE.md` im Repository-Wurzelverzeichnis.

---

## Lizenz

[MIT](LICENSE) — © 2026 Stefan Dohr.

Das Auffinden von `winget.exe` außerhalb des PATH ist konzeptionell
[Winget-AutoUpdate](https://github.com/Romanitho/Winget-AutoUpdate) entlehnt (MIT, © Romanitho);
der Code ist eigenständig in C# geschrieben.
