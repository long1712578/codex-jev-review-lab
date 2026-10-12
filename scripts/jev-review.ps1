$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'src/JevReview.Cli/JevReview.Cli.csproj'
Push-Location -LiteralPath $repoRoot
try {
    & dotnet run --project $projectPath --no-restore -- @args
    $commandExit = $LASTEXITCODE
}
finally { Pop-Location }
exit $commandExit
