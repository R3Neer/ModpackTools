param([Parameter(Mandatory)][string]$Executable)
$ErrorActionPreference='Stop'
$previousCore=$env:MODPACKTOOLS_CORE_PATH
$previousProject=$env:MODPACKTOOLS_PROJECT
$previousConfig=$env:MODPACKTOOLS_CONFIG_HOME
try {
    $env:MODPACKTOOLS_CORE_PATH=$Executable
    $env:MODPACKTOOLS_PROJECT='old-environment'
    $env:MODPACKTOOLS_CONFIG_HOME='Z:\does-not-exist\modpacktools-fixture'
    Import-Module (Join-Path $PSScriptRoot '../../ModpackTools.psd1') -Force
    $module=Get-Module ModpackTools
    & $module {
        function script:Initialize-MpConsole { throw 'Renderer must not be initialized' }
        function script:Invoke-MpCurrent { throw 'Legacy current must not run' }
    }
    foreach($active in @($null,'',' ','selected-stale',"á O'Brien")) {
        & $module {param($value) $script:ActiveProjectId=$value} $active
        foreach($tokens in @(,@('project','current'); ,@('PROJECT','CURRENT'); ,@('--ascii','project','current'))) {
            $raw=@(modpack @tokens --json --no-human)
            if($raw.Count -ne 1){throw 'Current emitted multiple envelopes'}
            $result=$raw[0]|ConvertFrom-Json
            if(-not $result.ok -or $result.data.active_project -cne $active){throw 'Module session snapshot changed or stale environment used'}
            $state=& $module {$script:ActiveProjectId}
            if($state -cne $active){throw 'Current changed parent session'}
        }
    }
    $output=[Collections.Generic.List[string]]::new()
    try {modpack project current extra --project forbidden --json --no-human | ForEach-Object {$output.Add($_)}; throw 'Forbidden project accepted'}
    catch {if($_.FullyQualifiedErrorId -notlike 'ModpackTools.Option.ForbiddenCombination*'){throw}}
    if($output.Count -ne 1 -or ($output[0]|ConvertFrom-Json).error.target -cne 'forbidden'){throw 'Current domain error mismatch'}
    'Native PS current passed with explicit module state and no renderer or legacy handler'
} finally {
    $env:MODPACKTOOLS_CORE_PATH=$previousCore; $env:MODPACKTOOLS_PROJECT=$previousProject; $env:MODPACKTOOLS_CONFIG_HOME=$previousConfig
    Remove-Module ModpackTools -ErrorAction SilentlyContinue
}
