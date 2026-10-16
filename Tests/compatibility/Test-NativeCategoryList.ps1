param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$LegacyRoot,
    [string]$NuExecutable = 'D:\Desarrollo\Toolchains\Nushell\bin\nu.exe')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$run = Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-category-list-' + [guid]::NewGuid().ToString('N'))
$packs = Join-Path $run 'packs with spaces'
$project = Join-Path $packs 'fixture'
$config = Join-Path $run 'config'
$previous = @{}; foreach ($name in @('MODPACKTOOLS_CONFIG_HOME','MODPACKTOOLS_PROJECT','MODPACKTOOLS_CORE_PATH')) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }
function Canonical($value) {
    if ($value -is [Collections.IDictionary]) {
        $result = [ordered]@{}; foreach ($key in @($value.Keys | Sort-Object)) { $result[$key] = Canonical $value[$key] }; return $result
    }
    if ($value -is [array]) { return ,@($value | ForEach-Object { Canonical $_ }) }
    return $value
}
function Assert-Category([string[]]$Options = @()) {
    $tokens = @('category','list') + $Options + @('--json','--no-human')
    $request = Join-Path $run 'request.json'
    [IO.File]::WriteAllText($request, (@{ arguments = $tokens } | ConvertTo-Json -Compress))
    $legacy = Invoke-NativeTestProcess (Get-Command pwsh).Source @('-NoProfile','-File',(Join-Path $LegacyRoot 'Nushell/Invoke-ModpackBridge.ps1'),'-RequestPath',$request) -TimeoutMilliseconds 30000
    $expected = $legacy.Stdout | ConvertFrom-Json -AsHashtable -Depth 100
    if (-not $expected.ok) { $expected.arguments = @($expected.arguments | Where-Object { $null -ne $_ }) }
    $cachePath = Join-Path $config 'last-categories.json'
    $oldCache = if ($expected.ok) { Get-Content -LiteralPath $cachePath -Raw | ConvertFrom-Json -AsHashtable } else { $null }
    $native = Invoke-NativeTestProcess $Executable $tokens
    $actual = $native.Stdout | ConvertFrom-Json -AsHashtable -Depth 100
    if ($native.Stderr -or $native.Exit -ne $(if ($expected.ok) { 0 } else { 1 }) -or
        (Canonical $expected | ConvertTo-Json -Depth 100 -Compress) -cne (Canonical $actual | ConvertTo-Json -Depth 100 -Compress)) { throw "Category differential failed:`n$($legacy.Stdout)`n$($native.Stdout)`n$($native.Stderr)" }
    if ($expected.ok) {
        $newCache = Get-Content -LiteralPath $cachePath -Raw | ConvertFrom-Json -AsHashtable
        [void]$oldCache.Remove('CreatedUtc'); [void]$newCache.Remove('CreatedUtc')
        if ((Canonical $oldCache | ConvertTo-Json -Depth 100 -Compress) -cne (Canonical $newCache | ConvertTo-Json -Depth 100 -Compress)) { throw 'Category cache differs' }
    }
}
try {
    foreach ($directory in @($config,(Join-Path $project '.modpack'))) { [void][IO.Directory]::CreateDirectory($directory) }
    [IO.File]::WriteAllText((Join-Path $config 'config.psd1'), "@{Root='$($packs.Replace("'","''"))'}")
    [IO.File]::WriteAllText((Join-Path $project '.modpack/project.psd1'), "@{SchemaVersion=1;Id='fixture'}")
    [IO.File]::WriteAllText((Join-Path $project 'pack.toml'), "name = `"Fixture`"`n[index]`nfile = `"index.toml`"`n")
    $env:MODPACKTOOLS_CONFIG_HOME = $config; $env:MODPACKTOOLS_PROJECT = 'fixture'
    Assert-Category # No metadata/index is required by this operation.
    $metadata = Join-Path $project '.modpack/metadata.psd1'
    [IO.File]::WriteAllText($metadata, "@{Categories=@{z=@{Name='Zulu';Order=2};a=@{Name='Alpha';Order=-1}};Mods=@{'absent-id'=@{Category='z'}};ResourcePacks=@{}}")
    Assert-Category
    Assert-Category @('--project','fixture')
    Assert-Category @('extra')
    Assert-Category @('one','two')
    Assert-Category @('--wat=yes')
    $env:MODPACKTOOLS_CORE_PATH = $Executable
    $module = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../Nushell/modpack.nu')).Replace('\','/')
    $nuScript = Join-Path $run 'native-only.nu'
    [IO.File]::WriteAllText($nuScript, "use '$module' main; `$env.PATH = []; let rows = (modpack category list --no-human); if (`$rows | length) != 3 { error make {msg: 'category count'} }; if `$rows.0.id != 'a' { error make {msg: 'category order'} }; print 'PASS: native-only Nu categories'")
    $nu = Invoke-NativeTestProcess $NuExecutable @('--no-config-file',$nuScript)
    if ($nu.Exit -or $nu.Stderr) { throw "Nu category failed: $($nu.Stderr)" }
    [IO.File]::WriteAllText((Join-Path $project 'index.toml'), '')
    $read = Invoke-NativeTestProcess $Executable @('content','list','--category','2','--json','--no-human')
    $result = $read.Stdout | ConvertFrom-Json
    if ($read.Exit -or $read.Stderr -or -not $result.ok -or @($result.data.inventory.filters).Count -eq 0) { throw "Native category reference roundtrip failed: $($read.Stdout)" }
    'PASS: six public category differentials, cache equivalence, numeric category roundtrip and Nu without PowerShell'
} finally {
    foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name,$previous[$name]) }
    if ([IO.Directory]::Exists($run)) { [IO.Directory]::Delete($run,$true) }
}
