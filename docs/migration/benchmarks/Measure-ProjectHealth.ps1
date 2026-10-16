param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$LegacyRoot,
    [int]$Samples = 21
)
$ErrorActionPreference = 'Stop'
if ($Samples -lt 3) { throw 'At least three samples required' }
$workspace = 'C:\Users\Usuario\Desktop\ReorganizacionPC'
$run = Join-Path $workspace ('assessment/benchmarks/project-health-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($run)
function Write-Text($path, $text) { [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false)) }
function Text-Hash($text) { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text))).ToLowerInvariant() }
function Get-Manifest($directory) {
    @(Get-ChildItem -LiteralPath $directory -File -Recurse -Force | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = [IO.Path]::GetRelativePath($directory, $_.FullName); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
}
function Canonical($value) {
    if ($value -is [Collections.IDictionary]) { $result = [ordered]@{}; foreach ($key in @($value.Keys | Sort-Object)) { $result[$key] = Canonical $value[$key] }; return $result }
    if ($value -is [array]) { $items = [Collections.Generic.List[object]]::new(); foreach ($child in $value) { $items.Add((Canonical $child)) }; return ,$items.ToArray() }
    return $value
}
$previousConfig = $env:MODPACKTOOLS_CONFIG_HOME
$sourceFiles = @('Private/Graph.ps1', 'Private/LoaderMetadata.ps1', 'Private/Inventory.ps1', 'Private/Health.ps1')
$sourceHashes = @(foreach ($relative in $sourceFiles) { [ordered]@{ path = $relative; sha256 = (Get-FileHash -LiteralPath (Join-Path $LegacyRoot $relative)).Hash } })
try {
    Import-Module (Join-Path $LegacyRoot 'ModpackTools.psd1') -Force
    $module = Get-Module ModpackTools
    $results = @()
    foreach ($workload in @(@{ Name = '20-local'; Count = 20; Managed = $false }, @{ Name = '100-managed-verified'; Count = 100; Managed = $true })) {
        $fixture = Join-Path $run $workload.Name; $pack = Join-Path $fixture 'pack'; $config = Join-Path $fixture 'config'
        foreach ($relative in @('pack/.modpack', 'pack/mods', 'config/metadata', 'config/artifacts')) { [void][IO.Directory]::CreateDirectory((Join-Path $fixture $relative)) }
        Write-Text (Join-Path $pack '.modpack/project.psd1') "@{SchemaVersion=1;Id='benchmark';JavaVersion='21'}"
        Write-Text (Join-Path $pack 'pack.toml') "name=`"Benchmark`"`nversion=`"1`"`n[index]`nfile=`"index.toml`"`n[versions]`nminecraft=`"1.21`"`nfabric=`"0.16.0`"`n"
        Write-Text (Join-Path $pack 'index.toml') "hash-format=`"sha256`"`n"
        $padding = [byte[]]::new(256 * 1024); [Random]::new(42).NextBytes($padding)
        for ($index = 0; $index -lt $workload.Count; $index++) {
            $id = 'bench' + $index.ToString('D4'); $jarPath = Join-Path $fixture ($id + '.jar')
            $file = [IO.FileStream]::new($jarPath, [IO.FileMode]::CreateNew)
            try {
                $zip = [IO.Compression.ZipArchive]::new($file, [IO.Compression.ZipArchiveMode]::Create, $true)
                try {
                    $entry = $zip.CreateEntry('fabric.mod.json').Open(); $writer = [IO.StreamWriter]::new($entry, [Text.UTF8Encoding]::new($false))
                    try { $writer.Write((@{ schemaVersion = 1; id = $id; name = $id; version = '1.0.0'; environment = '*'; depends = @{ minecraft = '>=1.21'; fabricloader = '>=0.16.0' } } | ConvertTo-Json -Depth 10 -Compress)) } finally { $writer.Dispose() }
                    $asset = $zip.CreateEntry('assets/benchmark/padding.bin', [IO.Compression.CompressionLevel]::NoCompression).Open()
                    try { $asset.Write($padding) } finally { $asset.Dispose() }
                } finally { $zip.Dispose() }
            } finally { $file.Dispose() }
            if ($workload.Managed) {
                $hash = (Get-FileHash -LiteralPath $jarPath -Algorithm SHA512).Hash
                [IO.File]::Move($jarPath, (Join-Path $config ('artifacts/' + (Text-Hash ('sha512:' + $hash)))))
                Write-Text (Join-Path $pack ('mods/' + $id + '.pw.toml')) "name=`"$id`"`nfilename=`"$id.jar`"`nside=`"both`"`n[update.modrinth]`nmod-id=`"p$index`"`nversion=`"v$index`"`n"
                $version = @{ id = "v$index"; project_id = "p$index"; date_published = '2026-10-12T10:00:00Z'; files = @(@{ filename = "$id.jar"; primary = $true; url = ''; hashes = @{ sha512 = $hash } }); dependencies = @(); loaders = @('fabric'); game_versions = @('1.21') }
                Write-Text (Join-Path $config ('metadata/' + (Text-Hash "version/v$index") + '.json')) ($version | ConvertTo-Json -Depth 10 -Compress)
            } else { [IO.File]::Move($jarPath, (Join-Path $pack ('mods/' + $id + '.jar'))) }
        }
        $env:MODPACKTOOLS_CONFIG_HOME = $config
        $manifest = Get-Manifest $fixture; $now = [DateTimeOffset]::UtcNow.ToString('O')
        function Invoke-LegacyHealth {
            & $module {
                param($root)
                $project = Read-ModpackProject $root; $state = Get-MpProjectState $project; $report = Get-MpValidatedGraphReport $project $state
                $nodes = @(foreach ($item in Get-ModpackUpdateItems $state.Inventory) {
                    $node = $state.Nodes[$item.Id]
                    [ordered]@{ Id = $node.Id; Name = $item.Name; VersionId = $node.VersionId; Kind = $item.Kind; Side = $item.Side; Enabled = $node.Enabled; Pinned = $node.Pinned; Intent = $node.Intent;
                        Mods = @(foreach ($mod in $node.Mods) { [ordered]@{ Id = $mod.Id; Version = $mod.Version; Side = $mod.Side; Nested = [bool]$mod.Nested } });
                        Requirements = @($node.Requirements); Warnings = @($node.Warnings) }
                })
                [ordered]@{ Complete = $report.Complete; Errors = @($report.Errors).Count; Unknown = @($report.Unknown).Count; Warnings = @($report.Warnings).Count; Nodes = $nodes } | ConvertTo-Json -Depth 100 -Compress
            } $pack
        }
        function Invoke-NativeHealth {
            $start = [Diagnostics.ProcessStartInfo]::new([IO.Path]::GetFullPath($Executable))
            foreach ($argument in @('--inspect-project-health', $pack, $config, $now)) { $start.ArgumentList.Add($argument) }
            $start.UseShellExecute = $false; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
            $process = [Diagnostics.Process]::Start($start)
            try {
                $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
                if (-not $process.WaitForExit(60000)) { $process.Kill($true); $process.WaitForExit(); throw 'Native health timeout' }
                $text = $stdout.GetAwaiter().GetResult()
                if ($process.ExitCode -ne 0 -or $stderr.GetAwaiter().GetResult() -cne '') { throw "Native health failed: $text" }
                return $text
            } finally { $process.Dispose() }
        }
        $timer = [Diagnostics.Stopwatch]::StartNew(); $legacy = Invoke-LegacyHealth; $timer.Stop(); $firstLegacy = $timer.Elapsed.TotalMilliseconds
        $timer.Restart(); $native = Invoke-NativeHealth; $timer.Stop(); $firstNative = $timer.Elapsed.TotalMilliseconds
        $old = $legacy | ConvertFrom-Json -AsHashtable; $new = $native | ConvertFrom-Json -AsHashtable
        $common = [ordered]@{ Complete = $new.Report.Complete; Errors = $new.Report.Errors.Count; Unknown = $new.Report.Unknown.Count; Warnings = $new.Report.Warnings.Count; Nodes = @($new.Nodes | ForEach-Object {
            [ordered]@{ Id = $_.Id; Name = $_.Name; VersionId = $_.VersionId; Kind = $_.Kind; Side = $_.Side; Enabled = $_.Enabled; Pinned = $_.Pinned; Intent = $_.Intent; Mods = $_.Mods; Requirements = $_.Requirements; Warnings = $_.Warnings }
        }) }
        if (($old | ForEach-Object { Canonical $_ } | ConvertTo-Json -Depth 100 -Compress) -cne (Canonical $common | ConvertTo-Json -Depth 100 -Compress)) { throw 'Health/node projection mismatch before samples' }
        $raw = @(for ($sample = 1; $sample -le $Samples; $sample++) {
            foreach ($engine in $(if ($sample % 2) { @('legacy', 'native') } else { @('native', 'legacy') })) {
                $timer.Restart(); $out = if ($engine -eq 'native') { Invoke-NativeHealth } else { Invoke-LegacyHealth }; $timer.Stop()
                [ordered]@{ sample = $sample; engine = $engine; milliseconds = $timer.Elapsed.TotalMilliseconds; output_characters = $out.Length }
            }
        })
        $after = Get-Manifest $fixture
        if (($manifest | ConvertTo-Json -Depth 10 -Compress) -cne ($after | ConvertTo-Json -Depth 10 -Compress)) { throw 'Benchmark fixture changed' }
        $summary = @(foreach ($engine in @('legacy', 'native')) { $ordered = @($raw | Where-Object engine -eq $engine | ForEach-Object milliseconds | Sort-Object); [ordered]@{ engine = $engine; median_ms = $ordered[[int][Math]::Floor($ordered.Count / 2)]; p95_ms = $ordered[[int][Math]::Ceiling($ordered.Count * 0.95) - 1] } })
        $results += [ordered]@{ workload = $workload.Name; count = $workload.Count; fixture = $fixture; first_legacy_ms = $firstLegacy; first_native_ms = $firstNative; samples = $raw; summary = $summary; manifest = $manifest; equivalent_common_projection = $true }
        $summary | ConvertTo-Json -Compress
    }
    foreach ($source in $sourceHashes) { if ((Get-FileHash -LiteralPath (Join-Path $LegacyRoot $source.path)).Hash -cne $source.sha256) { throw 'Original source changed' } }
    $report = [ordered]@{ timestamp = [DateTimeOffset]::Now.ToString('O'); legacy_root = $LegacyRoot; legacy_revision = (& git -C $LegacyRoot rev-parse HEAD); native_sha256 = (Get-FileHash -LiteralPath $Executable).Hash; ps_version = [string]$PSVersionTable.PSVersion; culture = [cultureinfo]::CurrentCulture.Name; os = [Environment]::OSVersion.VersionString; scope = 'State and provider-validated graph; warm loaded PowerShell versus fresh Native AOT process including richer node/version JSON'; exclusions = 'No network, health persisted cache/fingerprint, public host, renderer, Nu or mutation. Synthetic fixtures, OS caches warmed, no eviction.'; source_hashes = $sourceHashes; results = $results }
    $resultPath = Join-Path $run 'results.json'; Write-Text $resultPath ($report | ConvertTo-Json -Depth 100)
    "Evidence: $resultPath"
} finally { Remove-Module ModpackTools -ErrorAction SilentlyContinue; $env:MODPACKTOOLS_CONFIG_HOME = $previousConfig }
