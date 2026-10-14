param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$PackRoot,[Parameter(Mandatory)][string]$LegacyRoot,[int]$Samples=21)
$ErrorActionPreference='Stop'
if($Samples -lt 3){throw 'At least three samples are required'}
$workspace='C:\Users\Usuario\Desktop\ReorganizacionPC'
$run=Join-Path $workspace ('assessment/benchmarks/raw-inventory-'+[guid]::NewGuid().ToString('N'))
$fixture=Join-Path $run 'pack';[void][IO.Directory]::CreateDirectory($fixture)
$source=[IO.Path]::GetFullPath($PackRoot);$files=[Collections.Generic.List[IO.FileInfo]]::new()
foreach($relative in @('pack.toml','.modpack/project.psd1','.modpack/metadata.psd1','config/defaultoptions-common.toml')){$file=Get-Item -LiteralPath (Join-Path $source $relative) -ErrorAction SilentlyContinue;if($file -is [IO.FileInfo]){$files.Add($file)}}
foreach($kind in @('mods','resourcepacks','shaderpacks')){
    $path=Join-Path $source $kind
    if(-not [IO.Directory]::Exists($path)){continue}
    [void][IO.Directory]::CreateDirectory((Join-Path $fixture $kind))
    foreach($directory in Get-ChildItem -LiteralPath $path -Directory -Recurse -Force){
        if($directory.LinkTarget){continue};$relative=[IO.Path]::GetRelativePath($source,$directory.FullName);$copy=Join-Path $fixture $relative
        [void][IO.Directory]::CreateDirectory($copy);[IO.File]::SetAttributes($copy,($directory.Attributes -band (-bnot [IO.FileAttributes]::ReparsePoint)))
    }
    foreach($file in Get-ChildItem -LiteralPath $path -File -Recurse -Force){if($file.Name -match '(?i)\.pw\.toml$|\.jar$|\.zip$'){$files.Add($file)}}
}
$manifest=@(foreach($file in $files){
    $relative=[IO.Path]::GetRelativePath($source,$file.FullName);$copy=Join-Path $fixture $relative
    [void][IO.Directory]::CreateDirectory((Split-Path $copy -Parent));$before=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    [IO.File]::Copy($file.FullName,$copy);[IO.File]::SetAttributes($copy,($file.Attributes -band (-bnot [IO.FileAttributes]::ReparsePoint)))
    if((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -cne $before){throw "Source changed while copying $relative"}
    [ordered]@{path=$relative;bytes=$file.Length;attributes=[int]$file.Attributes;sha256=$before}
})
$previous=$env:MODPACKTOOLS_CONFIG_HOME
$env:MODPACKTOOLS_CONFIG_HOME=Join-Path $run 'config';[void][IO.Directory]::CreateDirectory($env:MODPACKTOOLS_CONFIG_HOME)
try {
    Import-Module (Join-Path $LegacyRoot 'ModpackTools.psd1') -Force
    $module=Get-Module ModpackTools
    function Invoke-Legacy {
        & $module {
            param($root)
            function Physical($items){return ,@(foreach($item in $items){$row=[ordered]@{id=$item.Id;kind=$item.Kind;name=$item.Name;filename=$item.Filename;side=$item.Side;source=$item.Source;metadata_path=$item.MetadataPath;category=$item.Category};if($item.Kind -eq 'mod'){$row.invalid_category=$item.InvalidCategory};if($item.Source -eq 'packwiz'){$row.version_id=$item.VersionId;$row.pinned=$item.Pinned};$row})}
            $project=Read-ModpackProject $root;$snapshot=Get-ModpackInventory $project;$p=ConvertTo-MpMachineProject $project;$p.index_file=$project.IndexFile;$p.index_path=$project.IndexPath
            $active=@(foreach($item in $snapshot.ActiveResources){[ordered]@{id=$item.Id;kind=$item.Kind;name=$item.Name;filename=$item.Filename;source=$item.Source;enabled=$item.Enabled;priority=$item.Priority}})
            ConvertTo-Json -InputObject ([ordered]@{project=$p;metadata=$snapshot.Metadata;mods=(Physical $snapshot.Mods);resources=(Physical $snapshot.ResourcePacks);active=$active;inactive=(Physical $snapshot.InactiveResources);shaders=(Physical $snapshot.Shaders)}) -Depth 100 -Compress
        } $fixture
    }
    function Invoke-Aot {
        $start=[Diagnostics.ProcessStartInfo]::new([IO.Path]::GetFullPath($Executable));$start.ArgumentList.Add('--inspect-inventory');$start.ArgumentList.Add($fixture)
        $start.UseShellExecute=$false;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true;$process=[Diagnostics.Process]::Start($start)
        try{$out=$process.StandardOutput.ReadToEndAsync();$err=$process.StandardError.ReadToEndAsync();if(-not $process.WaitForExit(10000)){$process.Kill($true);$process.WaitForExit();throw 'AOT timeout'};$text=$out.GetAwaiter().GetResult();if($process.ExitCode -ne 0 -or $err.GetAwaiter().GetResult() -cne ''){throw "AOT failure: $text"};return $text}finally{$process.Dispose()}
    }
    function Canonical($value){if($value -is [Collections.IDictionary]){$result=[ordered]@{};foreach($key in @($value.Keys|Sort-Object)){$result[$key]=Canonical $value[$key]};return $result};if($value -is [array]){$items=[Collections.Generic.List[object]]::new();foreach($child in $value){$items.Add((Canonical $child))};return ,$items.ToArray()};return $value}
    $timer=[Diagnostics.Stopwatch]::StartNew();$legacyFirst=Invoke-Legacy;$timer.Stop();$firstLegacy=$timer.Elapsed.TotalMilliseconds
    $timer.Restart();$nativeFirst=Invoke-Aot;$timer.Stop();$firstNative=$timer.Elapsed.TotalMilliseconds
    $old=ConvertFrom-Json -InputObject $legacyFirst -AsHashtable;$new=ConvertFrom-Json -InputObject $nativeFirst -AsHashtable;[void]$new.Remove('local_mod_observations')
    if((ConvertTo-Json -InputObject (Canonical $old) -Depth 100 -Compress) -cne (ConvertTo-Json -InputObject (Canonical $new) -Depth 100 -Compress)){throw 'Real-pack fixture projection mismatch before timing series'}
    $raw=@(for($index=0;$index -lt $Samples;$index++){
        $order=if($index%2){@('aot','legacy')}else{@('legacy','aot')}
        foreach($engine in $order){$timer.Restart();$output=if($engine -eq 'aot'){Invoke-Aot}else{Invoke-Legacy};$timer.Stop();[ordered]@{sample=$index+1;engine=$engine;ms=$timer.Elapsed.TotalMilliseconds;output_chars=$output.Length}}
    })
    foreach($entry in $manifest){$original=Join-Path $source $entry.path;if((Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash -cne $entry.sha256 -or [int](Get-Item -LiteralPath $original -Force).Attributes -ne $entry.attributes){throw "Source changed during benchmark: $($entry.path)"}}
    $summary=@(foreach($engine in @('legacy','aot')){$ordered=@($raw|Where-Object engine -eq $engine|ForEach-Object ms|Sort-Object);[ordered]@{engine=$engine;median_ms=$ordered[[int][Math]::Floor($ordered.Count/2)];p95_ms=$ordered[[int][Math]::Ceiling($ordered.Count*0.95)-1]}})
    $report=[ordered]@{scope='raw inventory plus equivalent projection and JSON; loaded PS vs fresh Native AOT inspector';timestamp=[DateTimeOffset]::Now.ToString('O');source=$source;fixture=$fixture;native_sha256=(Get-FileHash -LiteralPath $Executable -Algorithm SHA256).Hash;legacy_revision=(& git -C $LegacyRoot rev-parse HEAD);ps_version=[string]$PSVersionTable.PSVersion;culture=[cultureinfo]::CurrentCulture.Name;os=[Environment]::OSVersion.VersionString;cache='Copied fixture, warmed OS cache; no eviction, no persistent inventory/health cache';first_legacy_ms=$firstLegacy;first_aot_ms=$firstNative;counts=@{mods=$old.mods.Count;resources=$old.resources.Count;active=$old.active.Count;inactive=$old.inactive.Count;shaders=$old.shaders.Count};samples=$raw;summary=$summary;manifest=$manifest;notes='Native inspector also emits original JAR observations. No filters/references/health/renderer/adapters/network. No competing agent tests or builds intentionally started. Source hashes/attributes unchanged.'}
    $outputPath=Join-Path $run 'results.json';[IO.File]::WriteAllText($outputPath,($report|ConvertTo-Json -Depth 100));$summary|ConvertTo-Json -Compress;"Evidence: $outputPath"
} finally {Remove-Module ModpackTools -ErrorAction SilentlyContinue;$env:MODPACKTOOLS_CONFIG_HOME=$previous}
