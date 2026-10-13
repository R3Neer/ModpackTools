param([Parameter(Mandatory)][string]$Executable,[string]$LegacyRoot='D:\Tools\ModpackTools')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-inventory-'+[guid]::NewGuid().ToString('N'))
try {
    foreach($directory in @('.modpack','mods','resourcepacks','shaderpacks','config')){[void][IO.Directory]::CreateDirectory((Join-Path $fixture $directory))}
    [IO.File]::WriteAllText((Join-Path $fixture '.modpack/project.psd1'),"@{SchemaVersion=1;Id='fixture';DisplayName='Display'}")
    [IO.File]::WriteAllText((Join-Path $fixture 'pack.toml'),"name = `"Technical`"`n[index]`nfile = `"index.toml`"`n[versions]`nminecraft = `"1.21.1`"`nfabric = `"0.16`"`n")
    $runner=Join-Path $fixture 'legacy.ps1';$metadata=Join-Path $fixture '.modpack/metadata.psd1';$order=Join-Path $fixture 'config/defaultoptions-common.toml'
    [IO.File]::WriteAllText($runner,@'
param($ModulePath,$RootPath)
Import-Module $ModulePath -Force
& (Get-Module ModpackTools) {
 param($root)
 function Physical($items){return ,@(foreach($item in $items){$row=[ordered]@{id=$item.Id;kind=$item.Kind;name=$item.Name;filename=$item.Filename;side=$item.Side;source=$item.Source;metadata_path=$item.MetadataPath;category=$item.Category};if($item.Kind -eq 'mod'){$row.invalid_category=$item.InvalidCategory};if($item.Source -eq 'packwiz'){$row.version_id=$item.VersionId;$row.pinned=$item.Pinned};$row})}
 try {
  $project=Read-ModpackProject $root;$snapshot=Get-ModpackInventory -Project $project
  $p=ConvertTo-MpMachineProject $project;$p.index_file=$project.IndexFile;$p.index_path=$project.IndexPath
  $active=@(foreach($item in $snapshot.ActiveResources){[ordered]@{id=$item.Id;kind=$item.Kind;name=$item.Name;filename=$item.Filename;source=$item.Source;enabled=$item.Enabled;priority=$item.Priority}})
  ConvertTo-Json -InputObject ([ordered]@{project=$p;metadata=$snapshot.Metadata;mods=(Physical $snapshot.Mods);resources=(Physical $snapshot.ResourcePacks);active=$active;inactive=(Physical $snapshot.InactiveResources);shaders=(Physical $snapshot.Shaders)}) -Depth 100 -Compress
 }catch{ConvertTo-Json -InputObject @{failure=$true;id=$_.Exception.Data['ModpackTools.ErrorId'];category=[string][Management.Automation.ErrorCategory]$_.Exception.Data['ModpackTools.ErrorCategory'];target=$_.Exception.Data['ModpackTools.TargetObject']} -Compress}
} $RootPath
'@)
    function Canonical($value){
        if($value -is [Collections.IDictionary]){$result=[ordered]@{};foreach($key in @($value.Keys|Sort-Object)){$result[$key]=Canonical $value[$key]};return $result}
        if($value -is [array]){$items=[Collections.Generic.List[object]]::new();foreach($child in $value){$items.Add((Canonical $child))};return ,$items.ToArray()}
        return $value
    }
    function Invoke-Inventory([string[]]$Options=@()){return Invoke-NativeTestProcess $Executable (@('--inspect-inventory',$fixture)+$Options)}
    function Assert-InventoryPair {
        $old=Invoke-NativeTestProcess (Get-Command pwsh).Source @('-NoProfile','-File',$runner,'-ModulePath',(Join-Path $LegacyRoot 'ModpackTools.psd1'),'-RootPath',$fixture)
        $new=Invoke-Inventory
        if($old.Exit -ne 0 -or $new.Stderr -cne ''){throw 'Inventory transport failed'}
        $oldValue=ConvertFrom-Json -InputObject $old.Stdout -AsHashtable
        $newValue=ConvertFrom-Json -InputObject $new.Stdout -AsHashtable
        if($oldValue.ContainsKey('failure')){
            if($new.Exit -ne 1 -or $newValue.error.id -cne $oldValue.id -or $newValue.error.category -cne $oldValue.category -or $newValue.error.target -cne $oldValue.target){throw "Inventory priority mismatch: $($old.Stdout) $($new.Stdout)"}
        }else{
            [void]$newValue.Remove('local_mod_observations')
            if($new.Exit -ne 0 -or (ConvertTo-Json -InputObject (Canonical $oldValue) -Depth 100 -Compress) -cne (ConvertTo-Json -InputObject (Canonical $newValue) -Depth 100 -Compress)){throw "Inventory mismatch: $($old.Stdout) $($new.Stdout)"}
        }
    }
    Assert-InventoryPair
    $managed=@()
    foreach($kind in @('mods','resourcepacks','shaderpacks')){
        $file=Join-Path $fixture "$kind/item.pw.toml";$managed+=$file
        [IO.File]::WriteAllText($file,"name = `"Managed $kind`"`nfilename = `"managed-$kind.zip`"`npin = true`n")
    }
    foreach($kind in @('resourcepacks','shaderpacks')){[IO.File]::WriteAllText((Join-Path $fixture "$kind/local.zip"),'optional display does not open ZIP')}
    $archive=[IO.Compression.ZipFile]::Open((Join-Path $fixture 'mods/local.jar'),[IO.Compression.ZipArchiveMode]::Create)
    try{$writer=[IO.StreamWriter]::new($archive.CreateEntry('fabric.mod.json').Open());try{$writer.Write('{"name":"Archive Name","environment":"client"}')}finally{$writer.Dispose()}}finally{$archive.Dispose()}
    [IO.File]::WriteAllText($order,'defaultResourcePacks=["vanilla","file/local.zip","file/missing.zip"]')
    [IO.File]::WriteAllText($metadata,"@{Categories=@{qol=@{}};Mods=@{'local:mods/local.jar'=@{Name='Editorial Name';Category='QOL'}};Unknown=@('á',`$null,@{X=1})}")
    Assert-InventoryPair
    $observed=Invoke-Inventory;$value=$observed.Stdout|ConvertFrom-Json
    if($value.local_mod_observations[0].original_name -cne 'Archive Name' -or $value.local_mod_observations[0].status -cne 'read' -or $value.mods[0].name -cne 'Editorial Name'){throw 'Original JAR identity was lost after mod classification/sort'}
    [long]$bytes=0;foreach($file in $managed){$bytes+=(Get-Item -LiteralPath $file).Length}
    foreach($pair in @(@('--max-managed-files','3','2'),@('--max-managed-directories','3','2'),@('--max-managed-bytes',[string]$bytes,[string]($bytes-1)),@('--max-zip-files','2','1'))){
        $exact=Invoke-Inventory @($pair[0],$pair[1]);$over=Invoke-Inventory @($pair[0],$pair[2])
        if($exact.Exit -ne 0 -or $over.Exit -ne 1 -or $over.Stderr -cne '' -or ($over.Stdout|ConvertFrom-Json).error.id -cne 'ModpackTools.Content.ReadLimitExceeded'){throw "Shared inventory quota failed: $($pair[0])"}
    }
    [IO.File]::WriteAllText($metadata,'@{Categories=$null}')
    [IO.File]::WriteAllText($managed[1],'name=[')
    Assert-InventoryPair
    [IO.File]::WriteAllText($metadata,"@{Mods=@{'packwiz:mods/item.pw.toml'='bad'}}")
    $classified=Invoke-Inventory;$errorValue=$classified.Stdout|ConvertFrom-Json
    if($classified.Exit -ne 1 -or $errorValue.error.id -cne 'ModpackTools.Metadata.InvalidOverride'){throw 'Resource decode preempted mod classification failure'}
    [IO.File]::WriteAllText($metadata,'@{}')
    [IO.File]::WriteAllText($managed[1],"name = `"Resource`"`n")
    [IO.File]::WriteAllText($managed[2],"name = `"Shader`"`n")
    [IO.File]::WriteAllText($order,'defaultResourcePacks=["open"')
    [IO.File]::WriteAllText($metadata,"@{ResourcePacks=@{'vanilla'=`$null}}")
    Assert-InventoryPair
    foreach($option in @('--max-managed-files','--max-local-files','--max-zip-files','--max-order-items','--max-entry-bytes')){
        $invalid=Invoke-Inventory @($option,'0')
        if($invalid.Exit -ne 2 -or $invalid.Stderr -cne '' -or ($invalid.Stdout|ConvertFrom-Json).error.id -cne 'Core.InputReadFailed'){throw "Invalid inventory policy did not precede IO: $option"}
    }
    'Native raw inventory: 4 differential snapshots/priorities, original JAR identity, 4 shared quota boundaries, E07 priority and 5 policy checks passed'
} finally {
    $resolved=[IO.Path]::GetFullPath($fixture)
    if(-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-inventory-*'){throw 'Unsafe fixture cleanup target'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
