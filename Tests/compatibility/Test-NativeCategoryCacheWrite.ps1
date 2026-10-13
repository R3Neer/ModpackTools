param([Parameter(Mandatory)][string]$Executable,[string]$LegacyRoot='D:\Tools\ModpackTools')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-category-cache-write-'+[guid]::NewGuid().ToString('N'))
$oldConfig=[Environment]::GetEnvironmentVariable('MODPACKTOOLS_CONFIG_HOME')
$stamp='2026-10-14T10:09:10.1234567+02:00'
try {
 $pack=Join-Path $fixture 'pack';$config=Join-Path $fixture 'native';$legacyConfig=Join-Path $fixture 'legacy'
 foreach($directory in @((Join-Path $pack '.modpack'),$config,$legacyConfig)){[void][IO.Directory]::CreateDirectory($directory)}
 [IO.File]::WriteAllText((Join-Path $pack '.modpack/project.psd1'),"@{SchemaVersion=1;Id='fixture'}")
 [IO.File]::WriteAllText((Join-Path $pack 'pack.toml'),"name='Fixture'`nversion='1'`n[index]`nfile='index.toml'`n")
 $metadata=Join-Path $pack '.modpack/metadata.psd1';$path=Join-Path $config 'last-categories.json'
 Import-Module (Join-Path $LegacyRoot 'ModpackTools.psd1') -Force
 $module=Get-Module ModpackTools
 &$module {param($stamp)
  $writer=(Get-Command Write-ModpackCategoryCache).Definition
  Set-Item Function:script:Write-ModpackCategoryCache ([scriptblock]::Create($writer.Replace("[datetime]::UtcNow.ToString('o')","([datetimeoffset]::Parse('$stamp')).UtcDateTime.ToString('o')")))
  $timestamp=(Get-Command Test-MpCacheTimestamp).Definition
  Set-Item Function:script:Test-MpCacheTimestamp ([scriptblock]::Create($timestamp.Replace('[datetimeoffset]::UtcNow',"([datetimeoffset]::Parse('$stamp'))")))
 } $stamp
 $project=&$module {param($root)Read-ModpackProject $root} $pack
 $cases=@(
  @{text='@{}'},
  @{text="@{Categories=@{q=@{Name='中文 🎮';Order=1};z=@{Order=2}}}"},
  @{text="@{Categories=@{unclassified=@{Name='Custom'}}}"},
  @{text="@{Categories=@{q=@{Name=@('A','B')}}}"},
  @{text="@{Categories=@{q=@{Name='Replacement'}}}"},
  @{text='@{}';empty=$true}
 )
 $calls=0
 foreach($case in $cases){
  [IO.File]::WriteAllText($metadata,$case.text)
  $before=(Get-FileHash -LiteralPath $metadata).Hash
  $view=&$module {param($project)Get-ModpackCategoryView $project} $project
  if($case.empty){$view.Categories=@();$view.DefinedCategoryCount=0}
  $env:MODPACKTOOLS_CONFIG_HOME=$legacyConfig
  &$module {param($view)Write-ModpackCategoryCache $view} $view
  $expected=[IO.File]::ReadAllText((Join-Path $legacyConfig 'last-categories.json'))|ConvertFrom-Json -AsHashtable -DateKind String
  $tokens=@('--inspect-write-category-cache',$pack,$config,'--created-utc',$stamp)
  if($case.empty){$tokens+=@('--empty-categories','true')}
  $native=Invoke-NativeTestProcess $Executable $tokens
  $calls++
  if($native.Exit-ne0-or$native.Stderr){throw "Category cache write failed $($native.Stdout)"}
  $result=$native.Stdout|ConvertFrom-Json
  $bytes=[IO.File]::ReadAllBytes($path);$actual=[Text.Encoding]::UTF8.GetString($bytes)|ConvertFrom-Json -AsHashtable -DateKind String
  if((ConvertTo-Json -InputObject $actual -Depth 30 -Compress)-cne(ConvertTo-Json -InputObject $expected -Depth 30 -Compress)-or$result.path-cne$path-or$result.count-ne$view.Categories.Count-or($bytes.Length-ge3-and$bytes[0]-eq239-and$bytes[1]-eq187-and$bytes[2]-eq191)){throw 'Full category cache JSON, count/path or encoding changed'}
  $env:MODPACKTOOLS_CONFIG_HOME=$config
  $read=&$module {Read-ModpackCategoryCache}
  if(@($read.Categories).Count-ne$view.Categories.Count){throw 'Legacy reader rejected native category cache'}
  foreach($row in $view.Categories){
   $resolved=&$module {param($project,$number)Resolve-ModpackCategoryId -Project $project -Selector ([string]$number) -AllowUnclassified} $project $row.ReferenceNumber
   if($resolved-cne$row.Id){throw 'Legacy resolver changed a persisted category reference'}
   $calls++
  }
  $exact=Invoke-NativeTestProcess $Executable ($tokens+@('--max-cache-bytes',([string]$bytes.Length)))
  if($exact.Exit-ne0-or$exact.Stderr-or[Convert]::ToBase64String([IO.File]::ReadAllBytes($path))-cne[Convert]::ToBase64String($bytes)){throw 'Category cache exact byte quota changed output'}
  $over=Invoke-NativeTestProcess $Executable ($tokens+@('--max-cache-bytes',([string]($bytes.Length-1))))
  if($over.Exit-ne1-or$over.Stderr-or($over.Stdout|ConvertFrom-Json).error.id-cne'ModpackTools.Metadata.CategoryCacheWriteLimitExceeded'-or[Convert]::ToBase64String([IO.File]::ReadAllBytes($path))-cne[Convert]::ToBase64String($bytes)){throw 'Category byte quota changed old bytes'}
  $calls+=2
  if((Get-FileHash -LiteralPath $metadata).Hash-cne$before){throw 'Category writer changed source metadata'}
 }
 $bytes=[IO.File]::ReadAllBytes($path)
 $sentinel=Join-Path $config '.last-categories.json.unrelated.tmp';[IO.File]::WriteAllText($sentinel,'preserve')
 $locked=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
 try{$native=Invoke-NativeTestProcess $Executable @('--inspect-write-category-cache',$pack,$config,'--created-utc',$stamp);if($native.Exit-ne1-or$native.Stderr-or($native.Stdout|ConvertFrom-Json).error.id-cne'ModpackTools.Metadata.CategoryCacheWriteFailed'){throw 'Locked category cache was not rejected'}}finally{$locked.Dispose()}
 $calls++
 if([Convert]::ToBase64String([IO.File]::ReadAllBytes($path))-cne[Convert]::ToBase64String($bytes)){throw 'Locked category cache bytes changed'}
 $left=@(Get-ChildItem -LiteralPath $config -Filter '.last-categories.json.*.tmp' -Force -File)
 if($left.Count-ne1-or$left[0].FullName-cne$sentinel-or[IO.File]::ReadAllText($sentinel)-cne'preserve'){throw 'Category writer leaked a temporary or changed unrelated files'}
 $absent=Join-Path $fixture 'absent'
 $invalid=Invoke-NativeTestProcess $Executable @('--inspect-write-category-cache',$pack,$absent,'--max-cache-bytes','0')
 if($invalid.Exit-ne1-or$invalid.Stderr-or($invalid.Stdout|ConvertFrom-Json).error.id-cne'ModpackTools.Metadata.InvalidCategoryCacheWritePolicy'-or(Test-Path -LiteralPath $absent)){throw 'Invalid category writer policy created effects'}
 $calls++
 $exclusive=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);$exclusive.Dispose()
 "Native category cache writer: $calls file/resolver/quota/locked/policy controls, legacy full data, UTC/Unicode/duplicates/empty/replacement, cleanup and source integrity passed"
} finally {
 [Environment]::SetEnvironmentVariable('MODPACKTOOLS_CONFIG_HOME',$oldConfig)
 Remove-Module ModpackTools -ErrorAction SilentlyContinue
 $resolved=[IO.Path]::GetFullPath($fixture)
 $temporaryRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
 if(!$resolved.StartsWith($temporaryRoot,[StringComparison]::OrdinalIgnoreCase)-or[IO.Path]::GetFileName($resolved)-notlike'modpacktools-category-cache-write-*'){throw 'Unsafe category cache writer cleanup'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
