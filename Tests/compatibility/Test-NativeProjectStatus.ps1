param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$LegacyRoot,
    [string]$NuExecutable = 'D:\Desarrollo\Toolchains\Nushell\bin\nu.exe')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$run = Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-status-' + [guid]::NewGuid().ToString('N'))
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
function Assert-Status([string[]]$Options = @()) {
    $tokens = @('project','status') + $Options + @('--json','--no-human')
    $request = Join-Path $run 'request.json'
    [IO.File]::WriteAllText($request, (@{ arguments = $tokens } | ConvertTo-Json -Compress))
    $legacy = Invoke-NativeTestProcess (Get-Command pwsh).Source @('-NoProfile','-File',(Join-Path $LegacyRoot 'Nushell/Invoke-ModpackBridge.ps1'),'-RequestPath',$request) -TimeoutMilliseconds 30000
    $expected = $legacy.Stdout | ConvertFrom-Json -AsHashtable -Depth 100
    if (-not $expected.ok) { $expected.arguments = @($expected.arguments | Where-Object { $null -ne $_ }) }
    $native = Invoke-NativeTestProcess $Executable $tokens
    $actual = $native.Stdout | ConvertFrom-Json -AsHashtable -Depth 100
    if ($native.Stderr -or $native.Exit -ne $(if ($expected.ok) { 0 } else { 1 }) -or
        (Canonical $expected | ConvertTo-Json -Depth 100 -Compress) -cne (Canonical $actual | ConvertTo-Json -Depth 100 -Compress)) { throw "Status differential failed:`n$($legacy.Stdout)`n$($native.Stdout)`n$($native.Stderr)" }
    if ([IO.File]::Exists((Join-Path $config 'last-inventory.json')) -or [IO.File]::Exists((Join-Path $config 'last-categories.json')) -or [IO.Directory]::Exists((Join-Path $config 'health'))) { throw 'Status wrote a result cache' }
}
try {
    foreach ($directory in @($config,(Join-Path $project '.modpack'),(Join-Path $project 'config'))) { [void][IO.Directory]::CreateDirectory($directory) }
    [IO.File]::WriteAllText((Join-Path $config 'config.psd1'), "@{Root='$($packs.Replace("'","''"))'}")
    [IO.File]::WriteAllText((Join-Path $project '.modpack/project.psd1'), "@{SchemaVersion=1;Id='fixture'}")
    [IO.File]::WriteAllText((Join-Path $project 'pack.toml'), "name = `"Fixture`"`n[index]`nfile = `"index.toml`"`n")
    [IO.File]::WriteAllText((Join-Path $project 'index.toml'), '')
    $metadata = Join-Path $project '.modpack/metadata.psd1'
    [IO.File]::WriteAllText($metadata, '@{Categories=@{};Mods=@{};ResourcePacks=@{}}')
    $env:MODPACKTOOLS_CONFIG_HOME = $config; $env:MODPACKTOOLS_PROJECT = 'fixture'
    Assert-Status
    Assert-Status @('--project','fixture')
    Assert-Status @('extra')
    Assert-Status @('--verify')
    [IO.File]::WriteAllText((Join-Path $project 'config/defaultoptions-common.toml'), "defaultResourcePacks = [`"vanilla`",`"file/missing.zip`",`"vanilla`"]`n")
    Assert-Status
    $env:MODPACKTOOLS_CORE_PATH = $Executable
    $module = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../Nushell/modpack.nu')).Replace('\','/')
    $nuScript = Join-Path $run 'native-only.nu'
    [IO.File]::WriteAllText($nuScript, "use '$module' main; `$env.PATH = []; let data = (modpack project status --no-human); if `$data.summary.resources_enabled != 3 or `$data.project.id != 'fixture' { error make {msg: 'status result'} }; print 'PASS: native-only Nu status'")
    $nu = Invoke-NativeTestProcess $NuExecutable @('--no-config-file',$nuScript)
    if ($nu.Exit -or $nu.Stderr) { throw "Nu status failed: $($nu.Stderr)" }
    [IO.File]::Delete($metadata)
    Assert-Status
    'PASS: six public status differentials, no reference/health writes and Nu without PowerShell'
} finally {
    foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name,$previous[$name]) }
    if ([IO.Directory]::Exists($run)) { [IO.Directory]::Delete($run,$true) }
}
