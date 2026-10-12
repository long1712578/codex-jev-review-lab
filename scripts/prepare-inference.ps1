param([ValidateSet('dev', 'holdout')][string]$Dataset = 'holdout', [string]$Output = 'runs/inference-bundle')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$bundlePath = [IO.Path]::GetFullPath($Output, $repoRoot)
if (Test-Path -LiteralPath $bundlePath) { throw 'Output already exists; choose a new inference bundle directory.' }
New-Item -ItemType Directory -Path $bundlePath | Out-Null
& dotnet publish (Join-Path $repoRoot 'src/JevReview.Cli') -c Release --no-restore -p:UseAppHost=false -o (Join-Path $bundlePath 'app')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
New-Item -ItemType Directory -Path (Join-Path $bundlePath 'dataset'), (Join-Path $bundlePath 'rubrics'), (Join-Path $bundlePath 'out') | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot "datasets/$Dataset/items.json") -Destination (Join-Path $bundlePath 'dataset/items.json')
Copy-Item -LiteralPath (Join-Path $repoRoot 'rubrics/refactor-review.v1.json') -Destination (Join-Path $bundlePath 'rubrics/refactor-review.v1.json')
Write-Output "Inference bundle: $bundlePath. Contains only app, unlabeled input, rubric and empty output. Mount these folders individually; never mount gold or the host root."
