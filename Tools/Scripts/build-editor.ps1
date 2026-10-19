<#
.SYNOPSIS
    Builds a Release executable of the Anvil editor (Editor/Anvil/Anvil.csproj).

.DESCRIPTION
    Restores the local dotnet tools (mgcb) and NuGet packages, then publishes Anvil as a
    self-contained win-x64 Release app. The Engine, Vista, the content pipeline extension and
    the MonoGame content (.xnb) are built along with it through the project references, and
    steam_appid.txt is copied next to the exe so Steam works in Play mode.

    The output has to stay inside this checkout (default: Builds/Anvil, git-ignored): Anvil finds
    the source Engine/Content by walking up from its own folder, and it needs that source tree to
    import assets, save scenes/settings and build games (File > Build). The .NET SDK must be
    installed on the machine for those features.

    Close Anvil before running, otherwise the files in the output folder are locked.

.PARAMETER Output
    Output folder, relative to the repo root or absolute. Default: Builds\Anvil.

.PARAMETER Clean
    Delete the output folder before publishing so no stale files stay behind.

.PARAMETER Run
    Start the editor after a successful build.

.EXAMPLE
    .\Tools\Scripts\build-editor.ps1
    .\Tools\Scripts\build-editor.ps1 -Clean -Run
    .\Tools\Scripts\build-editor.ps1 -Output Builds\Anvil-Test
    powershell -ExecutionPolicy Bypass -File .\Tools\Scripts\build-editor.ps1   # when scripts are blocked
#>
[CmdletBinding()]
param(
    [string]$Output = 'Builds\Anvil',
    [switch]$Clean,
    [switch]$Run
)

$ErrorActionPreference = 'Stop'

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $root 'Editor\Anvil\Anvil.csproj'
$runtime = 'win-x64'
$outputDir = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $root $Output }
$outputDir = [System.IO.Path]::GetFullPath($outputDir)

function Invoke-Step([string]$title, [scriptblock]$command) {
    Write-Host ''
    Write-Host "==> $title" -ForegroundColor Cyan
    & $command
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: $title (exit code $LASTEXITCODE)" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

# --- Checks ------------------------------------------------------------------------------------

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host 'The .NET SDK (dotnet) was not found. Install the .NET 10 SDK and try again.' -ForegroundColor Red
    exit 1
}

$rootPrefix = $root.TrimEnd('\') + '\'
if (-not $outputDir.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    Write-Warning "$outputDir is outside the repo. Anvil looks for Engine\Content in the folders above its exe, so asset import, saving and game builds won't work from there."
}
$engineDir = Join-Path $root 'Engine'
if ($outputDir.StartsWith($engineDir.TrimEnd('\') + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
    Write-Host 'Choose an output folder outside Engine\ (it would be picked up as engine content).' -ForegroundColor Red
    exit 1
}

if (Get-Process -Name Anvil -ErrorAction SilentlyContinue) {
    Write-Warning 'Anvil is running; files it has open in the output folder can not be replaced.'
}

Write-Host "Anvil Release build -> $outputDir"
& dotnet --version | ForEach-Object { Write-Host "dotnet SDK $_" }

# --- Build -------------------------------------------------------------------------------------

if ($Clean -and (Test-Path $outputDir)) {
    Write-Host ''
    Write-Host "==> Removing $outputDir" -ForegroundColor Cyan
    Remove-Item -LiteralPath $outputDir -Recurse -Force
}

# dotnet-mgcb (pinned in Engine/dotnet-tools.json) for the MonoGame content build.
Push-Location $engineDir
try {
    Invoke-Step 'Restoring dotnet tools' { dotnet tool restore }
}
finally {
    Pop-Location
}

Invoke-Step 'Restoring NuGet packages' {
    dotnet restore $project -r $runtime
}

Invoke-Step 'Publishing Anvil (Release, self-contained)' {
    dotnet publish $project -c Release -r $runtime --self-contained true --no-restore `
        -o $outputDir /property:GenerateFullPaths=true '/consoleloggerparameters:NoSummary;ForceNoAlign'
}

# Engine.csproj keeps steam_appid.txt out of published games, but the editor is a development
# launch: without it Steam can't start outside the Steam client (SteamService.Connect).
Copy-Item -LiteralPath (Join-Path $engineDir 'thirdparty\steam\steam_appid.txt') -Destination $outputDir -Force

# --- Verify ------------------------------------------------------------------------------------

$exe = Join-Path $outputDir 'Anvil.exe'
if (-not (Test-Path $exe)) {
    Write-Host "Publish finished but $exe is missing." -ForegroundColor Red
    exit 1
}
if (-not (Test-Path (Join-Path $outputDir 'Content'))) {
    Write-Warning 'The Content folder is missing from the output; the engine viewport will fail to load assets.'
}

$sizeMb = [math]::Round(((Get-ChildItem $outputDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host ''
Write-Host "Done: $exe ($sizeMb MB)" -ForegroundColor Green

if ($Run) {
    Start-Process -FilePath $exe -WorkingDirectory $outputDir
}
