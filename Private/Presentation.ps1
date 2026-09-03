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

function Get-MpConsole {
    Assert-MpPresentation
    if (-not $script:MpConsole) { $script:MpConsole = New-R3Console -ThemeExtension (Read-MpThemeExtension) }
    return $script:MpConsole
}

function Initialize-MpConsole {
    param([string]$Colour = 'auto', [switch]$Ascii, $Invocation)
    Assert-MpPresentation
    $script:MpConsole = New-R3Console -Colour $Colour -Ascii:$Ascii -ThemeExtension (Read-MpThemeExtension) -Invocation $Invocation
}

function ConvertFrom-MpPresentationOptions {
    param([object[]]$Tokens)
    $remaining = [Collections.Generic.List[object]]::new(); $seen = @{}; $colour = 'auto'; $ascii = $false
    for ($i=0; $i -lt $Tokens.Count; $i++) {
        $token = [string]$Tokens[$i]
        if ($token -notmatch '^--(colour|ascii)(?:=(.*))?$') { $remaining.Add($Tokens[$i]); continue }
        $name = $Matches[1]; $inline = if ($Matches.ContainsKey(2)) { $Matches[2] } else { $null }
        if ($seen.ContainsKey($name)) { Throw-MpError -Message "Option '--$name' is repeated" -Hint "specify --$name once" -ErrorId 'Option.Duplicate' -Category InvalidArgument }
        $seen[$name] = $true
        if ($name -eq 'ascii') {
            if ($null -ne $inline) { Throw-MpError -Message "Option '--ascii' does not accept a value" -Hint '--ascii' -ErrorId 'Option.UnexpectedValue' -Category InvalidArgument }
            $ascii = $true; continue
        }
        if ($null -eq $inline) {
            $i++
            if ($i -ge $Tokens.Count) { Throw-MpError -Message "Option '--colour' requires a value" -Hint '--colour auto|always|never' -ErrorId 'Option.MissingValue' -Category InvalidArgument }
            $inline = [string]$Tokens[$i]
        }
        if ($inline -notin @('auto','always','never')) { Throw-MpError -Message "Colour mode '$inline' is invalid" -Hint '--colour auto|always|never' -ErrorId 'Option.InvalidColour' -Category InvalidArgument }
        $colour = $inline
    }
    [pscustomobject]@{ Arguments=@($remaining); Colour=$colour; Ascii=$ascii }
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
