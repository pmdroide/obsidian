<#
.SYNOPSIS
Publish the standalone Anvil MCP harness installer for Windows x64.
.EXAMPLE
.\build-mcp-installer.ps1
.\build-mcp-installer.ps1 -Output Builds\McpInstaller-Test
#>
[CmdletBinding()]
param([string]$Output = 'Builds\McpInstaller')

$ErrorActionPreference = 'Stop'
$installerRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$installerProject = Join-Path $installerRoot 'Tools\McpInstaller\McpInstaller.csproj'
$installerOutput = [System.IO.Path]::GetFullPath($(if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $installerRoot $Output }))
if (-not $installerOutput.StartsWith($installerRoot.TrimEnd('\') + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Choose a publish output folder inside this checkout, such as Builds\McpInstaller.'
}
foreach ($sourceFolder in @('Engine', 'Editor', 'Vista', 'ContentPipeline', 'Tools', 'Tests')) {
    $sourcePath = Join-Path $installerRoot $sourceFolder
    if ($installerOutput -eq $sourcePath -or $installerOutput.StartsWith($sourcePath + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Choose a publish output folder outside source projects, such as Builds\McpInstaller.'
    }
}
& dotnet publish $installerProject -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $installerOutput
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host ('Installer ready: ' + (Join-Path $installerOutput 'anvil-mcp.exe'))
