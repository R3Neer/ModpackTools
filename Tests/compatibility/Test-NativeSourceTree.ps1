param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$LegacyRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-source-tree-'+[guid]::NewGuid().ToString('N'))
try {
 [void][IO.Directory]::CreateDirectory((Join-Path $fixture '.git'))
 [void][IO.Directory]::CreateDirectory((Join-Path $fixture 'dist'))
 [void][IO.Directory]::CreateDirectory((Join-Path $fixture 'sub/dist'))
 [IO.File]::WriteAllText((Join-Path $fixture '.git/ignored'),'ignored')
 [IO.File]::WriteAllText((Join-Path $fixture 'a.txt'),'AAA')
 [IO.File]::WriteAllText((Join-Path $fixture 'dist/artifact'),'OLD')
 [IO.File]::WriteAllText((Join-Path $fixture 'sub/dist/included'),'nested dist')
 [IO.File]::WriteAllText((Join-Path $fixture 'hidden'),'hidden system')
 [IO.File]::SetAttributes((Join-Path $fixture 'hidden'),[IO.FileAttributes]::Hidden-bor[IO.FileAttributes]::System)
 Import-Module (Join-Path $LegacyRoot 'ModpackTools.psd1') -Force
 $module=Get-Module ModpackTools
 function Inspect([string]$root){
  $native=Invoke-NativeTestProcess $Executable @('--inspect-source-tree',$root)
  if($native.Exit-ne0-or$native.Stderr){throw "Source tree failed: $($native.Stdout)"}
  $actual=$native.Stdout|ConvertFrom-Json -AsHashtable -DateKind String
  $expected=&$module {param($root)Get-MpTreeState $root} $root
  if($actual.root-cne[IO.Path]::GetFullPath($root)-or$actual.files.Count-ne$expected.Count){throw 'Source root or complete file count changed'}
  foreach($path in $expected.Keys){if(!$actual.files.ContainsKey($path)-or$actual.files[$path]-cne$expected[$path]){throw "Source hash or spelling changed: $path"}}
  $health=&$module {param($tree)Get-MpHash ((@($tree.Keys|Where-Object{$_-notlike'dist/*'}|Sort-Object|ForEach-Object{"$_=$($tree[$_])"})-join"`n")+'|validator=3')} $expected
  $address=&$module {param($root)Get-MpHash ([IO.Path]::GetFullPath($root).ToLowerInvariant())} $root
  if($actual.health_fingerprint-cne$health-or$actual.root_hash-cne$address){throw 'Ordinary health fingerprint/root hash changed'}
  $actual
 }
 $before=Inspect $fixture
 [void](Inspect (Join-Path $fixture 'a.txt'))
 [void](Inspect (Join-Path $fixture '.git/ignored'))
 [void](Inspect (Join-Path $fixture '.git'))
 $time=[IO.File]::GetLastWriteTimeUtc((Join-Path $fixture 'dist/artifact'))
 [IO.File]::WriteAllText((Join-Path $fixture 'dist/artifact'),'NEW');[IO.File]::SetLastWriteTimeUtc((Join-Path $fixture 'dist/artifact'),$time)
 $after=Inspect $fixture
 if($before.source_fingerprint-ceq$after.source_fingerprint-or$before.health_fingerprint-cne$after.health_fingerprint){throw 'Dist bytes were not protected separately from health'}
 $diff=Invoke-NativeTestProcess $Executable @('--inspect-source-changes',(ConvertTo-Json -InputObject $before.files -Compress),(ConvertTo-Json -InputObject $after.files -Compress))
 $rows=@($diff.Stdout|ConvertFrom-Json -NoEnumerate)
 if($diff.Exit-ne0-or$diff.Stderr-or$rows.Count-ne1-or$rows[0].Path-cne'dist/artifact'-or$rows[0].Before-cne$before.files['dist/artifact']-or$rows[0].After-cne$after.files['dist/artifact']){throw 'Full source change projection changed'}
 $unicode=Invoke-NativeTestProcess $Executable @('--inspect-source-changes','{"é.txt":"A","é.txt":"A"}','{"é.txt":"A","é.txt":"B"}')
 $changed=@($unicode.Stdout|ConvertFrom-Json -NoEnumerate)
 if($unicode.Exit-ne0-or$unicode.Stderr-or$changed.Count-ne1-or$changed[0].Path-cne'é.txt'){throw 'E24 still loses a culturally equivalent distinct path'}
 $shadow=Invoke-NativeTestProcess $Executable @('--inspect-source-changes','{"Keys":"A","other":"A"}','{"Keys":"B","other":"A"}')
 $shadowed=$shadow.Stdout|ConvertFrom-Json -NoEnumerate
 if($shadow.Exit-ne0-or$shadow.Stderr-or$shadowed.Count-ne1-or$shadowed[0].Path-cne'Keys'-or$shadowed[0].Before-cne'A'-or$shadowed[0].After-cne'B'){throw 'E24 still hides a path through dictionary member shadowing'}
 $count=8
 foreach($text in @('',"é é ß Æ 😀",[string][char]0xD800,[string][char]0xDC00,('A'*4095+'😀B'+[char]0xD800))){
  $units=ConvertTo-Json -InputObject @($text.ToCharArray()|ForEach-Object{[int]$_}) -Compress
  $native=Invoke-NativeTestProcess $Executable @('--inspect-text-hash',$units)
  $expected=&$module {param($text)Get-MpHash $text} $text
  if($native.Exit-ne0-or$native.Stderr-or($native.Stdout|ConvertFrom-Json)-cne$expected){throw 'Streaming UTF8 text hash changed, including surrogate boundaries'}
  $count++
 }
 $bounded=Join-Path $fixture 'bounded';$child=Join-Path $bounded 'child'
 [void][IO.Directory]::CreateDirectory($child)
 [IO.File]::WriteAllBytes((Join-Path $bounded 'one'),[byte[]]@(1));[IO.File]::WriteAllBytes((Join-Path $child 'two'),[byte[]]@(2))
 $characters=$bounded.Length+$child.Length+(Join-Path $bounded 'one').Length+(Join-Path $child 'two').Length
 $exact=Invoke-NativeTestProcess $Executable @('--inspect-source-tree',$bounded,'--max-tree-files','2','--max-tree-directories','2','--max-tree-bytes','2','--max-tree-path-chars',([string]$characters))
 if($exact.Exit-ne0-or$exact.Stderr){throw "Exact source quotas failed $($exact.Stdout)"};$count++
 foreach($probe in @(@('--max-tree-files','1'),@('--max-tree-directories','1'),@('--max-tree-bytes','1'),@('--max-tree-path-chars',([string]($characters-1))))){
  $native=Invoke-NativeTestProcess $Executable (@('--inspect-source-tree',$bounded)+$probe)
  $failure=$native.Stdout|ConvertFrom-Json
  if($native.Exit-ne1-or$native.Stderr-or$failure.error.id-cne'ModpackTools.Source.TreeReadLimitExceeded'-or$failure.error.target-cne$bounded-or!$failure.error.details){throw 'Source limit returned partial data or wrong context'}
  $count++
 }
 foreach($flag in @('--max-tree-files','--max-tree-directories','--max-tree-bytes','--max-tree-path-chars')){
  $native=Invoke-NativeTestProcess $Executable @('--inspect-source-tree',(Join-Path $fixture 'absent'),$flag,'0')
  if($native.Exit-ne1-or$native.Stderr-or($native.Stdout|ConvertFrom-Json).error.id-cne'ModpackTools.Source.InvalidTreeReadPolicy'){throw 'Source policy did not precede IO'}
  $count++
 }
 $locked=[IO.File]::Open((Join-Path $bounded 'one'),[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
 try{$native=Invoke-NativeTestProcess $Executable @('--inspect-source-tree',$bounded);if($native.Exit-ne1-or$native.Stderr-or($native.Stdout|ConvertFrom-Json).error.id-cne'ModpackTools.Source.TreeReadFailed'){throw 'Failed source read returned a partial snapshot'}}finally{$locked.Dispose()}
 $count++
 foreach($path in @((Join-Path $bounded 'one'),(Join-Path $child 'two'))){$exclusive=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);$exclusive.Dispose()}
 "Native source tree: $count complete legacy-tree/hash, content-edit, E24, UTF8/surrogate, exact/over quota, policy and fail-closed IO controls passed"
} finally {
 Remove-Module ModpackTools -ErrorAction SilentlyContinue
 $resolved=[IO.Path]::GetFullPath($fixture)
 $temporaryRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
 if(!$resolved.StartsWith($temporaryRoot,[StringComparison]::OrdinalIgnoreCase)-or[IO.Path]::GetFileName($resolved)-notlike'modpacktools-source-tree-*'){throw 'Unsafe source tree fixture cleanup'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
