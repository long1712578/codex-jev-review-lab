Set-StrictMode -Version Latest

function Assert-JevPlainPath {
    param([Parameter(Mandatory)][string]$Path)
    $currentPath = [IO.Path]::GetFullPath($Path)
    while ($null -ne $currentPath) {
        try {
            $item = Get-Item -LiteralPath $currentPath -Force -ErrorAction Stop
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'linked_path_not_allowed'
            }
        }
        catch {
            if ($_.CategoryInfo.Category -ne [Management.Automation.ErrorCategory]::ObjectNotFound) { throw }
        }
        $parent = [IO.Directory]::GetParent($currentPath)
        $currentPath = if ($null -eq $parent) { $null } else { $parent.FullName }
    }
}

function Get-JevChildPath {
    param([Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)][string]$RelativePath)
    if ([IO.Path]::IsPathRooted($RelativePath)) { throw 'absolute_package_entry_not_allowed' }
    $rootPath = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Root))
    $fullPath = [IO.Path]::GetFullPath((Join-Path $rootPath $RelativePath))
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $prefix = $rootPath + [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($prefix, $comparison)) { throw 'path_outside_package' }
    Assert-JevPlainPath $fullPath
    return $fullPath
}

function Invoke-JevProcess {
    param([Parameter(Mandatory)][string]$FilePath, [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string[]]$CommandArguments)
    $startInfo = [Diagnostics.ProcessStartInfo]::new($FilePath)
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = [Text.Encoding]::UTF8
    $startInfo.StandardErrorEncoding = [Text.Encoding]::UTF8
    [void]$startInfo.Environment.Remove('TYPESAFE_API_KEY')
    foreach ($argument in $CommandArguments) { $startInfo.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($startInfo)
    try {
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw 'offline_process_failed' }
        return [pscustomobject]@{ StdOut = $stdout; StdErr = $stderr }
    }
    finally { $process.Dispose() }
}

function Invoke-JevDotNet {
    param([Parameter(Mandatory)][string]$WorkingDirectory, [Parameter(Mandatory)][string[]]$CommandArguments)
    $result = Invoke-JevProcess 'dotnet' $WorkingDirectory $CommandArguments
    if (-not [string]::IsNullOrWhiteSpace($result.StdOut)) { Write-Host $result.StdOut.TrimEnd() }
    if (-not [string]::IsNullOrWhiteSpace($result.StdErr)) { Write-Host $result.StdErr.TrimEnd() }
}
