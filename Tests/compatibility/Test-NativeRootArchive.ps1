param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$LegacyRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-root-archive-'+[guid]::NewGuid().ToString('N'))
$f='{schemaVersion:1,id:"m",version:"1",depends:{d:">=1"}}'
$q='{schema_version:1,quilt_loader:{id:"q",version:"2"}}'
$t="[[mods]]`nmodId='g'`nversion='1'"
$cases=@(
 @{l='fabric';e=@(@('fabric.mod.json',$f))},@{l='quilt';e=@(@('fabric.mod.json','invalid'),@('quilt.mod.json',$q))},
 @{l='forge';e=@(@('fabric.mod.json','invalid'),@('META-INF/mods.toml',$t))},
 @{l='neoforge';e=@(@('META-INF/neoforge.mods.toml',''),@('META-INF/mods.toml',$t))},
 @{l='neoforge';e=@(@('META-INF/neoforge.mods.toml','broken='),@('META-INF/mods.toml',$t))},
 @{l='fabric';e=@(@('fabric.mod.json',''),@('fabric.mod.json',$f))},@{l='fabric';e=@(@('FABRIC.MOD.JSON',$f))},
 @{l='fabric';e=@(@('fabric.mod.json',$f));encoding='unicode'},@{l='fabric';e=@(@('fabric.mod.json',$f));encoding='utf8bom'},
 @{l='forge';e=@(@('META-INF/mods.toml','broken='),@('META-INF/MANIFEST.MF',('x'*(4MB+1))))},
 @{l='forge';e=@(@('META-INF/mods.toml',$t+"`nfeatures=1"),@('META-INF/jarjar/metadata.json',('x'*(4MB+1))))},
 @{l='forge';e=@(@('META-INF/mods.toml',$t),@('META-INF/jarjar/metadata.json',('x'*(4MB+1))))},
 @{l='other';e=@(@('fabric.mod.json',$f))},@{l='fabric';e=@()}
)
function Same($a,$b){
 if($null-eq$a-or$null-eq$b){return $null-eq$a-and$null-eq$b}
 if($a-is[Collections.IDictionary]-and$b-is[Collections.IDictionary]){if($a.Count-ne$b.Count){return $false};foreach($entry in $a.GetEnumerator()){if(-not$b.Contains($entry.Key)-or-not(Same $entry.Value $b[$entry.Key])){return $false}};return $true}
 if($a-is[array]-and$b-is[array]){if($a.Count-ne$b.Count){return $false};for($i=0;$i-lt$a.Count;$i++){if(-not(Same $a[$i] $b[$i])){return $false}};return $true}
 return $a.GetType()-eq$b.GetType()-and$a-ceq$b
}
try{
 [void][IO.Directory]::CreateDirectory($fixture)
 Import-Module (Join-Path $LegacyRoot 'ModpackTools.psd1') -Force;$module=Get-Module ModpackTools;$count=0
 foreach($case in $cases){
  $path=Join-Path $fixture "$count.jar";$file=[IO.FileStream]::new($path,[IO.FileMode]::Create)
  $archive=[IO.Compression.ZipArchive]::new($file,[IO.Compression.ZipArchiveMode]::Create)
  try{foreach($entry in $case.e){$stream=$archive.CreateEntry($entry[0]).Open();try{
   $encoding=if($case.encoding-eq'unicode'){[Text.Encoding]::Unicode}elseif($case.encoding-eq'utf8bom'){[Text.UTF8Encoding]::new($true)}else{[Text.UTF8Encoding]::new($false)}
   $preamble=$encoding.GetPreamble();$stream.Write($preamble);$stream.Write($encoding.GetBytes([string]$entry[1]))
  }finally{$stream.Dispose()}}}finally{$archive.Dispose();$file.Dispose()}
  $before=(Get-FileHash -LiteralPath $path).Hash
  $old=&$module {param($p,$l)Get-MpLoaderMetadata $p $l} $path $case.l
  $native=Invoke-NativeTestProcess $Executable @('--inspect-loader-archive-root',$path,$case.l)
  if($native.Exit-ne0-or$native.Stderr){throw "Archive transport failure: $($native.Stdout)"}
  $actual=ConvertFrom-Json -InputObject $native.Stdout -AsHashtable -DateKind String
  $expected=@(foreach($mod in $old.Mods){[ordered]@{Id=$mod.Id;Version=$mod.Version;Side=$mod.Side;Nested=$false;Aliases=@();Requirements=@($mod.Requirements|ForEach-Object {[ordered]@{Target=$_.Target;RangeDisplay=($_.Range-join' OR ');Kind=$_.Kind;Side=$_.Side;Scope=$_.Scope;Maven=$_.Maven;Source=$_.Source;SuggestedVersionId=$_.SuggestedVersionId;RangeChecks=@();Any=@();All=@();Unless=$null}})}})
  if(-not(Same $expected @($actual.mods))){throw "Archive case ${count}: complete mod mismatch $($native.Stdout)"}
  if($old.Warnings.Count-ne$actual.warnings.Count){throw "Archive case ${count}: warning count mismatch"}
  for($i=0;$i-lt$old.Warnings.Count;$i++){
   if($old.Warnings[$i]-like'Manifest could not be fully read:*'){if($actual.warnings[$i]-notlike'Manifest could not be fully read:*'-or$actual.can_traverse_nested){throw "Archive case ${count}: failure stage mismatch"}}
   elseif($old.Warnings[$i]-cne$actual.warnings[$i]){throw "Archive case ${count}: warning mismatch"}
  }
  if((Get-FileHash -LiteralPath $path).Hash-cne$before){throw 'Archive was mutated'}
  $exclusive=[IO.FileStream]::new($path,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);$exclusive.Dispose();$count++
 }
 "Native root archive: $count complete readonly/stage/encoding/duplicate/handle comparisons passed"
}finally{
 if(Test-Path -LiteralPath $fixture){$resolved=[IO.Path]::GetFullPath($fixture);$root=[IO.Path]::GetFullPath([IO.Path]::GetTempPath());if(-not$resolved.StartsWith($root,[StringComparison]::OrdinalIgnoreCase)-or[IO.Path]::GetFileName($resolved)-notlike'modpacktools-root-archive-*'){throw 'Unsafe fixture cleanup'};Remove-Item -LiteralPath $resolved -Recurse -Force}
}
