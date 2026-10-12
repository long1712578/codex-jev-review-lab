param(
    [Parameter(Mandatory)][string]$Run,
    [Parameter(Mandatory)][string]$Gold,
    [Parameter(Mandatory)][string]$Output,
    [string]$Baseline = ''
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$commandArgs = @('evaluate', '--run', $Run, '--gold', $Gold, '--output', $Output, '--inference-root', $repoRoot)
if ($Baseline) { $commandArgs += @('--baseline', $Baseline) }
& dotnet run --project (Join-Path $repoRoot 'src/JevReview.Cli') --no-build -- @commandArgs
exit $LASTEXITCODE
