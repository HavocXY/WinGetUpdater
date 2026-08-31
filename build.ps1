<#
.SYNOPSIS
    Baut, prueft und veroeffentlicht WinGet Studio.

.DESCRIPTION
    Ohne Parameter wird die vollstaendige Kette ausgefuehrt:
      1. Schemapruefung gegen die installierte winget-Version
      2. Unit-Tests
      3. Selbsttest der fertigen Anwendung
      4. Veroeffentlichung als einzelne, eigenstaendige EXE nach dist\

.PARAMETER Runtime
    Zielplattform, Standard win-x64. Fuer ARM-Geraete: win-arm64.

.PARAMETER SkipTests
    Ueberspringt Schemapruefung und Unit-Tests.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Runtime win-arm64
#>
[CmdletBinding()]
param(
    [string] $Runtime = 'win-x64',
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\WinGetStudio\WinGetStudio.csproj'
$tests = Join-Path $root 'tests\WinGetStudio.Tests\WinGetStudio.Tests.csproj'
$output = Join-Path $root "dist\$Runtime"

function Step([string] $text) {
    Write-Host ''
    Write-Host "==> $text" -ForegroundColor Cyan
}

if (-not $SkipTests) {
    Step 'Schema gegen die installierte winget-Version pruefen'
    & (Join-Path $root 'tools\Check-Schema.ps1') -Quiet
    if ($LASTEXITCODE -ne 0) { throw 'Das Schema weicht von der installierten winget-Version ab.' }

    Step 'Unit-Tests'
    dotnet test $tests --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Unit-Tests fehlgeschlagen.' }
}

Step "Veroeffentlichen fuer $Runtime"
dotnet publish $project `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $output `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    --nologo
if ($LASTEXITCODE -ne 0) { throw 'Veroeffentlichung fehlgeschlagen.' }

$exe = Join-Path $output 'WinGetStudio.exe'
if (-not (Test-Path $exe)) { throw "Erwartete Datei fehlt: $exe" }

Step 'Selbsttest der veroeffentlichten Anwendung'
& $exe --selftest | Out-Null
$selfTestReport = Join-Path $env:TEMP 'wingetstudio-selftest.txt'
if (Test-Path $selfTestReport) { Get-Content $selfTestReport | Write-Host }
if ($LASTEXITCODE -ne 0) { throw 'Selbsttest fehlgeschlagen.' }

$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ''
Write-Host "Fertig: $exe ($size MB)" -ForegroundColor Green
