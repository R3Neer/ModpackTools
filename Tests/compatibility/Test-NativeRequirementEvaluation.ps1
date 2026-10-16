param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$LegacyRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
function Req([string]$target='m',[string]$kind='required',$range='*') {
 return @{Target=$target;Range=$range;Kind=$kind;Scope='mod';Side='both';Maven=$false;Source='manifest';SuggestedVersionId='';Any=@();All=@();Unless=$null}
}
$present=Req;$absent=Req 'absent';$unknown=Req 'java';$bad=Req 'm' 'required' '~٢.0'
$cases=[Collections.Generic.List[object]]::new()
foreach($kind in @('required','optional','recommended','incompatible','discouraged')) {
 $cases.Add(@{requirement=(Req 'absent' $kind);mods=@{m=@('1')};projects=@{}})
 $cases.Add(@{requirement=(Req 'm' $kind);mods=@{m=@('1')};projects=@{}})
}
foreach($versions in @(@{v=@()},@{v=@($null)},@{v=@('')},@{v=@('','1')},@{v=@('opaque','1')})) {
 $cases.Add(@{requirement=(Req 'm' 'required' '>=1');mods=@{m=$versions.v};projects=@{}})
}
foreach($condition in @($present,$unknown,$absent)) {
 $r=$bad.Clone();$r.Unless=$condition;$cases.Add(@{requirement=$r;mods=@{m=@('1')};projects=@{}})
}
foreach($group in @(@{op='Any';v=@($present,$bad)},@{op='All';v=@($absent,$bad)},@{op='Any';v=@($unknown,$present)},@{op='All';v=@($unknown,$absent)})) {
 $r=Req;$r[$group.op]=$group.v;$cases.Add(@{requirement=$r;mods=@{m=@('1')};projects=@{}})
}
foreach($range in @(@{v='*'},@{v='Version'},@{v='version'},@{v=$null},@{v=''})) {
 $r=Req 'p' 'required' $range.v;$r.Scope='project';$cases.Add(@{requirement=$r;mods=@{};projects=@{p='Version'}})
}
$r=Req 'p';$r.Scope='project';$r.SuggestedVersionId='other';$cases.Add(@{requirement=$r;mods=@{};projects=@{p='Version'}})
$r=Req;$r.Any=@($unknown);$r.All=@($present);$cases.Add(@{requirement=$r;mods=@{m=@('1')};projects=@{}})
$directory=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-requirements-'+[guid]::NewGuid().ToString('N'))
try {
 [void][IO.Directory]::CreateDirectory($directory)
 Import-Module (Join-Path $LegacyRoot 'ModpackTools.psd1') -Force;$module=Get-Module ModpackTools;$count=0
 foreach($case in $cases) {
  $nodes=@{};foreach($entry in $case.projects.GetEnumerator()){$nodes[$entry.Key]=[pscustomobject]@{VersionId=$entry.Value}}
  $old=& $module {param($r,$n,$m)try{[pscustomobject]@{Threw=$false;Value=(Test-MpRequirement $r $n $m)}}catch{[pscustomobject]@{Threw=$true;Value=$null}}} $case.requirement $nodes $case.mods
  $path=Join-Path $directory "$count.json";$case|ConvertTo-Json -Depth 60 -Compress|Set-Content -LiteralPath $path -Encoding utf8NoBOM
  $native=Invoke-NativeTestProcess $Executable @('--inspect-requirement',$path)
  if($native.Stderr){throw "Requirement ${count}: stderr emitted"}
  if($old.Threw) {
   $failure=$native.Stdout|ConvertFrom-Json -AsHashtable
   if($native.Exit-ne1-or$failure.error.id-notlike'*Version.*'){throw "Requirement ${count}: consumed error was lost: $($native.Stdout)"}
  } else {
   $actual=$native.Stdout|ConvertFrom-Json
   if($native.Exit-ne0-or$null-eq$old.Value-and$null-ne$actual-or$null-ne$old.Value-and($actual-isnot[bool]-or$actual-ne$old.Value)){throw "Requirement ${count}: tri-state mismatch: $($native.Stdout)"}
  }
  $count++
 }
 "Native requirement evaluation: $count differential presence/composite/project/consumed-error cases passed"
} finally {
 if(Test-Path -LiteralPath $directory){$full=[IO.Path]::GetFullPath($directory);if(-not$full.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase)-or[IO.Path]::GetFileName($full)-notlike'modpacktools-requirements-*'){throw 'Unsafe requirement fixture cleanup'};Remove-Item -LiteralPath $full -Recurse -Force}
}
