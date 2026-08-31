<#
.SYNOPSIS
    Vergleicht winget-schema.json mit der Hilfe der installierten winget-Version.

.DESCRIPTION
    Ruft fuer jeden im Schema beschriebenen Befehl "winget <befehl> --help" auf,
    liest alle dort dokumentierten Optionen aus und vergleicht sie mit dem Schema.

    Exitcode 0 = Schema und winget stimmen ueberein.
    Exitcode 1 = Abweichung gefunden (fehlende oder ueberzaehlige Optionen).

    Das ist der Vollstaendigkeitsnachweis: solange dieses Skript gruen ist,
    unterstuetzt WinGet Studio jede Option, die winget dokumentiert.

.PARAMETER SchemaPath
    Pfad zu winget-schema.json. Standard: ..\src\WinGetStudio\Resources\winget-schema.json

.PARAMETER Quiet
    Nur die Zusammenfassung ausgeben.
#>
[CmdletBinding()]
param(
    [string] $SchemaPath,
    [switch] $Quiet
)

$ErrorActionPreference = 'Stop'

if (-not $SchemaPath) {
    # $PSScriptRoot ist je nach Aufrufart leer; dann vom aktuellen Verzeichnis aus suchen.
    $base = if ($PSScriptRoot) { $PSScriptRoot } else { Join-Path (Get-Location) 'tools' }
    $SchemaPath = Join-Path $base '..\src\WinGetStudio\Resources\winget-schema.json'
}

if (-not (Test-Path -LiteralPath $SchemaPath)) {
    Write-Host "Schemadatei nicht gefunden: $SchemaPath" -ForegroundColor Red
    exit 2
}

# winget gibt UTF-8 aus; ohne das werden Umlaute in der Hilfe zerlegt.
$previousEncoding = [Console]::OutputEncoding
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

# Optionen, die bewusst nicht im Schema stehen: --help wird durch den
# Dokumentationslink der Oberflaeche ersetzt.
$ignored = @('--help')

function Get-HelpOptions {
    param([string[]] $CommandPath)

    $raw = & winget @CommandPath --help 2>&1 | Out-String
    $found = New-Object System.Collections.Generic.HashSet[string]

    foreach ($line in ($raw -split "`r?`n")) {
        # Optionszeilen der winget-Hilfe: zwei Leerzeichen, dann die Flags,
        # dann mindestens zwei Leerzeichen und die Beschreibung.
        if ($line -notmatch '^\s{2}(-|--)\S') { continue }
        $flags = ($line.Trim() -split '\s{2,}')[0]
        foreach ($flag in ($flags -split ',')) {
            $flag = $flag.Trim()
            if ($flag.StartsWith('--') -and $ignored -notcontains $flag) {
                [void] $found.Add($flag)
            }
        }
    }
    return $found
}

$schema = Get-Content -LiteralPath $SchemaPath -Raw -Encoding UTF8 | ConvertFrom-Json
$options = $schema.options
$globals = @($schema.globals)

$problems = @()
$checked = 0
$optionRefs = New-Object System.Collections.Generic.HashSet[string]

foreach ($cmd in $schema.commands) {
    $checked++
    $path = @($cmd.path)

    $ids = @()
    if ($cmd.positional) { $ids += $cmd.positional }
    $ids += @($cmd.primary)
    $ids += @($cmd.advanced)
    $ids += $globals
    $ids = $ids | Where-Object { $_ } | Select-Object -Unique

    $expected = New-Object System.Collections.Generic.HashSet[string]
    foreach ($id in $ids) {
        [void] $optionRefs.Add($id)
        $opt = $options.$id
        if (-not $opt) {
            $problems += [pscustomobject]@{
                Command = ($path -join ' '); Kind = 'UNBEKANNTE-ID'; Detail = $id
            }
            continue
        }
        [void] $expected.Add($opt.cli)
        foreach ($a in @($opt.aliases)) { if ($a) { [void] $expected.Add($a) } }
    }

    $actual = Get-HelpOptions -CommandPath $path

    foreach ($flag in $actual) {
        if (-not $expected.Contains($flag)) {
            $problems += [pscustomobject]@{
                Command = ($path -join ' '); Kind = 'FEHLT-IM-SCHEMA'; Detail = $flag
            }
        }
    }
    foreach ($flag in $expected) {
        if (-not $actual.Contains($flag)) {
            $problems += [pscustomobject]@{
                Command = ($path -join ' '); Kind = 'NICHT-IN-WINGET'; Detail = $flag
            }
        }
    }

    if (-not $Quiet) {
        $status = if ($problems | Where-Object Command -eq ($path -join ' ')) { 'X' } else { 'OK' }
        Write-Host ("  [{0,2}] winget {1}" -f $status, ($path -join ' '))
    }
}

# Verwaiste Optionsdefinitionen finden: im Schema definiert, von keinem Befehl benutzt.
foreach ($prop in $options.PSObject.Properties) {
    if (-not $optionRefs.Contains($prop.Name)) {
        $problems += [pscustomobject]@{
            Command = '(global)'; Kind = 'UNBENUTZTE-OPTION'; Detail = $prop.Name
        }
    }
}

Write-Host ''
Write-Host ("winget-Version im Schema : {0}" -f $schema.wingetVersion)
Write-Host ("winget-Version installiert: {0}" -f (& winget --version))
Write-Host ("Geprüfte Befehle          : {0}" -f $checked)
Write-Host ("Optionsdefinitionen       : {0}" -f @($options.PSObject.Properties).Count)

if ($problems.Count -eq 0) {
    Write-Host ''
    Write-Host 'Schema vollstaendig - jede von winget dokumentierte Option ist abgebildet.' -ForegroundColor Green
    try { [Console]::OutputEncoding = $previousEncoding } catch { }
    exit 0
}

Write-Host ''
Write-Host ("{0} Abweichung(en):" -f $problems.Count) -ForegroundColor Red
$problems | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
try { [Console]::OutputEncoding = $previousEncoding } catch { }
exit 1
