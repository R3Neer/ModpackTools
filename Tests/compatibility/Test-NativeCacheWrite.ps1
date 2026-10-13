param([Parameter(Mandatory)][string]$Executable,[string]$LegacyRoot='D:\Tools\ModpackTools')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-cache-write-'+[guid]::NewGuid().ToString('N'))
try {
 $pack=Join-Path $fixture 'pack';$configuration=Join-Path $fixture 'configuration'
 foreach($directory in @('.modpack','mods','resourcepacks','shaderpacks','config')){[void][IO.Directory]::CreateDirectory((Join-Path $pack $directory))}
 [IO.File]::WriteAllText((Join-Path $pack '.modpack/project.psd1'),"@{SchemaVersion=1;Id='fixture'}")
 [IO.File]::WriteAllText((Join-Path $pack 'pack.toml'),'[index]')
 [IO.File]::WriteAllText((Join-Path $pack 'mods/one.pw.toml'),"name = `"One`"`n[update.modrinth]`nmod-id = `"one`"`n")
 [IO.File]::WriteAllText((Join-Path $pack 'mods/local.jar'),'unreadable optional display metadata')
 [IO.File]::WriteAllText((Join-Path $pack 'resourcepacks/A.zip'),'local resource')
 [IO.File]::WriteAllText((Join-Path $pack 'shaderpacks/S.zip'),'local shader')
 [IO.File]::WriteAllText((Join-Path $pack 'config/defaultoptions-common.toml'),'defaultResourcePacks=["file/A.zip","vanilla","file/"]')
 [IO.File]::WriteAllText((Join-Path $pack '.modpack/metadata.psd1'),"@{Categories=@{unclassified=@{}};Mods=@{'modrinth:one'=@{Name='中文 🎮'}}}")
 function PackSnapshot {return @(Get-ChildItem -LiteralPath $pack -Recurse -Force -File|Sort-Object FullName|ForEach-Object {($_.FullName+'|'+(Get-FileHash -LiteralPath $_.FullName).Hash+'|'+[string]$_.Attributes)}) -join "`n"}
 $before=PackSnapshot
 $clock=[DateTimeOffset]::UtcNow.AddMinutes(-5);$stamp=$clock.ToOffset([TimeSpan]::FromHours(2)).ToString('O')
 $tokens=@('--inspect-write-reference-cache',$pack,$configuration,'--created-utc',$stamp)
 $result=Invoke-NativeTestProcess $Executable $tokens
 if($result.Exit -ne 0 -or $result.Stderr -cne ''){throw "Fresh cache write failed $($result.Stdout) $($result.Stderr)"}
 $path=Join-Path $configuration 'last-inventory.json';$bytes=[IO.File]::ReadAllBytes($path);$cache=[Text.Encoding]::UTF8.GetString($bytes)|ConvertFrom-Json -DateKind String
 if($cache.SchemaVersion -ne 1 -or $cache.CreatedUtc -cne $clock.UtcDateTime.ToString('O') -or $cache.ProjectId -cne 'fixture' -or $cache.Results.Count -ne ($result.Stdout|ConvertFrom-Json).count -or ($bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191)){throw 'Cache schema, UTC timestamp, count or BOM mismatch'}
 $reader=Join-Path $fixture 'legacy-reader.ps1'
 [IO.File]::WriteAllText($reader,@'
param($ModulePath,$RootPath,$ConfigurationDirectory)
Import-Module $ModulePath -Force
& (Get-Module ModpackTools) {
 param($root,$configuration)
 $script:ConfigHomeOverride=$configuration
 $cache=Read-ModpackInventoryReferenceCache;$project=Read-ModpackProject $root
 foreach($row in $cache.Results){$resolved=Resolve-ModpackInventoryNumber -Selector ([string]$row.Index) -Project $project;if($resolved.Id -cne $row.Id -or $resolved.Selector -cne $row.Selector){throw 'Legacy cache resolution mismatch'}}
 # ConvertFrom-Json materializes dates; compare the same UTC instant at file precision.
 $cache.CreatedUtc=([DateTimeOffset]$cache.CreatedUtc).UtcDateTime.ToString('O')
 $cache|ConvertTo-Json -Depth 8 -Compress
} $RootPath $ConfigurationDirectory
'@)
 $legacy=Invoke-NativeTestProcess (Get-Command pwsh).Source @('-NoProfile','-File',$reader,'-ModulePath',(Join-Path $LegacyRoot 'ModpackTools.psd1'),'-RootPath',$pack,'-ConfigurationDirectory',$configuration)
 if($legacy.Exit -ne 0 -or (($legacy.Stdout|ConvertFrom-Json -DateKind String)|ConvertTo-Json -Depth 8 -Compress) -cne ($cache|ConvertTo-Json -Depth 8 -Compress)){throw "Legacy reader could not consume complete native cache $($legacy.Stdout) $($legacy.Stderr)"}
 $sentinel=Join-Path $configuration '.last-inventory.json.unrelated.tmp';[IO.File]::WriteAllText($sentinel,'preserve')
 $exact=Invoke-NativeTestProcess $Executable ($tokens+@('--max-cache-bytes',[string]$bytes.Length))
 if($exact.Exit -ne 0 -or [Convert]::ToBase64String([IO.File]::ReadAllBytes($path)) -cne [Convert]::ToBase64String($bytes)){throw 'Exact cache byte quota failed'}
 $over=Invoke-NativeTestProcess $Executable ($tokens+@('--max-cache-bytes',[string]($bytes.Length-1)))
 if($over.Exit -ne 1 -or ($over.Stdout|ConvertFrom-Json).error.id -cne 'ModpackTools.Inventory.ReferenceCacheWriteLimitExceeded' -or [Convert]::ToBase64String([IO.File]::ReadAllBytes($path)) -cne [Convert]::ToBase64String($bytes)){throw 'Cache quota failure changed old bytes'}
 $absent=Join-Path $fixture 'absent';$invalid=Invoke-NativeTestProcess $Executable @('--inspect-write-reference-cache',$pack,$absent,'--max-cache-bytes','0')
 if($invalid.Exit -ne 1 -or ($invalid.Stdout|ConvertFrom-Json).error.id -cne 'ModpackTools.Inventory.InvalidReferenceCacheWritePolicy' -or (Test-Path -LiteralPath $absent)){throw 'Invalid write quota created effects'}
 $lock=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
 try{$locked=Invoke-NativeTestProcess $Executable $tokens;if($locked.Exit -ne 1 -or ($locked.Stdout|ConvertFrom-Json).error.id -cne 'ModpackTools.Inventory.ReferenceCacheWriteFailed'){throw 'Locked-cache write was not rejected'}}finally{$lock.Dispose()}
 if([Convert]::ToBase64String([IO.File]::ReadAllBytes($path)) -cne [Convert]::ToBase64String($bytes)){throw 'Locked replacement changed old bytes'}
 $left=@(Get-ChildItem -LiteralPath $configuration -Filter '.last-inventory.json.*.tmp' -Force -File)
 if($left.Count -ne 1 -or $left[0].FullName -cne $sentinel -or [IO.File]::ReadAllText($sentinel) -cne 'preserve'){throw 'Cache cleanup damaged an unrelated file or leaked its temporary'}
 $empty=Invoke-NativeTestProcess $Executable ($tokens+@('--match','no matches'))
 $emptyCache=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
 if($empty.Exit -ne 0 -or $emptyCache.Results -isnot [array] -or $emptyCache.Results.Count -ne 0){throw 'Empty cache replacement failed'}
 $exclusive=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);$exclusive.Dispose()
 if((PackSnapshot) -cne $before){throw 'Cache persistence changed the source pack'}
 'Native cache writer: complete legacy-compatible Unicode cache, exact/over quotas, invalid policy, locked replacement, cleanup, empty replacement and handle/source integrity passed'
}finally{
 $resolved=[IO.Path]::GetFullPath($fixture)
 if(-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-cache-write-*'){throw 'Unsafe fixture cleanup target'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
