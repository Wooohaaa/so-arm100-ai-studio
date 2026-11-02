$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repoRoot 'skills\so-arm100-visual-control'
$destination = Join-Path $env:USERPROFILE '.codex\skills\so-arm100-visual-control'

if (-not (Test-Path (Join-Path $source 'SKILL.md'))) {
    throw "Skill source not found: $source"
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
New-Item -ItemType Directory -Force -Path $destination | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $destination -Recurse -Force
Write-Output "Installed SO-100 visual control skill to $destination"
