param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$LegacyRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-categories-'+[guid]::NewGuid().ToString('N'))
try {
 [void][IO.Directory]::CreateDirectory((Join-Path $fixture '.modpack'))
 [IO.File]::WriteAllText((Join-Path $fixture '.modpack/project.psd1'),"@{SchemaVersion=1;Id='fixture'}")
 [IO.File]::WriteAllText((Join-Path $fixture 'pack.toml'),'[index]')
 $path=Join-Path $fixture '.modpack/metadata.psd1';$runner=Join-Path $fixture 'legacy.ps1'
 [IO.File]::WriteAllText($runner,@'
param($ModulePath,$RootPath)
Import-Module $ModulePath -Force
& (Get-Module ModpackTools) {
 param($root)
 try {
  $metadata=Get-ModpackMetadata -Project ([pscustomobject]@{Root=$root})
  $items=@(foreach($key in $metadata.Categories.Keys){$value=$metadata.Categories[$key];[pscustomobject][ordered]@{key=[string]$key;name=$(if($value.ContainsKey('Name')){[string]$value.Name}else{([string]$key).ToUpperInvariant()});order=$(if($value.ContainsKey('Order')){[int]$value.Order}else{1000})}})|Sort-Object order,name
  ConvertTo-Json -InputObject @($items) -Depth 20 -Compress
 }catch{ConvertTo-Json -InputObject @{failure=$true;message=$_.Exception.Message} -Compress}
} $RootPath
'@)
 $success=0;$failure=0
 foreach($definition in @('@{}','@{Name=$null;Order=$null}',"@{Name='Display';Order=-5}","@{Name=@('A','B');Order='1.5'}","@{Name=@{x=1};Order=`$true}","@(@{Name='Wrapped';Order=1})","@{Order='0x10'}",'@{Order=2.5}','@{Order=3.5}','@{Order=2147483647}','@{Order=-2147483648}','$null','$false','@()','@{Order=@(1)}','@{Order=2147483648}',"@{Order='bad'}","@(@{Name='A';Order=1},@{Name='B';Order=2})",'@(@{Order=@(1)})')) {
  [IO.File]::WriteAllText($path,"@{Categories=@{qol=$definition}}")
  $old=Invoke-NativeTestProcess (Get-Command pwsh).Source @('-NoProfile','-File',$runner,'-ModulePath',(Join-Path $LegacyRoot 'ModpackTools.psd1'),'-RootPath',$fixture)
  $new=Invoke-NativeTestProcess $Executable @('--inspect-categories',$fixture)
  if($old.Exit -ne 0 -or $new.Stderr -cne ''){throw 'Category transport failed'}
  $b=$old.Stdout|ConvertFrom-Json;$n=$new.Stdout|ConvertFrom-Json
  if($b.failure){if($new.Exit -ne 1 -or $n.error.id -cne 'ModpackTools.Metadata.InvalidCategoryDefinition' -or $n.error.category -cne 'InvalidData' -or $n.error.target -cne 'qol'){throw "Category E11 mismatch $definition $($new.Stdout)"};$failure++}
  else {if($new.Exit -ne 0 -or (ConvertTo-Json -InputObject @($b) -Compress) -cne (ConvertTo-Json -InputObject @($n) -Compress)){throw "Category mismatch $definition $($old.Stdout) $($new.Stdout)"};$success++}
 }
 [IO.File]::WriteAllText($path,"@{Categories=@{z=@{Name='Zulu';Order=1};a=@{Name='Alpha';Order=1};b=@{Name='Before';Order=0}}}")
 $old=Invoke-NativeTestProcess (Get-Command pwsh).Source @('-NoProfile','-File',$runner,'-ModulePath',(Join-Path $LegacyRoot 'ModpackTools.psd1'),'-RootPath',$fixture)
 $new=Invoke-NativeTestProcess $Executable @('--inspect-categories',$fixture)
 if($old.Exit -ne 0 -or $new.Exit -ne 0 -or $new.Stderr -cne '' -or (($old.Stdout|ConvertFrom-Json)|ConvertTo-Json -Compress) -cne (($new.Stdout|ConvertFrom-Json)|ConvertTo-Json -Compress)){throw 'Multi-category baseline sorting mismatch'}
 $success++
 [IO.File]::WriteAllText($path,"@{Categories=@{z=@{Name='same';Order=1};a=@{Name='same';Order=1};b=@{Name='Before';Order=0}}}")
 $new=Invoke-NativeTestProcess $Executable @('--inspect-categories',$fixture)
 if($new.Exit -ne 0 -or (($new.Stdout|ConvertFrom-Json).key -join ',') -cne 'b,a,z'){throw 'Deterministic category ordering failed'}
 [IO.File]::WriteAllText($path,'@{}')
 $new=Invoke-NativeTestProcess $Executable @('--inspect-categories',$fixture)
 if($new.Exit -ne 0 -or $new.Stdout.Trim() -cne '[]'){throw 'Empty categories failed'}
 "Native categories: $success differential successes, $failure E11 failures and ordering/empty controls passed"
}finally{
 $resolved=[IO.Path]::GetFullPath($fixture)
 if(-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-categories-*'){throw 'Unsafe fixture cleanup target'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
