param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$NuExecutable, [Parameter(Mandatory)][string]$StubExecutable, [Parameter(Mandatory)][string]$PackwizExecutable)
$ErrorActionPreference='Stop'
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-nu-checks-'+[guid]::NewGuid().ToString('N'))
$previousCore=$env:MODPACKTOOLS_CORE_PATH
$previousConfig=$env:MODPACKTOOLS_CONFIG_HOME
$previousState=$env:MODPACKTOOLS_STATE_HOME
$previousAppData=$env:APPDATA
$previousLocalAppData=$env:LOCALAPPDATA
try {
    $configuration=Join-Path $fixture 'configuration'
    $projects=Join-Path $fixture 'projects'
    $pack=Join-Path $projects 'fixture'
    [void][IO.Directory]::CreateDirectory($configuration)
    $env:APPDATA=Join-Path $fixture 'Roaming'
    $env:LOCALAPPDATA=Join-Path $fixture 'Local'
    [void][IO.Directory]::CreateDirectory($env:APPDATA)
    [void][IO.Directory]::CreateDirectory($env:LOCALAPPDATA)
    [void][IO.Directory]::CreateDirectory((Join-Path $pack '.modpack'))
    [IO.File]::WriteAllText((Join-Path $configuration 'config.psd1'), "@{Root='$($projects.Replace("'","''"))'}")
    [IO.File]::WriteAllText((Join-Path $pack '.modpack/project.psd1'), "@{SchemaVersion=1;Id='fixture'}")
    [IO.File]::WriteAllText((Join-Path $pack 'pack.toml'), '[index]')
    $env:MODPACKTOOLS_CORE_PATH=[IO.Path]::GetFullPath($Executable)
    $env:MODPACKTOOLS_CONFIG_HOME=$configuration
    $env:MODPACKTOOLS_STATE_HOME=Join-Path $fixture 'stable-native-state'
    foreach($name in @('Test-NativeNu.nu','Test-NativeNuProjects.nu','Test-NativeNuCurrent.nu','Test-NativeNuEnvironment.nu')) {
        & $NuExecutable --no-config-file (Join-Path $PSScriptRoot $name)
        if($LASTEXITCODE -ne 0){throw "Native Nu check failed: $name"}
    }
    & $NuExecutable --no-config-file (Join-Path $PSScriptRoot 'Test-NativeNuUse.nu') ([IO.Path]::GetFullPath($StubExecutable))
    if($LASTEXITCODE -ne 0){throw 'Native Nu use check failed'}
    & $NuExecutable --no-config-file (Join-Path $PSScriptRoot 'Test-NativeNuMutations.nu') ([IO.Path]::GetFullPath($StubExecutable))
    if($LASTEXITCODE -ne 0){throw 'Native Nu mutation check failed'}
    & $NuExecutable --no-config-file (Join-Path $PSScriptRoot 'Test-NativeNuCategories.nu') ([IO.Path]::GetFullPath($StubExecutable))
    if($LASTEXITCODE -ne 0){throw 'Native Nu categories check failed'}
    & $NuExecutable --no-config-file (Join-Path $PSScriptRoot 'Test-NativeNuConfiguration.nu') ([IO.Path]::GetFullPath($StubExecutable))
    if($LASTEXITCODE -ne 0){throw 'Native Nu configuration check failed'}
    & $NuExecutable --no-config-file (Join-Path $PSScriptRoot 'Test-NativeNuRegistration.nu') ([IO.Path]::GetFullPath($StubExecutable))
    if($LASTEXITCODE -ne 0){throw 'Native Nu registration check failed'}
    & $NuExecutable --no-config-file (Join-Path $PSScriptRoot 'Test-NativeNuCreation.nu') ([IO.Path]::GetFullPath($StubExecutable))
    if($LASTEXITCODE -ne 0){throw 'Native Nu creation check failed'}
    $sideMods=Join-Path $projects 'Nu Native Pack-1.21.1/mods'
    [void][IO.Directory]::CreateDirectory($sideMods)
    $jar=[IO.File]::Create((Join-Path $sideMods 'side_fixture.jar'))
    try {
        $archive=[IO.Compression.ZipArchive]::new($jar,[IO.Compression.ZipArchiveMode]::Create,$true)
        try {
            $writer=[IO.StreamWriter]::new($archive.CreateEntry('fabric.mod.json').Open(),[Text.UTF8Encoding]::new($false))
            try {$writer.Write('{"schemaVersion":1,"id":"side_fixture","name":"Side Fixture","version":"1.0.0","environment":"*"}')} finally {$writer.Dispose()}
        } finally {$archive.Dispose()}
    } finally {$jar.Dispose()}
    & $NuExecutable --no-config-file (Join-Path $PSScriptRoot 'Test-NativeNuModSide.nu') ([IO.Path]::GetFullPath($StubExecutable))
    if($LASTEXITCODE -ne 0){throw 'Native Nu mod side check failed'}
    & $NuExecutable --no-config-file (Join-Path $PSScriptRoot 'Test-NativeNuRemoval.nu') ([IO.Path]::GetFullPath($StubExecutable)) ([IO.Path]::GetFullPath($PackwizExecutable))
    if($LASTEXITCODE -ne 0){throw 'Native Nu removal check failed'}
    & $NuExecutable --no-config-file (Join-Path $PSScriptRoot 'Test-NativeNuBuild.nu') ([IO.Path]::GetFullPath($StubExecutable)) ([IO.Path]::GetFullPath($PackwizExecutable))
    if($LASTEXITCODE -ne 0){throw 'Native Nu build check failed'}
} finally {
    $env:MODPACKTOOLS_CORE_PATH=$previousCore; $env:MODPACKTOOLS_CONFIG_HOME=$previousConfig; $env:MODPACKTOOLS_STATE_HOME=$previousState
    $env:APPDATA=$previousAppData; $env:LOCALAPPDATA=$previousLocalAppData
    $resolved=[IO.Path]::GetFullPath($fixture)
    if(-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-nu-checks-*'){throw 'Unsafe fixture cleanup target'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
