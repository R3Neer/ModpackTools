param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$NuExecutable, [Parameter(Mandatory)][string]$StubExecutable, [Parameter(Mandatory)][string]$PackwizExecutable)
$ErrorActionPreference='Stop'
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-nu-checks-'+[guid]::NewGuid().ToString('N'))
$previousExpectedVersion=$env:MODPACKTOOLS_TEST_EXPECTED_VERSION
$env:MODPACKTOOLS_TEST_EXPECTED_VERSION=[string](Import-PowerShellDataFile (Join-Path $PSScriptRoot '../../ModpackTools.psd1')).ModuleVersion
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
    & $NuExecutable --no-config-file (Join-Path $PSScriptRoot 'Test-NativeNuCreation.nu') ([IO.Path]::GetFullPath($StubExecutable)) ([IO.Path]::GetFullPath($PackwizExecutable))
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
    & $NuExecutable --no-config-file (Join-Path $PSScriptRoot 'Test-NativeNuAliases.nu') ([IO.Path]::GetFullPath($PackwizExecutable))
    if($LASTEXITCODE -ne 0){throw 'Native Nu short alias check failed'}
    & $NuExecutable --no-config-file (Join-Path $PSScriptRoot 'Test-NativeNuHumanReceipts.nu') ([IO.Path]::GetFullPath($PackwizExecutable))
    if($LASTEXITCODE -ne 0){throw 'Native Nu human transaction receipt check failed'}
    # Add only owned offline fixtures; no real Default Options/modpack is used.
    foreach($fixtureEntry in @(
        @{Path=(Join-Path $sideMods 'defaultoptions-fixture.jar');Entry='fabric.mod.json';Text='{"schemaVersion":1,"id":"defaultoptions","name":"Default Options","version":"1.0.0","environment":"*"}'},
        @{Path=(Join-Path $projects 'Nu Native Pack-1.21.1/resourcepacks/alpha.zip');Entry='pack.mcmeta';Text='{"pack":{"pack_format":34,"description":"Alpha UI fixture"}}'},
        @{Path=(Join-Path $projects 'Nu Native Pack-1.21.1/resourcepacks/beta.zip');Entry='pack.mcmeta';Text='{"pack":{"pack_format":34,"description":"Beta UI fixture"}}'}
    )){
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fixtureEntry.Path))
        $archive=[IO.Compression.ZipArchive]::new([IO.File]::Create($fixtureEntry.Path),[IO.Compression.ZipArchiveMode]::Create)
        try{$writer=[IO.StreamWriter]::new($archive.CreateEntry($fixtureEntry.Entry).Open(),[Text.UTF8Encoding]::new($false));try{$writer.Write($fixtureEntry.Text)}finally{$writer.Dispose()}}finally{$archive.Dispose()}
    }
    $resourceConfiguration=Join-Path $projects 'Nu Native Pack-1.21.1/config/defaultoptions-common.toml'
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resourceConfiguration))
    if(Test-Path -LiteralPath $resourceConfiguration){throw 'Resource fixture must own a new Default Options configuration'}
    [IO.File]::WriteAllText($resourceConfiguration,"# Owned offline fixture`ndefaultResourcePacks = []`nunknownSentinel = 'preserve me'`n",[Text.UTF8Encoding]::new($false))
    & $NuExecutable --no-config-file (Join-Path $PSScriptRoot 'Test-NativeNuHumanResources.nu')
    if($LASTEXITCODE -ne 0){throw 'Native Nu human resource check failed'}
    & $NuExecutable --no-config-file (Join-Path $PSScriptRoot 'Test-NativeNuRemoval.nu') ([IO.Path]::GetFullPath($StubExecutable)) ([IO.Path]::GetFullPath($PackwizExecutable))
    if($LASTEXITCODE -ne 0){throw 'Native Nu removal check failed'}
    & $NuExecutable --no-config-file (Join-Path $PSScriptRoot 'Test-NativeNuBuild.nu') ([IO.Path]::GetFullPath($StubExecutable)) ([IO.Path]::GetFullPath($PackwizExecutable))
    if($LASTEXITCODE -ne 0){throw 'Native Nu build check failed'}
    & $NuExecutable --no-config-file (Join-Path $PSScriptRoot 'Test-NativeNuDiff.nu') ([IO.Path]::GetFullPath($StubExecutable)) ([IO.Path]::GetFullPath($PackwizExecutable))
    if($LASTEXITCODE -ne 0){throw 'Native Nu diff check failed'}
    & $NuExecutable --no-config-file (Join-Path $PSScriptRoot 'Test-NativeNuDoctor.nu') ([IO.Path]::GetFullPath($StubExecutable)) ([IO.Path]::GetFullPath($PackwizExecutable))
    if($LASTEXITCODE -ne 0){throw 'Native Nu doctor check failed'}
    & $NuExecutable --no-config-file (Join-Path $PSScriptRoot 'Test-NativeNuDoctorRepair.nu') ([IO.Path]::GetFullPath($PackwizExecutable)) ([IO.Path]::GetFullPath($StubExecutable))
    if($LASTEXITCODE -ne 0){throw 'Native Nu doctor repair check failed'}
    & $NuExecutable --no-config-file (Join-Path $PSScriptRoot 'Test-NativeNuHelp.nu') ([IO.Path]::GetFullPath($StubExecutable))
    if($LASTEXITCODE -ne 0){throw 'Native Nu help check failed'}
    & $NuExecutable --no-config-file (Join-Path $PSScriptRoot 'Test-NativeNuDoctorFailure.nu') ([IO.Path]::GetFullPath($StubExecutable))
    if($LASTEXITCODE -ne 0){throw 'Native Nu doctor retained-evidence failure check failed'}
} finally {
    $env:MODPACKTOOLS_TEST_EXPECTED_VERSION=$previousExpectedVersion
    $env:MODPACKTOOLS_CORE_PATH=$previousCore; $env:MODPACKTOOLS_CONFIG_HOME=$previousConfig; $env:MODPACKTOOLS_STATE_HOME=$previousState
    $env:APPDATA=$previousAppData; $env:LOCALAPPDATA=$previousLocalAppData
    $resolved=[IO.Path]::GetFullPath($fixture)
    if(-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-nu-checks-*'){throw 'Unsafe fixture cleanup target'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
