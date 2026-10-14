param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$LegacyRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-classification-'+[guid]::NewGuid().ToString('N'))
try {
    [void][IO.Directory]::CreateDirectory((Join-Path $fixture '.modpack'))
    [void][IO.Directory]::CreateDirectory((Join-Path $fixture 'mods'))
    [IO.File]::WriteAllText((Join-Path $fixture '.modpack/project.psd1'),"@{SchemaVersion=1;Id='fixture'}")
    [IO.File]::WriteAllText((Join-Path $fixture 'pack.toml'),'[index]')
    foreach($file in @('one','two')){[IO.File]::WriteAllText((Join-Path $fixture "mods/$file.pw.toml"),"name = `"Original`"`nfilename = `"$file.jar`"`npin = true`n[update.modrinth]`nmod-id = `"one`"`nversion = `"v1`"`n")}
    [IO.File]::WriteAllText((Join-Path $fixture 'mods/local.jar'),'unreadable optional JAR')
    $path=Join-Path $fixture '.modpack/metadata.psd1'; $runner=Join-Path $fixture 'legacy.ps1'
    [IO.File]::WriteAllText($runner,@'
param($ModulePath,$RootPath)
Import-Module $ModulePath -Force
& (Get-Module ModpackTools) {
 param($root)
 try {
  $project=[pscustomobject]@{Root=$root};$metadata=Get-ModpackMetadata $project
  $managed=@(Get-PackwizItems -Project $project -Directory mods -Kind mod)
  $items=@($managed;Get-LocalModItems -Project $project -KnownItems $managed)
  $items=@(Apply-ModMetadata -Mods $items -Metadata $metadata)
  $rows=@(foreach($item in $items){
   $row=[ordered]@{id=$item.Id;kind=$item.Kind;name=$item.Name;filename=$item.Filename;side=$item.Side;source=$item.Source;metadata_path=$item.MetadataPath;category=$item.Category;invalid_category=$item.InvalidCategory}
   if($item.Source -eq 'packwiz'){$row.version_id=$item.VersionId;$row.pinned=$item.Pinned};$row
  })
  ConvertTo-Json -InputObject $rows -Depth 20 -Compress
 }catch{ConvertTo-Json -InputObject @{failure=$true;id=$_.FullyQualifiedErrorId;message=$_.Exception.Message} -Compress}
} $RootPath
'@)
    $success=0;$failure=0
    foreach($entry in @('@{Name=@()}','@(@{Name=@()})','@(@{Name=@()},@{Name=@()})','@(@{Name=@()},@{Name=$null})','@{}',"@{Name='Display';Side='CLIENT';Category='QoL'}",'@{Name=$null;Side=$null;Category=$null}',"@{Name=@{x=1};Side=@('CLIENT','SERVER');Category='absent'}",'@{Name=1.234567891234567;Side=1.0000000000000002;Category=0}',"@{Category=''}",'$null','$false','0',"''",'@()','@($null)','@($false)',"@(@{Name='Wrapped';Side='client';Category='QOL'})",'@(@{})',"@(@{Name='A';Side='client';Category='QoL'},@{Name='B';Side='server';Category='QoL'})","@(@{Name=`$null;Side='client';Category=`$null},@{Name='A';Side=`$null;Category='QoL'})","'bad'",'@($null,$null)','@(@{},@{})',"@(@{Name='A'},@{})")) {
        [IO.File]::WriteAllText($path,"@{Categories=@{qol=@{}};Mods=@{'modrinth:one'=$entry;unused='malformed'}}")
        $old=Invoke-NativeTestProcess (Get-Command pwsh).Source @('-NoProfile','-File',$runner,'-ModulePath',(Join-Path $LegacyRoot 'ModpackTools.psd1'),'-RootPath',$fixture)
        $new=Invoke-NativeTestProcess $Executable @('--inspect-classified-mods',$fixture)
        if($old.Exit -ne 0 -or $new.Stderr -cne ''){throw 'Classification transport failed'}
        $oldValue=ConvertFrom-Json -InputObject $old.Stdout -NoEnumerate
        $newValue=ConvertFrom-Json -InputObject $new.Stdout -NoEnumerate
        if($oldValue -isnot [array] -and $oldValue.failure -eq $true){
            if($new.Exit -ne 1 -or $newValue.error.id -cne 'ModpackTools.Metadata.InvalidOverride' -or $newValue.error.category -cne 'InvalidData' -or $newValue.error.target -cne 'modrinth:one' -or -not $newValue.error.hint){throw "Invalid override escaped E07: $entry $($old.Stdout) $($new.Stdout)"};$failure++
        }else{
            if($new.Exit -ne 0 -or (ConvertTo-Json -InputObject $oldValue -Depth 20 -Compress) -cne (ConvertTo-Json -InputObject $newValue -Depth 20 -Compress)){throw "Classification mismatch: $entry $($old.Stdout) $($new.Stdout)"};$success++
        }
    }
    "Native mod classification: $success differential successes and $failure improved override failures passed"
} finally {
    $resolved=[IO.Path]::GetFullPath($fixture)
    if(-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-classification-*'){throw 'Unsafe fixture cleanup target'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
