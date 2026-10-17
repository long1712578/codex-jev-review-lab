#requires -Version 7.0
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][string]$ProjectPath,
    [string]$PackageRoot = (Split-Path $PSScriptRoot -Parent)
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'CodexJev.Common.ps1')
$projectRoot = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($ProjectPath))
$packagePath = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($PackageRoot))
Assert-JevPlainPath $projectRoot
Assert-JevPlainPath $packagePath
if (-not (Test-Path -LiteralPath $projectRoot -PathType Container)) { throw 'project_directory_required' }
$gitRoot = (Invoke-JevProcess 'git' $projectRoot @('rev-parse', '--show-toplevel')).StdOut.Trim()
$comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
if (-not $projectRoot.Equals([IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($gitRoot)), $comparison)) {
    throw 'git_repository_root_required'
}
$setupPath = Get-JevChildPath $projectRoot '.jev-review'
if (Test-Path -LiteralPath $setupPath) { throw 'project_setup_already_exists' }
$skills = @('ultrathink', 'orchestrate', 'workflowz', 'jevify')
foreach ($skill in $skills) {
    $skillPath = Get-JevChildPath $projectRoot ".agents/skills/$skill"
    if (Test-Path -LiteralPath $skillPath) { throw 'project_skill_already_exists' }
}

$manifest = Get-Content -LiteralPath (Get-JevChildPath $packagePath 'release.json') -Raw | ConvertFrom-Json
$copyPlan = [Collections.Generic.List[object]]::new()
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $manifest.files) {
    $source = Get-JevChildPath $packagePath $entry.path
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw 'package_file_missing' }
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) {
        throw 'package_hash_mismatch'
    }
    $destination = switch -Regex ($entry.path) {
        '^runtime/[^/]+$' { ".jev-review/$($entry.path)"; break }
        '^\.agents/skills/(ultrathink|orchestrate|workflowz|jevify)/(SKILL\.md|agents/openai\.yaml)$' { $entry.path; break }
        '^rubrics/refactor-review\.v1\.json$' { '.jev-review/rubric.json'; break }
        '^configs/policy\.json$' { '.jev-review/policy.json'; break }
        '^fixtures/fake/dev-responses\.json$' { '.jev-review/fake-responses.json'; break }
        '^datasets/smoke/items\.json$' { '.jev-review/smoke/items.json'; break }
        '^docs/codex-v1-(quickstart|release-notes)\.md$' { ".jev-review/$($entry.path)"; break }
        '^scripts/(Install-CodexJev|CodexJev\.Common)\.ps1$' { $null; break }
        default { throw 'unexpected_package_file' }
    }
    if ($null -ne $destination) {
        $target = Get-JevChildPath $projectRoot $destination
        if (-not $seen.Add($target) -or (Test-Path -LiteralPath $target)) { throw 'installation_file_collision' }
        $copyPlan.Add([pscustomobject]@{ source = $source; target = $target })
    }
}
foreach ($required in @('.jev-review/runtime/jev-review.dll', '.jev-review/runtime/jev-review.deps.json',
    '.jev-review/runtime/jev-review.runtimeconfig.json', '.jev-review/runtime/JevReview.Core.dll',
    '.jev-review/rubric.json', '.jev-review/policy.json', '.jev-review/fake-responses.json', '.jev-review/smoke/items.json')) {
    if (-not $seen.Contains((Get-JevChildPath $projectRoot $required))) { throw 'incomplete_package' }
}
foreach ($skill in $skills) {
    foreach ($part in @('SKILL.md', 'agents/openai.yaml')) {
        if (-not $seen.Contains((Get-JevChildPath $projectRoot ".agents/skills/$skill/$part"))) { throw 'incomplete_package' }
    }
}
if (-not $PSCmdlet.ShouldProcess($projectRoot, "Install Codex + Jev $($manifest.version) ($($copyPlan.Count) files)")) { return }

foreach ($copy in $copyPlan) {
    Assert-JevPlainPath $copy.target
    [void](New-Item -ItemType Directory -Path (Split-Path $copy.target -Parent) -Force)
    Copy-Item -LiteralPath $copy.source -Destination $copy.target
}
$dll = (Join-Path $setupPath 'runtime/jev-review.dll') | ConvertTo-Json -Compress
$workspace = $projectRoot | ConvertTo-Json -Compress
$snippet = @"
[mcp_servers.jev_review]
command = "dotnet"
args = [$dll, "mcp", "--workspace", $workspace]
cwd = $workspace
"@
$snippet | Set-Content -LiteralPath (Join-Path $setupPath 'codex-mcp.toml') -Encoding utf8NoBOM
Copy-Item -LiteralPath (Join-Path $packagePath 'release.json') -Destination (Join-Path $setupPath 'release.json')
$installedFiles = @($copyPlan | ForEach-Object { [IO.Path]::GetRelativePath($projectRoot, $_.target).Replace('\', '/') })
$installedFiles += @('.jev-review/codex-mcp.toml', '.jev-review/release.json', '.jev-review/installation.json')
[ordered]@{ version = $manifest.version; sourceCommit = $manifest.sourceCommit; files = $installedFiles } |
    ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $setupPath 'installation.json') -Encoding utf8NoBOM
Write-Output "Installed Codex + Jev $($manifest.version). Merge .jev-review/codex-mcp.toml into your Codex config, then check /skills and /mcp."
