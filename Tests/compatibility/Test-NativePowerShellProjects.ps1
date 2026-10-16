param([Parameter(Mandatory)][string]$Executable)
$ErrorActionPreference='Stop'
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-ps-projects-'+[guid]::NewGuid().ToString('N'))
$previousCore=$env:MODPACKTOOLS_CORE_PATH
$previousConfig=$env:MODPACKTOOLS_CONFIG_HOME
try {
    [void][IO.Directory]::CreateDirectory($fixture)
    $env:MODPACKTOOLS_CORE_PATH=$Executable
    $env:MODPACKTOOLS_CONFIG_HOME=$fixture
    [IO.File]::WriteAllText((Join-Path $fixture 'config.psd1'), "@{Root='$($fixture.Replace("'","''"))'}")
    Import-Module (Join-Path $PSScriptRoot '../../ModpackTools.psd1') -Force
    $module=Get-Module ModpackTools
    & $module {
        function script:Initialize-MpConsole { throw 'Renderer must not be initialized' }
        function script:Get-ModpackProjects { throw 'Legacy project reader must not run' }
    }
    $cases=@(,@('project','list'); ,@('PROJECT','LIST'); ,@('--ascii','project','list'))
    foreach($tokens in $cases) {
        $raw=@(modpack @tokens --json --no-human)
        if($raw.Count -ne 1){throw 'Catalog emitted multiple envelopes'}
        $result=$raw[0]|ConvertFrom-Json
        if(-not $result.ok -or $result.data.projects.items -isnot [array] -or $result.data.projects.items.Count -ne 0){throw 'Empty catalog adapter result mismatch'}
    }
    $pack=Join-Path $fixture 'registered'
    [void][IO.Directory]::CreateDirectory((Join-Path $pack '.modpack'))
    [IO.File]::WriteAllText((Join-Path $pack '.modpack/project.psd1'), "@{SchemaVersion=1;Id='registered'}")
    [IO.File]::WriteAllText((Join-Path $pack 'pack.toml'), '[index]')
    $catalog=modpack project list --json --no-human | ConvertFrom-Json
    if($catalog.data.projects.items.Count -ne 1 -or $catalog.data.projects.items[0].id -cne 'registered' -or $null -ne $catalog.data.projects.items[0].name){throw 'Nullable project adapter result mismatch'}
    $output=[Collections.Generic.List[string]]::new()
    try {modpack project list --project absent --json --no-human | ForEach-Object {$output.Add($_)}; throw 'Forbidden project was accepted'}
    catch {if($_.FullyQualifiedErrorId -notlike 'ModpackTools.Option.ForbiddenCombination*'){throw}}
    if($output.Count -ne 1 -or ($output[0]|ConvertFrom-Json).error.target -cne 'absent'){throw 'Domain error adapter result mismatch'}
    'Native PS project catalog passed without renderer or legacy reader'
} finally {
    $env:MODPACKTOOLS_CORE_PATH=$previousCore
    $env:MODPACKTOOLS_CONFIG_HOME=$previousConfig
    Remove-Module ModpackTools -ErrorAction SilentlyContinue
    $resolved=[IO.Path]::GetFullPath($fixture)
    if(-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-ps-projects-*'){throw 'Unsafe fixture cleanup target'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
