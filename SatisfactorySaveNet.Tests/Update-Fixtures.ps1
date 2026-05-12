<#
.SYNOPSIS
    Pulls every game save matching the curated fixture naming pattern from the
    local Satisfactory save directory into Fixtures/.

.DESCRIPTION
    Reads the ExpectedSessionName constant from
    Fixtures/RealSaveFixtureTests.cs (the source of truth) and copies every
    .sav file in %LOCALAPPDATA%\FactoryGame\Saved\SaveGames whose filename
    matches "<session-name> - *.sav" into the test project's Fixtures
    directory. Existing files are overwritten.

    Personal autosaves don't match the pattern and won't be copied; if you
    drop one into Fixtures/ manually, .gitignore keeps it out of git history
    while the smoke test still exercises it locally.

.PARAMETER SaveGamesRoot
    Override the source path. Defaults to the Satisfactory default location.

.PARAMETER WhatIf
    Print what would be copied without doing it.

.EXAMPLE
    .\Update-Fixtures.ps1
    Sync all matching saves into Fixtures/.

.EXAMPLE
    .\Update-Fixtures.ps1 -WhatIf
    Show what would be copied without writing.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$SaveGamesRoot = (Join-Path $env:LOCALAPPDATA 'FactoryGame\Saved\SaveGames')
)

$ErrorActionPreference = 'Stop'

$here         = Split-Path -Parent $MyInvocation.MyCommand.Path
$fixturesDir  = Join-Path $here 'Fixtures'
$testsClass   = Join-Path $fixturesDir 'RealSaveFixtureTests.cs'

if (-not (Test-Path $testsClass)) {
    throw "Could not find $testsClass — run this script from inside SatisfactorySaveNet.Tests."
}

# The C# fixture class is the single source of truth for the session name.
$source = Get-Content $testsClass -Raw
if ($source -notmatch 'ExpectedSessionName\s*=\s*"([^"]+)"') {
    throw 'Could not extract ExpectedSessionName from RealSaveFixtureTests.cs — has the constant been renamed?'
}
$sessionName = $Matches[1]
$pattern     = "$sessionName - *.sav"

Write-Host "Session : $sessionName"
Write-Host "Pattern : $pattern"
Write-Host "Source  : $SaveGamesRoot"
Write-Host "Target  : $fixturesDir"
Write-Host ''

if (-not (Test-Path $SaveGamesRoot)) {
    throw "Save games root not found: $SaveGamesRoot"
}

$matches = Get-ChildItem -Path $SaveGamesRoot -Recurse -File -Filter $pattern -ErrorAction SilentlyContinue
if (-not $matches) {
    Write-Warning "No saves matched the pattern under $SaveGamesRoot. Did you save in-game with the '$sessionName - <Scenario>' prefix?"
    return
}

# If the same fixture name shows up in multiple session subfolders, take the
# most recently written copy and warn — that's almost always what the user
# wants (latest save wins), but it's worth surfacing.
$grouped = $matches | Group-Object Name
$toCopy  = foreach ($group in $grouped) {
    if ($group.Count -gt 1) {
        Write-Warning ("Duplicate '{0}' in {1} session folders — taking the most recent." -f $group.Name, $group.Count)
    }
    $group.Group | Sort-Object LastWriteTime -Descending | Select-Object -First 1
}

$copied = 0
foreach ($file in $toCopy) {
    $dest = Join-Path $fixturesDir $file.Name
    if ($PSCmdlet.ShouldProcess($dest, "Copy from $($file.FullName)")) {
        Copy-Item -Path $file.FullName -Destination $dest -Force
        Write-Host "Copied: $($file.Name)  ($([math]::Round($file.Length / 1KB)) KB)"
        $copied++
    }
}

Write-Host ''
Write-Host "Done. $copied file(s) synced into Fixtures/."
Write-Host "Next: dotnet test --filter FullyQualifiedName~RealSaveFixture"
