# Copies the apworld into a local Archipelago clone so it can be generated and
# tested. The clone lives at ./Archipelago and is gitignored - it is upstream's
# code, not ours.
#
# Usage: powershell -File tools/ap-sync.ps1

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$src = Join-Path $repo 'apworld\alttl'
$clone = Join-Path $repo 'Archipelago'
$dest = Join-Path $clone 'worlds\alttl'

if (-not (Test-Path $src)) { throw "No apworld at $src" }
if (-not (Test-Path $clone)) {
    throw "No Archipelago clone at $clone. Clone https://github.com/ArchipelagoMW/Archipelago there."
}

if (Test-Path $dest) { Remove-Item -Recurse -Force $dest }
Copy-Item -Recurse $src $dest
Get-ChildItem -Recurse -Directory -Filter '__pycache__' $dest |
    ForEach-Object { Remove-Item -Recurse -Force $_.FullName }

Write-Output "Synced apworld/alttl -> $dest"
Write-Output "Test:     cd Archipelago; python -m unittest discover -s worlds/alttl/test -t ."
Write-Output "Generate: cd Archipelago; python Generate.py --player_files_path <dir with a yaml>"
