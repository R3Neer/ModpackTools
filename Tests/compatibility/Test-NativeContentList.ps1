param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$LegacyRoot,
    [string]$NuExecutable = 'D:\Desarrollo\Toolchains\Nushell\bin\nu.exe')
# PowerShell is used only as the differential oracle for the old implementation.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$run = Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-public-list-' + [guid]::NewGuid().ToString('N'))
$packs = Join-Path $run "packs á O'Brien"
$project = Join-Path $packs 'fixture'
$config = Join-Path $run 'config'
$savedConfig = $env:MODPACKTOOLS_CONFIG_HOME
$savedProject = $env:MODPACKTOOLS_PROJECT
$savedCore = $env:MODPACKTOOLS_CORE_PATH
function Canonical($value) {
    if ($value -is [Collections.IDictionary]) {
        $result = [ordered]@{}
        foreach ($key in @($value.Keys | Sort-Object)) { $result[$key] = Canonical $value[$key] }
        return $result
    }
    if ($value -is [array]) { return ,@($value | ForEach-Object { Canonical $_ }) }
    return $value
}
function Assert-ListCase([string[]]$Options = @()) {
    $tokens = @('content', 'list') + $Options + @('--json', '--no-human')
    $request = Join-Path $run 'request.json'
    [IO.File]::WriteAllText($request, (@{ arguments = $tokens } | ConvertTo-Json -Compress))
    $old = Invoke-NativeTestProcess (Get-Command pwsh).Source @('-NoLogo', '-NoProfile', '-File', (Join-Path $LegacyRoot 'Nushell/Invoke-ModpackBridge.ps1'), '-RequestPath', $request) -TimeoutMilliseconds 30000
    $expected = $old.Stdout | ConvertFrom-Json -AsHashtable -Depth 100
    # The typed schema retains arguments as an array; the legacy error path can
    # accidentally unwrap a singleton (same normalization as project contracts).
    if (-not $expected.ok) { $expected.arguments = @($expected.arguments | Where-Object { $null -ne $_ }) }
    $cachePath = Join-Path $config 'last-inventory.json'
    $oldCache = if ($expected.ok) { Get-Content -LiteralPath $cachePath -Raw | ConvertFrom-Json -AsHashtable } else { $null }
    $actualRun = Invoke-NativeTestProcess $Executable $tokens
    if ($actualRun.Stderr) { throw "Native stderr: $($actualRun.Stderr)" }
    $actual = $actualRun.Stdout | ConvertFrom-Json -AsHashtable -Depth 100
    if ($actualRun.Exit -ne $(if ($expected.ok) { 0 } else { 1 })) { throw "Unexpected exit: $($actualRun.Stdout)" }
    if ($expected.ok) {
        # Native graph4 uses accepted framed issue-key2 rather than old keys.
        foreach ($partition in @('errors', 'warnings', 'unknown')) {
            foreach ($issue in @($expected.data.health[$partition])) { [void]$issue.Remove('Key') }
            foreach ($issue in @($actual.data.health[$partition])) { [void]$issue.Remove('Key') }
        }
        $newCache = Get-Content -LiteralPath $cachePath -Raw | ConvertFrom-Json -AsHashtable
        [void]$oldCache.Remove('CreatedUtc'); [void]$newCache.Remove('CreatedUtc')
        if ((Canonical $oldCache | ConvertTo-Json -Depth 100 -Compress) -cne (Canonical $newCache | ConvertTo-Json -Depth 100 -Compress)) { throw 'Published numbered reference cache differs' }
    }
    if ((Canonical $expected | ConvertTo-Json -Depth 100 -Compress) -cne (Canonical $actual | ConvertTo-Json -Depth 100 -Compress)) {
        throw "Public list differs for $($Options -join ' '):`n$($old.Stdout)`n$($actualRun.Stdout)"
    }
}
try {
    foreach ($directory in @($config, (Join-Path $project '.modpack'), (Join-Path $project 'mods'), (Join-Path $project 'config'))) { [void][IO.Directory]::CreateDirectory($directory) }
    [IO.File]::WriteAllText((Join-Path $config 'config.psd1'), "@{Root='$($packs.Replace("'", "''"))'}")
    [IO.File]::WriteAllText((Join-Path $project '.modpack/project.psd1'), "@{SchemaVersion=1;Id='fixture';JavaVersion='21'}")
    [IO.File]::WriteAllText((Join-Path $project '.modpack/metadata.psd1'), '@{SchemaVersion=1;Categories=@{};Mods=@{};ResourcePacks=@{}}')
    [IO.File]::WriteAllText((Join-Path $project 'pack.toml'), "name = `"Fixture`"`nversion = `"1.0`"`n[index]`nfile = `"index.toml`"`n[versions]`nminecraft = `"1.21.1`"`nfabric = `"0.16`"`n")
    [IO.File]::WriteAllText((Join-Path $project 'index.toml'), "hash-format = `"sha256`"`n")
    $env:MODPACKTOOLS_CONFIG_HOME = $config; $env:MODPACKTOOLS_PROJECT = 'fixture'
    Assert-ListCase
    Assert-ListCase @('--verify')
    foreach ($options in @(,@('--type', 'mod'); ,@('--category', 'unclassified'); ,@('--side', 'host'); ,@('--state', 'inactive'); ,@('--source', 'local'); ,@('--match', 'absent'); ,@('--wat'); ,@('--type'); ,@('verify'); ,@('--verify=yes'); ,@('extra'); ,@(''))) { Assert-ListCase $options }
    $jar = Join-Path $project 'mods/local.jar'
    $archive = [IO.Compression.ZipFile]::Open($jar, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $entry = $archive.CreateEntry('fabric.mod.json'); $writer = [IO.StreamWriter]::new($entry.Open())
        try { $writer.Write('{"schemaVersion":1,"id":"fixturelocal","name":"Local fixture","version":"1.0","environment":"*"}') } finally { $writer.Dispose() }
    } finally { $archive.Dispose() }
    [IO.File]::WriteAllText((Join-Path $project 'config/defaultoptions-common.toml'), "defaultResourcePacks = [`"vanilla`", `"file/missing.zip`"]`n")
    Assert-ListCase
    Assert-ListCase @('--type', 'mod', '--verify')
    Assert-ListCase @('--type', 'resourcepack', '--state', 'active')
    $env:MODPACKTOOLS_CORE_PATH = $Executable
    $module = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../Nushell/modpack.nu')).Replace('\', '/')
    $nuScript = Join-Path $run 'native-only.nu'
    # An empty PATH makes an accidental PowerShell fallback fail. Absolute Nu/core
    # paths and clean config keep this independent of the user's installed setup.
    [IO.File]::WriteAllText($nuScript, "use '$module' main; `$env.PATH = []; modpack content list --no-human | to json --raw")
    $nu = Invoke-NativeTestProcess $NuExecutable @('--no-config-file', $nuScript)
    if ($nu.Exit -ne 0 -or $nu.Stderr) { throw "Native-only Nu failed: $($nu.Stderr)" }
    $items = $nu.Stdout | ConvertFrom-Json
    if (@($items).Count -ne 3) { throw 'Nu did not expose the complete structured content list' }
    [IO.File]::Delete((Join-Path $project 'index.toml'))
    Assert-ListCase
    [IO.File]::WriteAllText((Join-Path $project 'index.toml'), "hash-format = `"sha256`"`n")
    [IO.File]::Delete((Join-Path $project '.modpack/metadata.psd1'))
    Assert-ListCase
    'PASS: public content list differential, reference publication and fresh Nushell without PowerShell on PATH'
} finally {
    $env:MODPACKTOOLS_CONFIG_HOME = $savedConfig; $env:MODPACKTOOLS_PROJECT = $savedProject; $env:MODPACKTOOLS_CORE_PATH = $savedCore
    if ([IO.Directory]::Exists($run)) { [IO.Directory]::Delete($run, $true) }
}
