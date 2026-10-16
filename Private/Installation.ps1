# Shared by the standalone installer and self-update. No module import required.
function Get-MpUserModuleRoots {
    $systemRoots = @([IO.Path]::GetFullPath((Join-Path $PSHOME 'Modules')).TrimEnd('\','/'))
    if ($IsWindows) {
        foreach ($base in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
            if ($base) { foreach ($relative in @('PowerShell/Modules','WindowsPowerShell/Modules')) { $systemRoots += [IO.Path]::GetFullPath((Join-Path $base $relative)).TrimEnd('\','/') } }
        }
        $systemRoots += [IO.Path]::GetFullPath((Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/Modules')).TrimEnd('\','/')
    }
    $seen = @{}
    foreach ($entry in ($env:PSModulePath -split [IO.Path]::PathSeparator)) {
        if (-not $entry) { continue }
        $path = [IO.Path]::GetFullPath($entry).TrimEnd('\','/')
        if ($path -notin $systemRoots -and -not $seen.ContainsKey($path)) {
            $seen[$path] = $true; $path
        }
    }
}

function Resolve-MpInstallDestination {
    param([string]$InstallPath)
    if (-not $InstallPath) {
        $roots = @(Get-MpUserModuleRoots)
        if (-not $roots.Count) { Throw-MpError -Message 'No user module directory was found in PSModulePath' -Hint 'configure a user module directory or supply -InstallPath' -ErrorId 'Installation.NoUserRoot' }
        $existing = @($roots | Where-Object { [IO.File]::Exists((Join-Path $_ 'ModpackTools/ModpackTools.psd1')) -and -not (Test-Path -LiteralPath (Join-Path $_ 'ModpackTools/.git')) })
        $InstallPath = Join-Path $(if ($existing.Count) { $existing[0] } else { $roots[0] }) 'ModpackTools'
    }
    $destination = [IO.Path]::GetFullPath($InstallPath).TrimEnd('\','/')
    if ([IO.Path]::GetFileName($destination) -ne 'ModpackTools') {
        Throw-MpError -Message 'Installation must target a ModpackTools directory' -Hint 'supply a separate module directory with -InstallPath; add its parent to PSModulePath for import by name' -ErrorId 'Installation.InvalidTarget'
    }
    if (Test-Path -LiteralPath (Join-Path $destination '.git')) { Throw-MpError -Message 'Installation cannot replace a Git source checkout' -Hint 'use a separate installed module directory' -ErrorId 'Installation.SourceCheckout' }
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

function Get-MpInstallationState {
    param([Parameter(Mandatory)][string]$ModuleRoot)
    $path = Join-Path $ModuleRoot 'installation.json'
    if (-not [IO.File]::Exists($path)) { return $null }
    try {
        if ((Get-Item -LiteralPath $path).Length -gt 64KB) { throw 'Installation state exceeds its size limit.' }
        $state = [IO.File]::ReadAllText($path) | ConvertFrom-Json
        if ($state.SchemaVersion -ne 1 -or $state.InstallPath -isnot [string] -or $state.ConfigureNushellAdapter -isnot [bool] -or
            ($null -ne $state.NushellConfigPath -and $state.NushellConfigPath -isnot [string]) -or
            -not [IO.Path]::IsPathFullyQualified($state.InstallPath) -or
            [IO.Path]::GetFullPath($state.InstallPath).TrimEnd('\','/') -ne [IO.Path]::GetFullPath($ModuleRoot).TrimEnd('\','/') -or
            ($state.ConfigureNushellAdapter -and ($state.NushellConfigPath -isnot [string] -or -not [IO.Path]::IsPathFullyQualified($state.NushellConfigPath)))) { throw 'Invalid installation state.' }
        return $state
    } catch {
        Throw-MpError -Message 'Installed module state is invalid' -Details $_.Exception.Message -Hint 'repair installation.json before updating; do not select a different installation silently' -ErrorId 'Installation.InvalidState' -Category InvalidData -TargetObject $path
    }
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
