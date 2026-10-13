param([Parameter(Mandatory)][string]$Executable,[string]$LegacyRoot='D:\Tools\ModpackTools')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-resource-view-'+[guid]::NewGuid().ToString('N'))
try {
    foreach($directory in @('.modpack','resourcepacks','config')){[void][IO.Directory]::CreateDirectory((Join-Path $fixture $directory))}
    [IO.File]::WriteAllText((Join-Path $fixture '.modpack/project.psd1'),"@{SchemaVersion=1;Id='fixture'}")
    [IO.File]::WriteAllText((Join-Path $fixture 'pack.toml'),'[index]')
    [IO.File]::WriteAllText((Join-Path $fixture 'resourcepacks/first.pw.toml'),"name = `"Technical`"`nfilename = `"A.zip`"`npin = true`n")
    [IO.File]::WriteAllText((Join-Path $fixture 'resourcepacks/second.pw.toml'),"name = `"Other`"`nfilename = `"a.zip`"`n")
    [IO.File]::WriteAllText((Join-Path $fixture 'resourcepacks/null.pw.toml'),"name = `"Without filename`"`n")
    [IO.File]::WriteAllText((Join-Path $fixture 'resourcepacks/Z.zip'),'local ZIP display does not open this file')
    [IO.File]::WriteAllText((Join-Path $fixture 'config/defaultoptions-common.toml'),'defaultResourcePacks=["file/A.zip","vanilla","file/missing.zip","file/A.zip","file/"]')
    $path=Join-Path $fixture '.modpack/metadata.psd1';$runner=Join-Path $fixture 'legacy.ps1'
    [IO.File]::WriteAllText($runner,@'
param($ModulePath,$RootPath)
Import-Module $ModulePath -Force
& (Get-Module ModpackTools) {
 param($root)
 function Physical($items){return ,@(foreach($item in $items){$row=[ordered]@{id=$item.Id;kind=$item.Kind;name=$item.Name;filename=$item.Filename;side=$item.Side;source=$item.Source;metadata_path=$item.MetadataPath;category=$item.Category};if($item.Source -eq 'packwiz'){$row.version_id=$item.VersionId;$row.pinned=$item.Pinned};$row})}
 try {
  $view=Get-ModpackInventory -Project ([pscustomobject]@{Root=$root})
  $active=@(foreach($item in $view.ActiveResources){[ordered]@{id=$item.Id;kind=$item.Kind;name=$item.Name;filename=$item.Filename;source=$item.Source;enabled=$item.Enabled;priority=$item.Priority}})
  ConvertTo-Json -InputObject ([ordered]@{resources=(Physical $view.ResourcePacks);active=$active;inactive=(Physical $view.InactiveResources)}) -Depth 20 -Compress
 }catch{ConvertTo-Json -InputObject @{failure=$true;id=$_.FullyQualifiedErrorId;message=$_.Exception.Message} -Compress}
} $RootPath
'@)
    $success=0;$failure=0
    foreach($entry in @('@{Name=@()}','@(@{Name=@()})','@(@{Name=@()},@{Name=@()})','@(@{Name=@()},@{Name=$null})','@{}',"@{Name='Display'}",'@{Name=$null}',"@{Name=@('A',`$null,'B')}",'@{Name=1.234567891234567}',"@(@{Name='Wrapped'})","@(@{Name=`$null},@{Name='B'})",'@(@{},@{})','$null','$false','0',"''",'@()')) {
        [IO.File]::WriteAllText($path,"@{ResourcePacks=@{'file/A.zip'=$entry;unused=`$null}}")
        $old=Invoke-NativeTestProcess (Get-Command pwsh).Source @('-NoProfile','-File',$runner,'-ModulePath',(Join-Path $LegacyRoot 'ModpackTools.psd1'),'-RootPath',$fixture)
        $new=Invoke-NativeTestProcess $Executable @('--inspect-resource-view',$fixture)
        if($old.Exit -ne 0 -or $new.Stderr -cne ''){throw 'Resource view transport failed'}
        $oldValue=ConvertFrom-Json -InputObject $old.Stdout
        $newValue=ConvertFrom-Json -InputObject $new.Stdout
        if($oldValue.failure -eq $true){
            if($new.Exit -ne 1 -or $newValue.error.id -cne 'ModpackTools.Metadata.InvalidResourceOverride' -or $newValue.error.category -cne 'InvalidData' -or $newValue.error.target -cne 'file/A.zip' -or -not $newValue.error.hint){throw "Resource override escaped E09: $entry $($old.Stdout) $($new.Stdout)"};$failure++
        }else{
            if($new.Exit -ne 0 -or (ConvertTo-Json -InputObject $oldValue -Depth 20 -Compress) -cne (ConvertTo-Json -InputObject $newValue -Depth 20 -Compress)){throw "Resource view mismatch: $entry $($old.Stdout) $($new.Stdout)"};$success++
        }
    }
    [IO.File]::WriteAllText($path,'@{}')
    foreach($directory in @('mods','shaderpacks')){[void][IO.Directory]::CreateDirectory((Join-Path $fixture $directory));[IO.File]::WriteAllText((Join-Path $fixture "$directory/invalid.pw.toml"),'name=[')}
    $isolated=Invoke-NativeTestProcess $Executable @('--inspect-resource-view',$fixture)
    if($isolated.Exit -ne 0 -or $isolated.Stderr -cne ''){throw 'Resource-only inspection read unrelated kind metadata'}
    "Native resource view: $success differential successes, $failure improved errors and kind-isolation control passed"
} finally {
    $resolved=[IO.Path]::GetFullPath($fixture)
    if(-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-resource-view-*'){throw 'Unsafe fixture cleanup target'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
