# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

WinGetUpdater is a WPF desktop front end for the Windows package manager `winget`. It has two
faces, and both are load-bearing requirements:

1. **The main view does exactly one thing** — update installed programs — and must stay
   self-explanatory for someone opening it for the first time. Anything else belongs behind the
   "All commands" mode, the options expander, or the log drawer.
2. **The advanced mode exposes every winget command and option** — currently 39 executable
   commands and 99 distinct options for winget 1.29.290.

A third rule cuts across both: **no error is ever swallowed.** There is not a single empty
`catch` block, and a test enforces that.

Read [Architecture](#architecture) before changing anything structural.

UI language is German and English, switchable at runtime. Source comments and identifiers are
German; keep that convention when editing.

## Commands

```powershell
.\build.ps1                       # schema check + tests + single-file publish + self-test
.\build.ps1 -Runtime win-arm64    # other target
.\build.ps1 -SkipTests            # publish only
.\tools\Check-Schema.ps1          # completeness check on its own (exit 0 = schema matches winget)
```

```bash
dotnet build src/WinGetUpdater/WinGetUpdater.csproj      # dev build
dotnet run --project src/WinGetUpdater                  # run the app
dotnet test tests/WinGetUpdater.Tests/WinGetUpdater.Tests.csproj
dotnet test tests/WinGetUpdater.Tests/WinGetUpdater.Tests.csproj --filter "FullyQualifiedName~TableParserTests"
```

The solution is `WinGetUpdater.slnx` (the XML solution format), not a classic `.sln`.

### Headless modes — use these to verify changes

The app is a `WinExe`, so these are the way to check it without a human clicking. All three exist
because normal verification is otherwise impossible; prefer them over asking the user to look.

```bash
WinGetUpdater.exe --selftest
# Schema load, option-id resolution, winget discovery, command-line building, elevation
# detection and table parsing, without opening a window. Exit 0 = fine.
# Report also written to %TEMP%\wingetupdater-selftest.txt

WinGetUpdater.exe --screenshot out.png [flags]
# Renders the real window to PNG. Read the PNG back to check layout and bindings.
#   (no --command)   main view; waits for the automatic update check to finish first
#   --command <id>   switches to advanced mode and selects that command
#   --query <text>   prefills the query field
#   --run            runs the selected command (advanced mode)
#   --update         actually performs the updates (CHANGES THE MACHINE — ask first)
#   --options        opens the update options expander
#   --output         opens the update log panel
#   --log            opens the error-log drawer
#   --light --english  theme and language
#   --screenshot none  skip the image (useful with --report)

WinGetUpdater.exe --report runs.tsv --command <id> --run --screenshot none
# Appends: command id, run state, status text, row count, preview line, first output line.
# Loop this over many commands to smoke-test them in one pass.
```

Unhandled exceptions land in `%TEMP%\wingetupdater-crash.log` **and** in the error log at
`%LOCALAPPDATA%\WinGetUpdater\wingetupdater.log`. Check both after any headless run; a silent
exit code 0 with no PNG usually means an exception was recorded there.

## Architecture

### Two modes, one engine

`ShellVm.Mode` switches between `AppMode.Updates` (the default, `UpdatePage` + `UpdateVm`) and
`AppMode.Advanced` (sidebar + `CommandPage` + `CommandVm`). Both build their winget arguments
through the **same** `CommandLineBuilder` against the **same** schema — the simple view is a
tidier way to drive the same machinery, not a shortcut with its own rules. If you add a flag to
the update view, it must exist as an option id in the schema.

Two traps that already bit once:

* `ShellVm.Select()` must **not** change `Mode`. It is called from the constructor to pre-warm the
  search page; switching there would boot the app into the wrong view.
* The update check runs itself from `UpdatePage.OnLoaded`, not from the view model constructor.
  Anything waiting on it must wait for `Stage != Start && !IsBusy`, not just `!IsBusy` — the check
  has not started yet at the moment the window appears.

### No swallowed errors

`ErrorLog.Instance` is the single sink. Every `catch` reports to it, including the ones that
recover silently (use `Info` for expected-and-handled, `Warn` for degraded, `Error` for broken).
`WingetRunner` logs every single run — command line, exit code, duration, and any stderr — so the
log is a complete trail, not just a crash record.

`NoSwallowedErrorsTests` scans `src/` for empty or comment-only `catch` blocks and for files that
catch without mentioning `ErrorLog`. **Do not "fix" a failure there by exempting the file.**
`ErrorLog.cs` itself is the only exemption, because a logger cannot report its own failure to
write without recursing.

### The schema is the program

`src/WinGetUpdater/Resources/winget-schema.json` describes all 39 commands and 99 options. The UI
is generated from it at runtime. **There is no per-command UI code anywhere.** The flow is:

```
winget-schema.json
  → SchemaStore          loads it (embedded resource, or a file next to the exe which wins)
  → CommandVm            builds one OptionVm per option id listed in the command
  → OptionVm             picks its control purely from OptionSpec.Kind
  → CommandLineBuilder   turns the values into an argument array + the preview line
  → WingetRunner         runs winget.exe, streams stdout/stderr back line by line
  → TableParser          turns column output into a grid (search/list/upgrade/pin list/source list)
```

Consequences worth internalising:

* **To add or change a winget option, edit the JSON. Never the XAML.** Adding a command means one
  more entry in `commands`; adding an option means one entry in `options` plus its id in the
  command's `primary` or `advanced` list.
* A command's option ids are split into `primary` (shown directly) and `advanced` (behind an
  expander); `globals` apply to every command. `positional` names the option winget also accepts
  as a bare first argument.
* `OptionSpec.Kind` (`flag` / `text` / `int` / `enum` / `filePath` / `folderPath` / `saveFilePath`)
  is the only thing that decides the control. `repeatable` turns it into a chip list.

### The completeness guarantee

`tools/Check-Schema.ps1` runs `winget <command> --help` for every command in the schema, extracts
every documented long option and diffs it against the schema in both directions. `build.ps1` fails
the build when it disagrees. `SchemaTests.cs` covers the same invariants offline (no orphan option
definitions, no duplicates, both languages present, positional flags marked).

**When winget updates**, run `Check-Schema.ps1`, add what it reports to the JSON, done. Never
"fix" a drift report by deleting the check or narrowing its scope — the check is the product
requirement made testable.

### Non-obvious invariants

* **Only long forms are emitted** (`--source`, never `-s`). winget reuses short forms across
  commands with different meanings: `-h` is `--silent` for `install` but `--history` for
  `configure`; `-o` is `--log` on `install` and `--output` on `export`; `-a` is `--architecture`
  but `--arg` for `source add`. Short forms exist in the schema only as display hints.
* **Colliding flags get separate option ids.** `--manifest` is a file path on `install`
  (`manifestPath`), a positional path on `validate` (`validateManifest`) and a boolean on `dscv3`
  (`dscManifest`). Same for `--enable`/`--disable`, which take a value under `settings` but are
  bare switches under `configure`/`mcp`. Adding an option that reuses an existing CLI flag with
  different arity means a new id, not a shared one.
* **Positional arguments are still written with their flag** (`winget hash --file X`). winget
  accepts both; the explicit form keeps the preview unambiguous.
* **`--disable-interactivity` is pre-set** on every command. A windowless child process must never
  block on a prompt nobody can see. It is a normal checkbox the user can clear.
* **`--force` and the `--accept-*` switches are never added automatically.** They defeat checks
  winget performs deliberately. Options with `"risk": "high"` in the schema get a warning marker.
* **Elevated runs cannot be redirected.** `WingetRunner.RunElevatedAsync` starts
  `cmd /c winget … > %TEMP%\wgupdater-<guid>.log 2>&1` with `Verb=runas` and tails that file while
  it runs, so the UI sees the same line stream either way. UAC cancellation surfaces as
  `Win32Exception` 1223. Note this path is not covered by tests — it needs a real UAC prompt.
* **Table parsing is positional and measured in display columns, not characters.** Three real
  winget quirks are handled, each found in actual output from this machine and each covered by a
  fixture in `tests/WinGetUpdater.Tests/Fixtures/`:
  - headers are localised (`Übereinstimmung` vs `Match`), so boundaries come from positions;
  - winget pads by **display width**, so an East Asian character (one `char`, two columns) shifts
    a whole row — `TableParser.RuneWidth` exists for this and must not be simplified away;
  - `winget upgrade` appends its summary line with no blank line before it, and prints
    `Version Verfügbar Quelle` with single spaces. A single space counts as a boundary when every
    data row has a space there, and rows that violate the grid go to `TableResult.Trailer`.

  Fixtures that are *not* verbatim winget output must be built from column widths, never from
  hand-aligned literals — hand alignment silently rots.
* **`winget.exe` is not reliably on PATH.** `WingetLocator` tries the App Execution Alias, then
  PATH, then globs `Microsoft.DesktopAppInstaller_*_8wekyb3d8bbwe` under WindowsApps. If none
  works the app shows an explanatory page instead of failing.

### Localisation split

Two separate mechanisms, deliberately:

* **UI chrome** lives in `Services/Localizer.cs` as an in-code table, bound via an indexer
  (`{Binding [Run.Execute], Source={x:Static svc:Localizer.Instance}}`). Switching raises
  `PropertyChanged("Item[]")` so every binding re-evaluates without a restart. Chosen over `.resx`
  precisely for that live switching.
* **winget's own vocabulary** — option labels, descriptions, command titles — lives in the schema
  JSON as `{"de": …, "en": …}` pairs, surfaced through `Models.Loc.Get(language)`.

winget's *output* always follows the Windows display language regardless of the UI setting. That
is winget behaviour and cannot be changed.

### Dependencies

The application has **no NuGet packages** — WPF, `System.Text.Json` and `System.Diagnostics.Process`
only, so it builds offline and without workloads. Keep it that way unless there is a strong reason;
the test project is the only place with package references (xunit).

The schema is an `EmbeddedResource` with `LogicalName="winget-schema.json"` so the published
artifact is genuinely a single file. `SchemaStore` still prefers `Resources\winget-schema.json`
next to the exe when present, which lets users retarget a newer winget without rebuilding — don't
remove that fallback.
