param([Parameter(Mandatory)][string]$Executable,[string]$LegacyRoot='D:\Tools\ModpackTools')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-category-cache-'+[guid]::NewGuid().ToString('N'))
$oldConfig=[Environment]::GetEnvironmentVariable('MODPACKTOOLS_CONFIG_HOME')
$clock='2026-10-13T12:00:00.0000000Z'
function Sentence([string]$value){$value=$value.Trim();if($value-notmatch'[.!?]$'){$value+='.'};$value}
try {
 [void][IO.Directory]::CreateDirectory((Join-Path $fixture '.modpack'))
 $metadataPath=Join-Path $fixture '.modpack/metadata.psd1'
 $metadata="@{Categories=@{qol='unused scalar';'é'=@{Order='unused bad'};True=@{};'1'=@{};'A B'=@{};''=@{}}}"
 $path=Join-Path $fixture 'last-categories.json'
 $normal='{"SchemaVersion":99,"CreatedUtc":"2026-10-13T12:00:00Z","ProjectId":"fixture","Categories":[{"Index":1,"Id":"qol"}]}'
 $env:MODPACKTOOLS_CONFIG_HOME=$fixture
 Import-Module (Join-Path $LegacyRoot 'ModpackTools.psd1') -Force
 $module=Get-Module ModpackTools
 &$module {param($clock)$body=(Get-Command Test-MpCacheTimestamp).Definition;Set-Item Function:script:Test-MpCacheTimestamp ([scriptblock]::Create($body.Replace('[datetimeoffset]::UtcNow',"([datetimeoffset]::Parse('$clock'))")))} $clock
 $cases=@(
  @{},@{selector='QOL';text='{bad'},@{selector='é';text='{bad'},@{selector='é'},@{selector='absent'},
  @{selector='UNCLASSIFIED';metadata='bad metadata';text='{bad'},@{selector='UNCLASSIFIED';allow=$true;metadata='bad metadata';text='{bad'},
  @{text=$normal.Replace('"SchemaVersion":99,','')},@{text=$normal.Replace('"ProjectId":"fixture"','"ProjectId":"other"')},
  @{text=$normal.Replace('2026-10-13T12:00:00Z','2026-10-12T12:00:00Z')},
  @{text=$normal.Replace('2026-10-13T12:00:00Z','2026-10-12T11:59:59.9999999Z')},
  @{text=$normal.Replace('2026-10-13T12:00:00Z','2026-10-14T12:00:00Z')},
  @{text=$normal.Replace('[{"Index":1,"Id":"qol"}]','[]');selector='2147483648'},
  @{text=$normal.Replace('[{"Index":1,"Id":"qol"}]','null')},
  @{text=$normal.Replace('"Categories"','"Unknown"')},
  @{text=$normal.Replace('"Index":1','"Index":null')},
  @{text=$normal.Replace('"Index":1','"Index":"bad"');selector='2147483648'},
  @{selector='2147483648'},@{selector="1`n"},
  @{text=$normal.Replace('"Id":"qol"','"Unused":"qol"')},
  @{text=$normal.Replace('"Id":"qol"','"Id":null')},
  @{text=$normal.Replace('"Id":"qol"','"Id":true')},
  @{text=$normal.Replace('"Id":"qol"','"Id":1')},
  @{text=$normal.Replace('"Id":"qol"','"Id":["A","B"]')},
  @{text=$normal.Replace('"Id":"qol"','"Id":"UNCLASSIFIED"');allow=$true},
  @{text=$normal.Replace('"Id":"qol"','"Id":"UNCLASSIFIED"')},
  @{text=$normal.Replace('[{"Index":1,"Id":"qol"}]','[{"Index":1,"Id":"qol"},{"Index":"bad","Id":"qol"}]')},
  @{text='{bad'},@{text='false'},@{text='null'},@{text=''},@{text='[/*x*/'+$normal+',]'},
  @{metadata='@{Mods=1}';text='{bad'}
 )
 $count=0
 foreach($case in $cases){
  [IO.File]::WriteAllText($metadataPath,$(if($case.ContainsKey('metadata')){$case.metadata}else{$metadata}))
  [IO.File]::WriteAllText($path,$(if($case.ContainsKey('text')){$case.text}else{$normal}),[Text.UTF8Encoding]::new($false))
  $before=(Get-FileHash -LiteralPath $path).Hash; $metadataBefore=(Get-FileHash -LiteralPath $metadataPath).Hash
  $selector=if($case.ContainsKey('selector')){$case.selector}else{'1'}
  $parameters=@{Project=[pscustomobject]@{Id='fixture';Root=$fixture};Selector=$selector;AllowUnclassified=[bool]$case.allow}
  foreach($stage in @('read','resolve')){
   $failure=$null
   try{$expected=if($stage-eq'read'){&$module {Read-ModpackCategoryCache}}else{&$module {param($parameters)Resolve-ModpackCategoryId @parameters} $parameters};$expectedJson=ConvertTo-Json -InputObject $expected -Depth 100 -Compress}catch{$failure=$_}
   $tokens=if($stage-eq'read'){@('--inspect-read-category-cache',$fixture)}else{@('--inspect-category-id',$fixture,'fixture',$fixture,$selector,'--now-utc',$clock,'--allow-unclassified',([bool]$case.allow).ToString())}
   $native=Invoke-NativeTestProcess $Executable $tokens
   if($native.Stderr){throw 'Unexpected category stderr'}
   if($failure){
    $actual=$native.Stdout|ConvertFrom-Json -DateKind String
    $id=$failure.Exception.Data['ModpackTools.ErrorId']
    if(!$id){
     if($native.Exit-ne1-or$actual.error.id-cne'ModpackTools.Metadata.InvalidCategoryCache'-or$actual.error.target-cne$path-or!$actual.error.details-or$actual.error.hint-cne'modpack category list'){throw "Category E20 mismatch $stage : $($native.Stdout)"}
    }else{
     $lines=@((Sentence $actual.error.message));if($actual.error.details){$lines+='Details: '+(Sentence $actual.error.details)};if($actual.error.hint){$lines+='Try: '+$actual.error.hint.Trim()}
     if($native.Exit-ne1-or$actual.error.id-cne$id-or$actual.error.target-cne$failure.Exception.Data['ModpackTools.TargetObject']-or$actual.error.category-cne([Management.Automation.ErrorCategory][int]$failure.Exception.Data['ModpackTools.ErrorCategory']).ToString()-or($id-ne'ModpackTools.Metadata.InvalidFile'-and($lines-join[Environment]::NewLine)-cne$failure.Exception.Message)){throw "Category domain mismatch $stage : $($native.Stdout)"}
    }
   }else{
    $actual=ConvertTo-Json -InputObject ($native.Stdout|ConvertFrom-Json -AsHashtable -DateKind String -NoEnumerate -Depth 100) -Compress -Depth 100
    $canonical=ConvertTo-Json -InputObject ($expectedJson|ConvertFrom-Json -AsHashtable -DateKind String -NoEnumerate -Depth 100) -Compress -Depth 100
    if($native.Exit-ne0-or$actual-cne$canonical){throw "Category full value mismatch $stage : $canonical / $($native.Stdout)"}
   }
   $count++
  }
  if((Get-FileHash -LiteralPath $path).Hash-cne$before-or(Get-FileHash -LiteralPath $metadataPath).Hash-cne$metadataBefore){throw 'Category reader changed fixture'}
  $exclusive=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);$exclusive.Dispose()
 }
 [IO.File]::WriteAllText($metadataPath,$metadata)
 [IO.File]::WriteAllText($path,'{bad')
 foreach($option in @('--max-cache-bytes','--max-cache-depth','--max-cache-rows','--max-cache-normalized-chars','--max-cache-token-chars','--max-cache-nodes')){
  $invalid=Invoke-NativeTestProcess $Executable @('--inspect-read-category-cache',$fixture,$option,'0')
  $diagnostic=$invalid.Stdout|ConvertFrom-Json -DateKind String
  if($invalid.Exit-ne1-or$invalid.Stderr-or$diagnostic.error.id-cne'ModpackTools.Metadata.InvalidCategoryCacheReadPolicy'){throw "Invalid category policy $option"}
  $literal=Invoke-NativeTestProcess $Executable @('--inspect-category-id',$fixture,'fixture',$fixture,'QOL',$option,'0')
  if($literal.Exit-ne0-or$literal.Stderr-or($literal.Stdout|ConvertFrom-Json)-cne'qol'){throw "Literal inspected unused cache policy $option"}
  [IO.File]::WriteAllText($metadataPath,'broken metadata')
  $reserved=Invoke-NativeTestProcess $Executable @('--inspect-category-id',$fixture,'fixture',$fixture,'UNCLASSIFIED','--allow-unclassified','true',$option,'0')
  if($reserved.Exit-ne0-or$reserved.Stderr-or($reserved.Stdout|ConvertFrom-Json)-cne'unclassified'){throw "Reserved category inspected metadata or cache policy $option"}
  [IO.File]::WriteAllText($metadataPath,$metadata)
  $count+=3
 }
 foreach($limit in @(3,2)){
  [IO.File]::WriteAllText($path,'"a"',[Text.UTF8Encoding]::new($false))
  $bounded=Invoke-NativeTestProcess $Executable @('--inspect-read-category-cache',$fixture,'--max-cache-bytes',([string]$limit))
  if($bounded.Stderr-or($limit-eq3-and($bounded.Exit-ne0-or($bounded.Stdout|ConvertFrom-Json)-cne'a'))-or($limit-eq2-and($bounded.Exit-ne1-or($bounded.Stdout|ConvertFrom-Json).error.id-cne'ModpackTools.Metadata.CategoryCacheReadLimitExceeded'))){throw 'Category actual-byte boundary changed'}
  $count++
 }
 foreach($rows in @(1,2)){
  [IO.File]::WriteAllText($path,$(if($rows-eq1){'{"Categories":[{}]}'}else{'{"Categories":[{},{}]}'}))
  $bounded=Invoke-NativeTestProcess $Executable @('--inspect-read-category-cache',$fixture,'--max-cache-rows','1')
  if($bounded.Stderr-or($rows-eq1-and$bounded.Exit-ne0)-or($rows-eq2-and($bounded.Exit-ne1-or($bounded.Stdout|ConvertFrom-Json).error.id-cne'ModpackTools.Metadata.CategoryCacheReadLimitExceeded'))){throw 'Category row boundary changed'}
  $count++
 }
 [IO.File]::WriteAllText($metadataPath,'broken metadata')
 $empty=Invoke-NativeTestProcess $Executable @('--inspect-category-id',$fixture,'fixture',$fixture,'')
 if($empty.Exit-ne2-or$empty.Stderr-or($empty.Stdout|ConvertFrom-Json).error.id-cne'Core.InputReadFailed'){throw 'Empty category input did not fail before IO'}
 $count++
 "Native category cache: $count full read/resolution, policy/bypass, byte/row boundaries and grammar controls, existing errors/E20 priorities, no descriptors/writes and released handles passed"
}finally{
 [Environment]::SetEnvironmentVariable('MODPACKTOOLS_CONFIG_HOME',$oldConfig)
 Remove-Module ModpackTools -ErrorAction SilentlyContinue
 $resolved=[IO.Path]::GetFullPath($fixture)
 $temporaryRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
 if(!$resolved.StartsWith($temporaryRoot,[StringComparison]::OrdinalIgnoreCase)-or[IO.Path]::GetFileName($resolved)-notlike'modpacktools-category-cache-*'){throw 'Unsafe fixture cleanup'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
