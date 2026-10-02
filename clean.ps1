<#
.SYNOPSIS
    Deletes the bin/ and obj/ build output folders of every project in the repo.

.DESCRIPTION
    Only folders that sit next to a .csproj or a MonoGame .mgcb file are removed
    (Engine, Engine/Content, Editor/Anvil, Vista, ...), so stale .xnb files and
    other leftover build output can't keep removed assets "working".
    Close the engine/Anvil first, otherwise locked files can't be deleted.

.EXAMPLE
    .\clean.ps1            # delete
    .\clean.ps1 -WhatIf    # only list what would be deleted
#>
[CmdletBinding(SupportsShouldProcess)]
param()

$root = $PSScriptRoot

$projectDirs = Get-ChildItem -Path $root -Recurse -File -Include *.csproj, *.mgcb |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj|\.git)[\\/]' } |
    ForEach-Object { $_.DirectoryName } |
    Sort-Object -Unique

$failed = 0
foreach ($dir in $projectDirs) {
    foreach ($name in 'bin', 'obj') {
        $target = Join-Path $dir $name
        if (-not (Test-Path $target)) { continue }

        $relative = $target.Substring($root.Length).TrimStart('\', '/')
        if ($PSCmdlet.ShouldProcess($relative, 'Remove')) {
            try {
                Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction Stop
                Write-Host "Removed $relative"
            }
            catch {
                Write-Warning "Could not remove ${relative}: $($_.Exception.Message)"
                $failed++
            }
        }
    }
}

if ($failed -gt 0) {
    Write-Warning "$failed folder(s) could not be removed. Is the engine or Anvil still running?"
    exit 1
}
