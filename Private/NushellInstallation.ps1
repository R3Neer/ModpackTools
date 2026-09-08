function Get-MpNushellConfigPath {
    if ($IsWindows -and $env:APPDATA) { return Join-Path $env:APPDATA 'nushell\config.nu' }
    if ($env:XDG_CONFIG_HOME) { return Join-Path $env:XDG_CONFIG_HOME 'nushell/config.nu' }
    return Join-Path $HOME '.config/nushell/config.nu'
}

function Set-MpNushellConfigBlock {
    param([Parameter(Mandatory)][string]$ConfigPath, [Parameter(Mandatory)][string]$ModulePath)
    $begin = '# >>> ModpackTools Nushell >>>'
    $end = '# <<< ModpackTools Nushell <<<'
    $normalized = $ModulePath.Replace('\','/')
    if ($normalized.Contains("'")) {
        Throw-MpError -Message 'The Nushell adapter path contains an unsupported apostrophe' -Details $ModulePath -Hint 'install ModpackTools in a path without apostrophes' -ErrorId 'Nushell.InvalidAdapterPath' -Category InvalidArgument -TargetObject $ModulePath
    }
    $block = "$begin`nuse '$normalized' main`n$end"
    $text = if (Test-Path -LiteralPath $ConfigPath -PathType Leaf) { Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 } else { '' }
    $pattern = '(?ms)^' + [regex]::Escape($begin) + '.*?^' + [regex]::Escape($end) + '\s*'
    $text = [regex]::Replace($text, $pattern, '').TrimEnd()
    if ($text) { $text += "`n`n" }
    $text += "$block`n"
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $ConfigPath))
    Write-Utf8TextFileAtomic -Path $ConfigPath -Text $text
}

function Install-MpNushellAdapter {
    param([Parameter(Mandatory)][string]$ModuleRoot)
    $nu = Get-Command nu.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $nu) { $nu = Get-Command nu -ErrorAction SilentlyContinue | Select-Object -First 1 }
    if (-not $nu) { return [pscustomobject]@{ Installed=$false; Reason='Nushell is not installed or is not on PATH.' } }

    $modulePath = Join-Path $ModuleRoot 'Nushell/modpack.nu'
    $bridgePath = Join-Path $ModuleRoot 'Nushell/Invoke-ModpackBridge.ps1'
    foreach ($path in @($modulePath,$bridgePath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            Throw-MpError -Message 'A required Nushell adapter file is missing' -Details $path -Hint 'reinstall ModpackTools from a complete package' -ErrorId 'Nushell.AdapterFileMissing' -Category ObjectNotFound -TargetObject $path
        }
    }

    $configPath = Get-MpNushellConfigPath
    Set-MpNushellConfigBlock -ConfigPath $configPath -ModulePath $modulePath
    return [pscustomobject]@{ Installed=$true; ConfigPath=$configPath; ModulePath=$modulePath; NuPath=$nu.Source }
}
