<#
.SYNOPSIS
Register Anvil's MCP with Claude Code, Cursor, Codex or VS Code.
.DESCRIPTION
Run with no arguments for a menu. Arguments are forwarded to the installer CLI.
Requires the .NET 10 SDK; no engine or content build is needed.
.EXAMPLE
.\Tools\Scripts\install-mcp.ps1
.\Tools\Scripts\install-mcp.ps1 --harness claudecode,cursor,codex --dry-run
.\Tools\Scripts\install-mcp.ps1 --harness codex --yes
#>
$ErrorActionPreference = 'Stop'
$installerRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$projectPath = Join-Path $installerRoot 'Tools\McpInstaller\McpInstaller.csproj'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error 'Install the .NET 10 SDK, or run a published anvil-mcp.exe installer.'
    exit 1
}
& dotnet run --project $projectPath -- @args
exit $LASTEXITCODE
