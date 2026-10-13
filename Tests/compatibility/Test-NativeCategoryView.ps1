param([Parameter(Mandatory)][string]$Executable,[string]$LegacyRoot='D:\Tools\ModpackTools')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-category-view-'+[guid]::NewGuid().ToString('N'))
$originalCulture=[Globalization.CultureInfo]::CurrentCulture
try {
 [void][IO.Directory]::CreateDirectory((Join-Path $fixture '.modpack'))
 [void][IO.Directory]::CreateDirectory((Join-Path $fixture 'mods'))
 [IO.File]::WriteAllText((Join-Path $fixture '.modpack/project.psd1'),"@{SchemaVersion=1;Id='fixture'}")
 [IO.File]::WriteAllText((Join-Path $fixture 'pack.toml'),"name = 'Fixture'`nversion = '1'`n[index]`nfile = 'index.toml'`n[versions]`nminecraft = '1.21'`nfabric = '0.16'`n")
 [IO.File]::WriteAllText((Join-Path $fixture 'mods/a.jar'),'unreadable local archive')
 [IO.File]::WriteAllText((Join-Path $fixture 'mods/b.jar'),'unreadable local archive')
 $path=Join-Path $fixture '.modpack/metadata.psd1'
 $broken=Join-Path $fixture 'mods/bad.pw.toml'
 Import-Module (Join-Path $LegacyRoot 'ModpackTools.psd1') -Force
 $module=Get-Module ModpackTools
 $project=&$module {param($root)Read-ModpackProject $root} $fixture
 $cases=@(
  @{text='@{}'},
  @{text="@{Categories=@{q=@{}};Mods=@{orphan=@{Category='Q'}}}"},
  @{text="@{Categories=@{unclassified=@{Name='Custom'}};Mods=@{orphan=@{Category='unclassified'}}}"},
  @{text="@{Categories=@{z=@{Name='Same';Order=1};'ä'=@{Name='Same';Order=1}}}";culture='en-US'},
  @{text="@{Categories=@{z=@{Name='Same';Order=1};'ä'=@{Name='Same';Order=1}}}";culture='sv-SE'},
  @{text="@{Categories=@{q=@{Name=@('A','B');Order=`$null}}}"},
  @{text="@{Categories=@{q=@{Name=`$null;Order=2.5}}}"},
  @{text="@{Categories=@{q=@{Name=@{x=1};Order='0x1'}}}"},
  @{text="@{Categories=@{q=@(@{Name='Q';Order=1})}}"},
  @{text="@{Categories=@{q=@{}};Mods=@{orphan=@(@{Category='q'})}}"},
  @{text="@{Categories=@{'q q'=@{}};Mods=@{orphan=@(@{Category='q'},@{Category='q'})}}"},
  @{text="@{Categories=@{q=@{}};Mods=@{orphan=@{Category=@()}}}"},
  @{text="@{Categories=@{'é'=@{}};Mods=@{orphan=@{Category='é'}}}"},
  @{text='@{Categories=@{};Mods=@{bad=$false}}'},
  @{text='@{Categories=@{q=@{}};Mods=@{bad=$false}}';raw='Metadata.InvalidCategoryAssignment'},
  @{text='@{Categories=@{q=$false};Mods=@{bad=$null}}';raw='Metadata.InvalidCategoryAssignment'},
  @{text='@{Categories=@{q=@{}};Mods=@{bad=@(@{Category=@()})}}';raw='Metadata.InvalidCategoryAssignment'},
  @{text='@{Categories=@{q=$false}}';raw='Metadata.InvalidCategoryDefinition'},
  @{text="@{Categories=@{q=@{Order='bad'}}}";raw='Metadata.InvalidCategoryDefinition'},
  @{text='@{Categories=@{q=@{Order=2147483648}}}';raw='Metadata.InvalidCategoryDefinition'},
  @{text='@{Categories=@{q=@{}};Mods=@{bad=$false}}';raw='Metadata.InvalidCategoryAssignment';badInventory=$true},
  @{text="@{Categories=@{q=@{Order='bad'}}}";raw='Metadata.InvalidCategoryDefinition';badInventory=$true},
  @{text='@{Categories=@{q=@{}}}';badInventory=$true;nativeOnly='ModpackTools.Content.InvalidMetadata'}
 )
 $count=0
 foreach($case in $cases){
  $culture=if($case.culture){$case.culture}else{'en-US'}
  [Globalization.CultureInfo]::CurrentCulture=[Globalization.CultureInfo]::GetCultureInfo($culture)
  [IO.File]::WriteAllText($path,$case.text)
  if($case.badInventory){[IO.File]::WriteAllText($broken,'invalid = [')}elseif(Test-Path -LiteralPath $broken){Remove-Item -LiteralPath $broken}
  $before=(Get-FileHash -LiteralPath $path).Hash
  $failure=$null
  try{$expected=&$module {param($project)Get-ModpackCategoryView -Project $project} $project}catch{$failure=$_}
  $native=Invoke-NativeTestProcess $Executable @('--inspect-category-view',$fixture,'--culture',$culture)
  if($native.Stderr){throw 'Category view emitted stderr'}
  $actual=$native.Stdout|ConvertFrom-Json -AsHashtable -DateKind String -Depth 100
  if($case.nativeOnly){
   # E02 already specifies strict managed TOML parsing; legacy overlooks this
   # unrelated malformed line. This control checks the later physical stage.
   if($native.Exit-ne1-or$actual.error.id-cne$case.nativeOnly-or$actual.error.target-cne$broken-or!$actual.error.details){throw 'Physical category-view stage lost the established E02 diagnostic'}
  }elseif($failure){
   $domain=$failure.Exception.Data['ModpackTools.ErrorId']
   $id=if($domain){$domain}else{'ModpackTools.'+$case.raw}
   if(!$id-or$native.Exit-ne1-or$actual.error.id-cne$id){throw "Category view failure mismatch: $($native.Stdout)"}
   if(!$domain-and$case.raw-eq'Metadata.InvalidCategoryAssignment'-and($actual.error.target-cne'bad'-or!$actual.error.details)){throw 'Missing contextual category assignment diagnostic'}
   if($domain-and$actual.error.target-cne$failure.Exception.Data['ModpackTools.TargetObject']){throw 'Changed existing category view failure target'}
  }else{
   if($native.Exit-ne0){throw "Category view failed: $($native.Stdout)"}
   $expectedProject=@{};foreach($key in @('Id','Root','DisplayName','DisplayVersion','OutputName','MinecraftVersion','Loader','LoaderVersion','IndexFile')){$expectedProject[$key]=$expected.Project.$key}
   $expectedRows=ConvertTo-Json -InputObject @($expected.Categories) -Depth 100 -Compress
   $actualRows=ConvertTo-Json -InputObject @($actual.Categories) -Depth 100 -Compress
   if($expectedRows-cne$actualRows-or$actual.DefinedCategoryCount-ne$expected.DefinedCategoryCount){throw "Category view rows differ: $expectedRows / $actualRows"}
   foreach($key in $expectedProject.Keys){if($expectedProject[$key]-cne$actual.Project[$key]){throw "Category view project field changed: $key"}}
  }
  if((Get-FileHash -LiteralPath $path).Hash-cne$before){throw 'Category view changed metadata'}
  $exclusive=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);$exclusive.Dispose()
  $count++
 }
 foreach($tokens in @(@('--inspect-category-view'),@('--inspect-category-view',$fixture,'--unknown','x'),@('--inspect-category-view',$fixture,'--culture','en-US','--culture','sv-SE'))){
  $native=Invoke-NativeTestProcess $Executable $tokens
  if($native.Exit-ne2-or$native.Stderr-or($native.Stdout|ConvertFrom-Json).error.id-cne'Core.InputReadFailed'){throw 'Category view grammar control failed'}
  $count++
 }
 "Native category view: $count differential/grammar controls, editorial versus physical counts, cultural ordering and failure priorities passed"
} finally {
 [Globalization.CultureInfo]::CurrentCulture=$originalCulture
 Remove-Module ModpackTools -ErrorAction SilentlyContinue
 $resolved=[IO.Path]::GetFullPath($fixture)
 $temporaryRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
 if(!$resolved.StartsWith($temporaryRoot,[StringComparison]::OrdinalIgnoreCase)-or[IO.Path]::GetFileName($resolved)-notlike'modpacktools-category-view-*'){throw 'Unsafe category view fixture cleanup'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
