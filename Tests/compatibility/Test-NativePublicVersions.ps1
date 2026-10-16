param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$LegacyRoot,
    [string]$NuExecutable = 'D:\Desarrollo\Toolchains\Nushell\bin\nu.exe', [switch]$OnlineSmoke)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$run = Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-versions-' + [guid]::NewGuid().ToString('N'))
$packs = Join-Path $run 'packs with spaces'; $project = Join-Path $packs 'fixture'; $config = Join-Path $run 'config'
$previous = @{}; foreach ($name in @('MODPACKTOOLS_CONFIG_HOME','MODPACKTOOLS_PROJECT','MODPACKTOOLS_CORE_PATH')) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }
try {
    foreach ($directory in @($config,(Join-Path $project '.modpack'),(Join-Path $project 'mods'))) { [void][IO.Directory]::CreateDirectory($directory) }
    [IO.File]::WriteAllText((Join-Path $config 'config.psd1'), "@{Root='$($packs.Replace("'","''"))'}")
    [IO.File]::WriteAllText((Join-Path $project '.modpack/project.psd1'), "@{SchemaVersion=1;Id='fixture'}")
    [IO.File]::WriteAllText((Join-Path $project 'pack.toml'), "name = `"Versions`"`n[index]`nfile = `"index.toml`"`n[versions]`nminecraft = `"1.21.1`"`nfabric = `"0.16`"`n")
    [IO.File]::WriteAllText((Join-Path $project 'mods/custom.pw.toml'), "name = `"Custom`"`nfilename = `"custom.jar`"`n")
    $env:MODPACKTOOLS_CONFIG_HOME = $config; $env:MODPACKTOOLS_PROJECT = 'fixture'; $env:MODPACKTOOLS_CORE_PATH = $Executable
    foreach ($tail in @(,@(); ,@('--wat'); ,@('missing'); ,@('custom'); ,@('1'); ,@('custom','--project','absent'))) {
        $tokens = @('content','versions') + $tail + @('--json','--no-human')
        $request = Join-Path $run 'request.json'; [IO.File]::WriteAllText($request, (@{ arguments = $tokens } | ConvertTo-Json -Compress))
        $old = Invoke-NativeTestProcess (Get-Command pwsh).Source @('-NoProfile','-File',(Join-Path $LegacyRoot 'Nushell/Invoke-ModpackBridge.ps1'),'-RequestPath',$request) -TimeoutMilliseconds 30000
        $new = Invoke-NativeTestProcess $Executable $tokens
        $expected = $old.Stdout | ConvertFrom-Json -AsHashtable; $actual = $new.Stdout | ConvertFrom-Json -AsHashtable
        $expected.arguments = @($expected.arguments | Where-Object { $null -ne $_ })
        if ($new.Exit -ne 1 -or $new.Stderr -or ($expected | ConvertTo-Json -Depth 100 -Compress) -cne ($actual | ConvertTo-Json -Depth 100 -Compress)) { throw "Version selection differential failed:`n$($old.Stdout)`n$($new.Stdout)" }
    }
    $empty = Invoke-NativeTestProcess $Executable @('content','versions','','--json','--no-human')
    if ($empty.Exit -ne 1 -or $empty.Stderr -or ($empty.Stdout | ConvertFrom-Json).error.id -ne 'ModpackTools.Content.NotFound') { throw 'Empty version selector must fail clearly without provider IO' }
    if ([IO.File]::Exists((Join-Path $config 'last-versions.json'))) { throw 'A failed selection wrote a version cache' }
    $module = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../Nushell/modpack.nu')).Replace('\','/')
    $script = Join-Path $run 'native-only.nu'
    [IO.File]::WriteAllText($script, "use '$module' main; `$env.PATH = []; let failure = (try { modpack content versions custom --no-human; null } catch {|err| `$err.msg }); if `$failure == null or `$failure !~ 'ModpackTools.Versions.UnsupportedProvider' { error make {msg: 'versions native failure'} }; print 'PASS: native-only Nu version selection'")
    $nu = Invoke-NativeTestProcess $NuExecutable @('--no-config-file',$script)
    if ($nu.Exit -or $nu.Stderr) { throw "Nu version failure transport: $($nu.Stderr)" }
    if ($OnlineSmoke) {
        # Read-only public provider metadata, owned synthetic project, no JARs or
        # real modpacks. No provider write/publish operation exists on this route.
        [IO.File]::WriteAllText((Join-Path $project 'mods/fabric-api.pw.toml'), "name = `"Fabric API`"`nfilename = `"fixture.jar`"`n[update.modrinth]`nmod-id = `"P7dR8mSH`"`nversion = `"none`"`n")
        [IO.File]::WriteAllText($script, "use '$module' main; `$env.PATH = []; let rows = (modpack content versions fabric-api --no-human); if (`$rows | is-empty) { error make {msg: 'empty live fixture response'} }; print (`$rows | length)")
        $nu = Invoke-NativeTestProcess $NuExecutable @('--no-config-file',$script) -TimeoutMilliseconds 90000
        if ($nu.Exit -or $nu.Stderr -or [int]$nu.Stdout -lt 1) { throw "Read-only provider smoke failed: $($nu.Stdout) $($nu.Stderr)" }
        $cache = Get-Content -LiteralPath (Join-Path $config 'last-versions.json') -Raw | ConvertFrom-Json
        if ($cache.ItemId -ne 'modrinth:P7dR8mSH' -or @($cache.Versions).Count -ne [int]$nu.Stdout) { throw 'Native Nu provider result/cache mismatch' }
        if (@(Get-ChildItem -LiteralPath (Join-Path $config 'metadata') -File).Count -ne 0) { throw 'Version listing leaked provider metadata or temporaries' }
        "PASS: read-only public provider smoke, $($nu.Stdout.Trim()) rows through Nu and AOT without PowerShell on PATH"
    }
    'PASS: six public version-selection differentials, empty-selector improvement and fresh Nu without PowerShell'
} finally {
    foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name,$previous[$name]) }
    if ([IO.Directory]::Exists($run)) { [IO.Directory]::Delete($run,$true) }
}
