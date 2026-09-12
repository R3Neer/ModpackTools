# R3CLI is loaded from the verified private package, never from PSModulePath.
$script:R3Module = $null
$script:R3LoadError = $null
$script:MpConsole = $null
try {
    . (Join-Path $script:ModuleRoot 'Private/VerifyR3CLI.ps1')
    $verifiedR3 = Test-MpR3Package -ModuleRoot $script:ModuleRoot
    $script:R3Module = Import-Module -Name $verifiedR3.Path -Scope Local -PassThru -Force
} catch { $script:R3LoadError = $_.Exception.Message }

function Assert-MpPresentation {
    if (-not $script:R3Module) {
        Throw-MpError -Message 'The bundled R3CLI presentation dependency is unavailable' -Details $script:R3LoadError -Hint 'run Install-ModpackTools.ps1 -Force from a complete ModpackTools package' -ErrorId 'Dependency.RendererUnavailable' -Category ResourceUnavailable
    }
}

function Read-MpThemeExtension {
    param([string]$Path = (Join-Path $script:ModuleRoot 'theme.toml'))
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Throw-MpError -Message "Theme file '$Path' does not exist" -Hint 'restore theme.toml and reinstall ModpackTools' -ErrorId 'Theme.NotFound' -Category ObjectNotFound -TargetObject $Path
    }
    $data = ConvertFrom-MpToml (Get-Content -LiteralPath $Path -Raw -Encoding UTF8)
    $section = if ($data.ContainsKey('colours')) { 'colours' } else { 'colors' }
    if (-not $data.ContainsKey($section)) {
        Throw-MpError -Message 'The product theme has no colours table' -Hint 'restore theme.toml' -ErrorId 'Theme.MissingColor' -Category InvalidData
    }
    $colors = $data[$section]; $extension = @{}
    foreach ($name in @('client','host','local')) {
        if (-not $colors.ContainsKey($name)) { Throw-MpError -Message "Required theme color '$name' is missing from '$Path'" -Hint 'restore the missing role' -ErrorId 'Theme.MissingColor' -Category InvalidData }
    }
    $canonical = (New-R3Console -Colour never).Theme
    foreach ($name in $colors.Keys) {
        $value = [string]$colors[$name]
        if ($value -notmatch '^#[0-9A-Fa-f]{6}$') { Throw-MpError -Message "Theme color '$name' must use #RRGGBB" -Hint 'correct theme.toml' -ErrorId 'Theme.InvalidColor' -Category InvalidData }
        # Legacy complete themes inherit unchanged canonical roles; custom values survive.
        if ($section -eq 'colors' -and $canonical.Contains($name) -and $canonical[$name] -eq $value) { continue }
        $extension[$name] = $value.ToUpperInvariant()
    }
    return $extension
}

function Get-MpHumanOutputIsTerminal {
    $hint = [Environment]::GetEnvironmentVariable('MODPACKTOOLS_NU_STDERR_TTY', 'Process')
    if ($hint -eq '1') { return $true }
    if ($hint -eq '0') { return $false }
    return -not [Console]::IsErrorRedirected
}

function Get-MpConsole {
    Assert-MpPresentation
    if (-not $script:MpConsole) { $script:MpConsole = New-R3Console -ThemeExtension (Read-MpThemeExtension) }
    return $script:MpConsole
}

function Initialize-MpConsole {
    param([string]$Colour = 'auto', [switch]$Ascii, $Invocation, [switch]$Json, [switch]$NoHuman)
    Assert-MpPresentation
    $parameters = @{
        Colour = $Colour
        Ascii = [bool]$Ascii
        ThemeExtension = Read-MpThemeExtension
        Invocation = $Invocation
    }
    if ($Json) {
        # The PowerShell pipeline carries only the machine envelope on stdout.
        # R3CLI's normal invocation analysis would mistake that transport pipe for
        # human-output redirection, even though human rendering is on stderr.
        [void]$parameters.Remove('Invocation')
        if ($NoHuman) {
            $parameters.Colour = 'never'
            $parameters.Sink = { param($Text, $Stream) }
        }
        else {
            # JSON owns stdout. Human presentation moves to stderr, and the Nu
            # adapter supplies the parent stderr TTY state because the child
            # PowerShell process otherwise sees Nushell's forwarding pipe.
            $parameters.IsTerminal = Get-MpHumanOutputIsTerminal
            $parameters.Sink = { param($Text, $Stream) [Console]::Error.WriteLine([string]$Text) }
        }
    }
    $script:MpConsole = New-R3Console @parameters
}

function ConvertFrom-MpPresentationOptions {
    param([object[]]$Tokens)
    $remaining = [Collections.Generic.List[object]]::new(); $seen = @{}; $colour = 'auto'; $ascii = $false; $project = $null; $json = $false; $noHuman = $false
    for ($i=0; $i -lt $Tokens.Count; $i++) {
        $token = [string]$Tokens[$i]
        $expanded = switch -CaseSensitive ($token) { '-h' {'--help'} '-V' {'--version'} '-n' {'--dry-run'} '-y' {'--yes'} default {$null} }
        if ($expanded) { $remaining.Add($expanded); continue }
        if ($token -cnotmatch '^(?:--(color|ascii|project|json|no-human)|(-p))(?:=(.*))?$') { $remaining.Add($Tokens[$i]); continue }
        $name = if ($Matches[2]) { 'project' } else { $Matches[1] }
        $inline = if ($Matches.ContainsKey(3)) { $Matches[3] } else { $null }
        if ($seen.ContainsKey($name)) { Throw-MpError -Message "Option '--$name' is repeated" -Hint "specify --$name once" -ErrorId 'Option.Duplicate' -Category InvalidArgument }
        $seen[$name] = $true
        if ($name -in @('ascii','json','no-human')) {
            if ($null -ne $inline) { Throw-MpError -Message "Option '--$name' does not accept a value" -Hint "--$name" -ErrorId 'Option.UnexpectedValue' -Category InvalidArgument }
            if ($name -eq 'ascii') { $ascii = $true }
            elseif ($name -eq 'json') { $json = $true }
            else { $noHuman = $true }
            continue
        }
        if ($null -eq $inline) {
            $i++
            if ($i -ge $Tokens.Count) { Throw-MpError -Message "Option '--$name' requires a value" -Hint "--$name <value>" -ErrorId 'Option.MissingValue' -Category InvalidArgument }
            $inline = [string]$Tokens[$i]
        }
        if ($name -eq 'project') {
            if ([string]::IsNullOrWhiteSpace($inline) -or $inline.StartsWith('-')) { Throw-MpError -Message "Option '--project' requires a project ID" -Hint '--project <id>' -ErrorId 'Option.MissingValue' -Category InvalidArgument }
            $project = $inline
            continue
        }
        if ($inline -notin @('auto','always','never')) { Throw-MpError -Message "Color mode '$inline' is invalid" -Hint '--color auto|always|never' -ErrorId 'Option.InvalidColor' -Category InvalidArgument }
        $colour = $inline
    }
    if ($noHuman -and -not $json) {
        Throw-MpError -Message "Option '--no-human' requires '--json'" -Hint '--json --no-human' -ErrorId 'Option.RequiredCombination' -Category InvalidArgument
    }
    [pscustomobject]@{ Arguments=@($remaining); Colour=$colour; Ascii=$ascii; Project=$project; Json=$json; NoHuman=$noHuman }
}

function Write-MpDoctorLine {
    param([string]$Status, [string]$Label, [AllowEmptyString()][string]$Value, [string]$Detail)
    $kind = @{pass='success';warn='warning';fail='error';info='info'}[$Status]
    Write-R3Status (Get-MpConsole) $kind "$Label`: $Value"
    if ($Detail) { Write-R3Line (Get-MpConsole) @(@{Text="  $Detail";Role='secondary'}) }
}

function Write-MpDoctorItem {
    param(
        [ValidateSet('pass','warn','fail','info')][string]$Status = 'info',
        [Parameter(Mandatory)][string]$Text
    )
    $kind = @{pass='success';warn='warning';fail='error';info='info'}[$Status]
    Write-R3Status (Get-MpConsole) $kind $Text
}

function Write-MpDoctorSummary {
    param([string]$Status, [string]$Text)
    Write-R3Status (Get-MpConsole) (@{pass='success';warn='warning';fail='error'}[$Status]) $Text
}

function Write-MpSideLegend {
    Write-R3Line (Get-MpConsole)
    Write-R3Line (Get-MpConsole) @(@{Text='  [C]';Role='client'}, @{Text=' Client    ';Role='secondary'}, @{Text='[H]';Role='host'}, @{Text=' Host    ';Role='secondary'}, @{Text='[C]';Role='client'}, @{Text='[H]';Role='host'}, @{Text=' Both';Role='secondary'})
}
