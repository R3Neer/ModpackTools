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
    $hasBom = $false; $text = ''
    if ([IO.File]::Exists($ConfigPath)) {
        if ((Get-Item -LiteralPath $ConfigPath).Length -gt 4MB) { Throw-MpError -Message 'Nushell configuration exceeds the edit limit' -Hint 'edit the marked import block manually' -ErrorId 'Nushell.ConfigLimit' -Category LimitsExceeded -TargetObject $ConfigPath }
        $bytes = [IO.File]::ReadAllBytes($ConfigPath)
        $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
        $offset = if ($hasBom) { 3 } else { 0 }
        try { $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes, $offset, $bytes.Length - $offset) }
        catch { Throw-MpError -Message 'Nushell configuration is not valid UTF-8' -Hint 'repair its encoding before changing the import block' -ErrorId 'Nushell.InvalidConfigEncoding' -Category InvalidData -TargetObject $ConfigPath }
    }
    $newline = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
    $block = "$begin${newline}use '$normalized' main${newline}$end${newline}"
    $starts = [regex]::Matches($text, '(?m)^' + [regex]::Escape($begin) + '\r?$')
    $ends = [regex]::Matches($text, '(?m)^' + [regex]::Escape($end) + '\r?$')
    if ($starts.Count -eq 1 -and $ends.Count -eq 1 -and $starts[0].Index -lt $ends[0].Index) {
        $after = $ends[0].Index + $ends[0].Length
        if ($after -lt $text.Length -and $text[$after] -eq "`n") { $after++ }
        $text = $text.Substring(0, $starts[0].Index) + $block + $text.Substring($after)
    } elseif (-not $starts.Count -and -not $ends.Count) {
        if ($text -and -not $text.EndsWith("`n")) { $text += $newline }
        $text += $block
    } else {
        Throw-MpError -Message 'Nushell configuration has ambiguous ModpackTools import markers' -Hint 'keep exactly one complete marked block before installing' -ErrorId 'Nushell.InvalidConfigBlock' -Category InvalidData -TargetObject $ConfigPath
    }
    if ($hasBom) { $text = [string][char]0xFEFF + $text }
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $ConfigPath))
    Write-Utf8TextFileAtomic -Path $ConfigPath -Text $text
}

function Install-MpNushellAdapter {
    param([Parameter(Mandatory)][string]$ModuleRoot, [string]$ConfigPath)
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

    $configPath = if ($ConfigPath) { [IO.Path]::GetFullPath($ConfigPath) } else { Get-MpNushellConfigPath }
    Set-MpNushellConfigBlock -ConfigPath $configPath -ModulePath $modulePath
    return [pscustomobject]@{ Installed=$true; ConfigPath=$configPath; ModulePath=$modulePath; NuPath=$nu.Source }
}
