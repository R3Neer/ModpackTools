# Shared by the standalone installer and self-update. No module import required.
function Get-MpUserModuleRoots {
    $profileRoot = [IO.Path]::GetFullPath($HOME).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    $documentsRoot = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'PowerShell/Modules'))
    $seen = @{}
    foreach ($entry in ($env:PSModulePath -split [IO.Path]::PathSeparator)) {
        if (-not $entry) { continue }
        $path = [IO.Path]::GetFullPath($entry).TrimEnd('\','/')
        if (($path.StartsWith($profileRoot, [StringComparison]::OrdinalIgnoreCase) -or $path -eq $documentsRoot) -and -not $seen.ContainsKey($path)) {
            $seen[$path] = $true; $path
        }
    }
}

function Resolve-MpInstallDestination {
    param([string]$InstallPath)
    $roots = @(Get-MpUserModuleRoots)
    if (-not $roots.Count) { Throw-MpError -Message 'No user module directory was found in PSModulePath' -Hint 'configure a user module directory' -ErrorId 'Installation.NoUserRoot' }
    if (-not $InstallPath) {
        $existing = @($roots | Where-Object { [IO.File]::Exists((Join-Path $_ 'ModpackTools/ModpackTools.psd1')) })
        $InstallPath = Join-Path $(if ($existing.Count) { $existing[0] } else { $roots[0] }) 'ModpackTools'
    }
    $destination = [IO.Path]::GetFullPath($InstallPath).TrimEnd('\','/')
    if ([IO.Path]::GetFileName($destination) -ne 'ModpackTools' -or (Split-Path -Parent $destination) -notin $roots) {
        Throw-MpError -Message 'Installation must target a ModpackTools directory directly inside a user PSModulePath entry' -Hint 'select the installed user module directory' -ErrorId 'Installation.InvalidTarget'
    }
    # Check ancestors too: a regular leaf beneath a junction is not a safe target.
    $ancestor = $destination
    while ($ancestor) {
        if (Test-Path -LiteralPath $ancestor) {
            if (Test-MpFileSystemLink (Get-Item -LiteralPath $ancestor -Force)) { Throw-MpError -Message "Installer target is a linked path: $ancestor" -Hint 'select a regular user module directory' -ErrorId 'Installation.LinkedPath' }
        }
        $ancestor = Split-Path -Parent $ancestor
    }
    return $destination
}

function Invoke-MpInstallProcess {
    param([string[]]$Arguments)
    $start = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    foreach ($argument in @('-NoProfile','-NonInteractive') + $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) { Throw-MpError -Message 'Installation process failed' -Details "$($stderr.Result) $($stdout.Result)" -Hint 'resolve the installation error and retry' -ErrorId 'Installation.ProcessFailed' }
        return $stdout.Result
    } finally { $process.Dispose() }
}
