function New-MpDoctorCheck {
    param(
        [Parameter(Mandatory)][string]$Section,
        [Parameter(Mandatory)][ValidateSet('pass', 'warn', 'fail', 'info')][string]$Status,
        [Parameter(Mandatory)][string]$Label,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Value,
        [string]$Detail
    )
    return [pscustomobject]@{ Section = $Section; Status = $Status; Label = $Label; Value = $Value; Detail = $Detail }
}

function Test-MpConfigurationWritable {
    $directory = Get-ModpackToolsConfigDirectory
    $probe = Join-Path $directory ('.write-test-' + [guid]::NewGuid().ToString('N'))
    try {
        [System.IO.Directory]::CreateDirectory($directory) | Out-Null
        [System.IO.File]::WriteAllText($probe, 'test', [System.Text.UTF8Encoding]::new($false))
        return $true
    }
    catch { return $false }
    finally { if (Test-Path -LiteralPath $probe) { Remove-Item -LiteralPath $probe -Force } }
}

function Test-MpPackwizInvocation {
    param([Parameter(Mandatory)][string]$Path)
    try {
        [void](Invoke-NativeCommandChecked -FilePath $Path -Arguments @('--help') -WorkingDirectory ([System.IO.Path]::GetTempPath()))
        return $true
    }
    catch { return $false }
}

function Get-MpMinecraftJavaCheck {
    $roaming = [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)
    if ([string]::IsNullOrWhiteSpace($roaming)) {
        return New-MpDoctorCheck -Section 'OPTIONAL' -Status warn -Label 'Minecraft Java' -Value 'Not detected' -Detail 'The standard application-data directory is unavailable.'
    }
    $minecraft = Join-Path $roaming '.minecraft'
    $versionsDirectory = Join-Path $minecraft 'versions'
    $versions = @()
    if (Test-Path -LiteralPath $versionsDirectory -PathType Container) {
        $versions = @(Get-ChildItem -LiteralPath $versionsDirectory -Directory -ErrorAction SilentlyContinue | Where-Object {
            Test-Path -LiteralPath (Join-Path $_.FullName "$($_.Name).json") -PathType Leaf
        })
    }
    if ($versions.Count) {
        $label = if ($versions.Count -eq 1) { '1 installed version' } else { "$($versions.Count) installed versions" }
        return New-MpDoctorCheck -Section 'OPTIONAL' -Status pass -Label 'Minecraft Java' -Value $label -Detail $minecraft
    }
    if (Test-Path -LiteralPath $minecraft -PathType Container) {
        return New-MpDoctorCheck -Section 'OPTIONAL' -Status warn -Label 'Minecraft Java' -Value 'No installed version detected' -Detail "Launcher data exists at '$minecraft'. Launch Java Edition once to install a version."
    }
    return New-MpDoctorCheck -Section 'OPTIONAL' -Status warn -Label 'Minecraft Java' -Value 'No standard installation detected' -Detail 'Custom launcher installations may not be discovered automatically.'
}

function Get-MpDoctorReport {
    $checks = [System.Collections.Generic.List[object]]::new()
    $checks.Add((New-MpDoctorCheck -Section 'SYSTEM' -Status pass -Label 'PowerShell' -Value $PSVersionTable.PSVersion.ToString()))
    $checks.Add((New-MpDoctorCheck -Section 'SYSTEM' -Status pass -Label 'ModpackTools' -Value $script:ModuleVersion -Detail $script:ModuleRoot))
    $configurationWritable = Test-MpConfigurationWritable
    $checks.Add((New-MpDoctorCheck -Section 'SYSTEM' -Status $(if ($configurationWritable) { 'pass' } else { 'fail' }) -Label 'Configuration' -Value $(if ($configurationWritable) { 'Writable' } else { 'Not writable' }) -Detail (Get-ModpackToolsConfigPath)))

    try { $packwiz = Resolve-MpPackwiz }
    catch {
        $checks.Add((New-MpDoctorCheck -Section 'PACKWIZ' -Status fail -Label 'Executable' -Value 'Unsupported platform' -Detail $_.Exception.Message))
        $packwiz = $null
    }
    if ($packwiz) {
        if (-not $packwiz.Available) {
            $value = if ($packwiz.Source -eq 'configured') { 'Configured path is missing' } else { 'Not found' }
            $checks.Add((New-MpDoctorCheck -Section 'PACKWIZ' -Status fail -Label 'Executable' -Value $value -Detail $packwiz.Path))
        }
        else {
            $checks.Add((New-MpDoctorCheck -Section 'PACKWIZ' -Status pass -Label 'Executable' -Value $packwiz.Path -Detail "Source: $($packwiz.Source)"))
            if ($packwiz.Version) { $checks.Add((New-MpDoctorCheck -Section 'PACKWIZ' -Status pass -Label 'Managed build' -Value $packwiz.Version)) }
            $working = Test-MpPackwizInvocation -Path $packwiz.Path
            $checks.Add((New-MpDoctorCheck -Section 'PACKWIZ' -Status $(if ($working) { 'pass' } else { 'fail' }) -Label 'Invocation' -Value $(if ($working) { 'Working' } else { 'Failed' })))
        }
    }

    $configReadable = $true
    try { $config = Get-ModpackToolsConfig }
    catch {
        $checks.Add((New-MpDoctorCheck -Section 'PROJECT ROOT' -Status fail -Label 'Root' -Value 'Configuration is invalid' -Detail $_.Exception.Message))
        $configReadable = $false
    }
    if ($configReadable) {
        if (-not $config.ContainsKey('Root') -or [string]::IsNullOrWhiteSpace([string]$config.Root)) {
            $checks.Add((New-MpDoctorCheck -Section 'PROJECT ROOT' -Status fail -Label 'Root' -Value 'Not configured'))
        }
        else {
            $root = [System.IO.Path]::GetFullPath([string]$config.Root)
            if (-not (Test-Path -LiteralPath $root -PathType Container)) {
                $checks.Add((New-MpDoctorCheck -Section 'PROJECT ROOT' -Status fail -Label 'Root' -Value 'Directory does not exist' -Detail $root))
            }
            else {
                $checks.Add((New-MpDoctorCheck -Section 'PROJECT ROOT' -Status pass -Label 'Root' -Value $root))
                try {
                    $projects = @(Get-ModpackProjects)
                    $value = if ($projects.Count -eq 1) { '1 project discovered' } else { "$($projects.Count) projects discovered" }
                    $checks.Add((New-MpDoctorCheck -Section 'PROJECT ROOT' -Status pass -Label 'Discovery' -Value $value))
                }
                catch { $checks.Add((New-MpDoctorCheck -Section 'PROJECT ROOT' -Status fail -Label 'Discovery' -Value 'Failed' -Detail $_.Exception.Message)) }
            }
        }
    }
    $active = if ($script:ActiveProjectId) { $script:ActiveProjectId } else { 'None in this session' }
    $checks.Add((New-MpDoctorCheck -Section 'PROJECT ROOT' -Status info -Label 'Active' -Value $active))

    $git = Get-Command git -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($git) {
        $gitVersion = try { (& $git.Source --version 2>$null) -replace '^git version\s+', '' } catch { 'Found' }
        $checks.Add((New-MpDoctorCheck -Section 'OPTIONAL' -Status pass -Label 'Git' -Value ([string]$gitVersion) -Detail $git.Source))
    }
    else { $checks.Add((New-MpDoctorCheck -Section 'OPTIONAL' -Status warn -Label 'Git' -Value 'Not found')) }
    $checks.Add((Get-MpMinecraftJavaCheck))

    $failures = @($checks | Where-Object Status -eq 'fail').Count
    $warnings = @($checks | Where-Object Status -eq 'warn').Count
    return [pscustomobject]@{ Checks = @($checks); Failures = $failures; Warnings = $warnings }
}

function Confirm-MpDoctorAction {
    param([Parameter(Mandatory)][string]$Prompt, [bool]$Default = $true, [switch]$Yes)
    if ($Yes) { return $Default }
    $suffix = if ($Default) { '[Y/n]' } else { '[y/N]' }
    $answer = Read-Host "$Prompt $suffix"
    if ([string]::IsNullOrWhiteSpace($answer)) { return $Default }
    return $answer.Trim().ToLowerInvariant() -in @('y', 'yes')
}

function Repair-MpDoctorEnvironment {
    param([switch]$Yes)

    $packwiz = Resolve-MpPackwiz
    if (-not $packwiz.Available -and (Confirm-MpDoctorAction -Prompt 'Install the verified Packwiz build recommended for this ModpackTools release?' -Default $true -Yes:$Yes)) {
        Write-MpStep 'Downloading and verifying Packwiz...'
        $installed = Install-MpManagedPackwiz
        Write-MpSuccess "Packwiz $($installed.Version) installed at $($installed.Path)"
        $addToPath = Confirm-MpDoctorAction -Prompt 'Add the Packwiz directory to your user PATH?' -Default $false -Yes:$Yes
        if ($addToPath) {
            [void](Add-MpDirectoryToUserPath -Directory (Split-Path -Parent $installed.Path))
            [void](Set-ModpackToolsConfigValue -Name packwiz -Value auto)
            Write-MpSuccess 'Packwiz was added to the user PATH.'
        }
        else {
            [void](Set-ModpackToolsConfigValue -Name packwiz -Value $installed.Path)
            Write-MpInfo 'Packwiz was saved as the ModpackTools executable without changing PATH.'
        }
    }

    $config = Get-ModpackToolsConfig
    $rootMissing = -not $config.ContainsKey('Root') -or [string]::IsNullOrWhiteSpace([string]$config.Root) -or -not (Test-Path -LiteralPath ([string]$config.Root) -PathType Container)
    if ($rootMissing -and -not $Yes -and (Confirm-MpDoctorAction -Prompt 'Configure the modpack project root now?' -Default $true)) {
        $root = Read-Host 'Project root directory'
        if (-not [string]::IsNullOrWhiteSpace($root)) {
            $resolved = Set-ModpackToolsConfigValue -Name root -Value $root
            Write-MpSuccess "Project root configured: $resolved"
        }
    }
}

function Write-MpDoctorReport {
    param([Parameter(Mandatory)]$Report)
    Write-MpBanner 'MODPACKTOOLS · DOCTOR'
    foreach ($section in @('SYSTEM', 'PACKWIZ', 'PROJECT ROOT', 'OPTIONAL')) {
        $items = @($Report.Checks | Where-Object Section -eq $section)
        if (-not $items.Count) { continue }
        Write-MpHelpHeading $section
        foreach ($item in $items) { Write-MpDoctorLine -Status $item.Status -Label $item.Label -Value $item.Value -Detail $item.Detail }
    }
    Write-Host ''
    if ($Report.Failures) {
        Write-MpDoctorSummary -Status fail -Text "$($Report.Failures) required issue(s) need attention. Run: modpack doctor --fix"
    }
    else {
        Write-MpDoctorSummary -Status pass -Text 'Everything required is ready.'
        if ($Report.Warnings) { Write-MpDoctorSummary -Status warn -Text "$($Report.Warnings) optional warning(s)." }
    }
}
