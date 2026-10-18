<#
.SYNOPSIS
Register Anvil's MCP with Claude Code, Cursor, Codex or VS Code.
.DESCRIPTION
Run with no arguments for a menu. Arguments are forwarded to the installer CLI.
Requires the .NET 10 SDK; no engine or content build is needed.
.EXAMPLE
.\install-mcp.ps1
.\install-mcp.ps1 --harness claudecode,cursor,codex --dry-run
.\install-mcp.ps1 --harness codex --yes
#>
$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'Tools\McpInstaller\McpInstaller.csproj'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error 'Install the .NET 10 SDK, or run a published anvil-mcp.exe installer.'
    exit 1
}
& dotnet run --project $projectPath -- @args
exit $LASTEXITCODE
