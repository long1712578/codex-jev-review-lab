#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '1.0.0',
    [string]$OutputDirectory,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$SkipChecks
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'CodexJev.Common.ps1')
$sourceRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $sourceRoot "runs/releases/$Version" }
$releaseRoot = [IO.Path]::GetFullPath($OutputDirectory)
Assert-JevPlainPath $releaseRoot
if ((Test-Path -LiteralPath $releaseRoot) -and @(Get-ChildItem -LiteralPath $releaseRoot -Force).Count -gt 0) {
    throw 'release_directory_not_empty'
}

$assets = @(
    '.agents/skills/ultrathink/SKILL.md', '.agents/skills/ultrathink/agents/openai.yaml',
    '.agents/skills/orchestrate/SKILL.md', '.agents/skills/orchestrate/agents/openai.yaml',
    '.agents/skills/workflowz/SKILL.md', '.agents/skills/workflowz/agents/openai.yaml',
    '.agents/skills/jevify/SKILL.md', '.agents/skills/jevify/agents/openai.yaml',
    'rubrics/refactor-review.v1.json', 'configs/policy.json', 'fixtures/fake/dev-responses.json', 'datasets/smoke/items.json',
    'scripts/CodexJev.Common.ps1', 'scripts/Install-CodexJev.ps1',
    'docs/codex-v1-quickstart.md', 'docs/codex-v1-release-notes.md'
)
foreach ($asset in $assets) {
    $assetPath = Get-JevChildPath $sourceRoot $asset
    if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) { throw 'release_asset_missing' }
}

if (-not $SkipChecks) {
    Invoke-JevDotNet $sourceRoot @('restore', 'JevReview.slnx')
    Invoke-JevDotNet $sourceRoot @('build', 'JevReview.slnx', '-c', $Configuration, '--no-restore')
    Invoke-JevDotNet $sourceRoot @('test', 'JevReview.slnx', '-c', $Configuration, '--no-build', '--no-restore')
    Invoke-JevDotNet $sourceRoot @('run', '--project', 'src/JevReview.Cli', '-c', $Configuration, '--no-build', '--',
        'classify', '--dataset', 'datasets/smoke', '--mode', 'fake')
}

[void](New-Item -ItemType Directory -Path $releaseRoot -Force)
$bundleRoot = Join-Path $releaseRoot "codex-jev-$Version"
[void](New-Item -ItemType Directory -Path $bundleRoot)
Invoke-JevDotNet $sourceRoot @('publish', 'src/JevReview.Cli', '-c', $Configuration, '--no-build', '--no-restore',
    '-p:UseAppHost=false', '-o', (Join-Path $bundleRoot 'runtime'))
foreach ($asset in $assets) {
    $from = Get-JevChildPath $sourceRoot $asset
    $to = Get-JevChildPath $bundleRoot $asset
    [void](New-Item -ItemType Directory -Path (Split-Path $to -Parent) -Force)
    Copy-Item -LiteralPath $from -Destination $to
}

$fileHashes = @(Get-ChildItem -LiteralPath $bundleRoot -File -Recurse -Force | Sort-Object FullName | ForEach-Object {
    Assert-JevPlainPath $_.FullName
    [ordered]@{
        path = [IO.Path]::GetRelativePath($bundleRoot, $_.FullName).Replace('\', '/')
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
})
$sourceCommit = (Invoke-JevProcess 'git' $sourceRoot @('rev-parse', 'HEAD')).StdOut.Trim()
$sourceDirty = -not [string]::IsNullOrWhiteSpace((Invoke-JevProcess 'git' $sourceRoot @('status', '--porcelain')).StdOut)
$manifest = [ordered]@{
    version = $Version; sourceCommit = $sourceCommit; sourceDirty = $sourceDirty
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); runtime = '.NET 10'; files = $fileHashes
}
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $bundleRoot 'release.json') -Encoding utf8NoBOM
$zipPath = Join-Path $releaseRoot "codex-jev-$Version.zip"
[IO.Compression.ZipFile]::CreateFromDirectory($bundleRoot, $zipPath)
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$zipHash  $([IO.Path]::GetFileName($zipPath))" | Set-Content -LiteralPath ($zipPath + '.sha256') -Encoding utf8NoBOM
[ordered]@{ version = $Version; archive = $zipPath; sha256 = $zipHash; checksSkipped = [bool]$SkipChecks } | ConvertTo-Json
