param([string]$Dataset = 'datasets/smoke', [string]$Output = '')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $repoRoot
try {
    & dotnet build JevReview.slnx --no-restore --verbosity quiet
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $commandArgs = @('classify', '--dataset', $Dataset, '--mode', 'fake')
    if ($Output) { $commandArgs += @('--output', $Output) }
    & dotnet run --project src/JevReview.Cli --no-build -- @commandArgs
    $commandExit = $LASTEXITCODE
}
finally { Pop-Location }
exit $commandExit
