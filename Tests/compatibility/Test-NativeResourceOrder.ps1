param([Parameter(Mandatory)][string]$Executable,[string]$LegacyRoot='D:\Tools\ModpackTools')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-resource-order-'+[guid]::NewGuid().ToString('N'))
try {
    [void][IO.Directory]::CreateDirectory((Join-Path $fixture '.modpack'))
    [void][IO.Directory]::CreateDirectory((Join-Path $fixture 'config'))
    [IO.File]::WriteAllText((Join-Path $fixture '.modpack/project.psd1'),"@{SchemaVersion=1;Id='fixture'}")
    [IO.File]::WriteAllText((Join-Path $fixture 'pack.toml'),'[index]')
    $path=Join-Path $fixture 'config/defaultoptions-common.toml';$runner=Join-Path $fixture 'legacy.ps1'
    [IO.File]::WriteAllText($runner,@'
param($ModulePath,$RootPath)
Import-Module $ModulePath -Force
& (Get-Module ModpackTools) {
 param($root)
 try { ConvertTo-Json -InputObject @(Get-DefaultResourcePackOrder -Project ([pscustomobject]@{Root=$root})) -Compress }
 catch { ConvertTo-Json -InputObject @{failure=$true;id=$_.Exception.Data['ModpackTools.ErrorId'];target=$_.Exception.Data['ModpackTools.TargetObject']} -Compress }
} $RootPath
'@)
    $success=0;$failure=0
    function Assert-OrderPair {
        $old=Invoke-NativeTestProcess (Get-Command pwsh).Source @('-NoProfile','-File',$runner,'-ModulePath',(Join-Path $LegacyRoot 'ModpackTools.psd1'),'-RootPath',$fixture)
        $new=Invoke-NativeTestProcess $Executable @('--inspect-resource-order',$fixture)
        if($old.Exit -ne 0 -or $new.Stderr -cne ''){throw 'Resource order transport failed'}
        $oldValue=ConvertFrom-Json -InputObject $old.Stdout -NoEnumerate
        $newValue=ConvertFrom-Json -InputObject $new.Stdout -NoEnumerate
        if($oldValue -isnot [array] -and $oldValue.failure -eq $true){
            $id=if($oldValue.id){$oldValue.id}else{'ModpackTools.ResourcePack.InvalidDefaultOptions'}
            $target=if($oldValue.id){$oldValue.target}else{$path}
            if($new.Exit -ne 1 -or $newValue.error.id -cne $id -or $newValue.error.target -cne $target -or $newValue.error.category -cne 'InvalidData' -or -not $newValue.error.hint){throw "Resource order failure mismatch: $($old.Stdout) $($new.Stdout)"}
            $script:failure++
        }else{
            if($new.Exit -ne 0 -or (ConvertTo-Json -InputObject $oldValue -Compress) -cne (ConvertTo-Json -InputObject $newValue -Compress)){throw "Resource order mismatch: $($old.Stdout) $($new.Stdout)"}
            $script:success++
        }
    }
    Assert-OrderPair
    foreach($text in @('other=1','defaultResourcePacks=[]','defaultResourcePacks=["file/Á.zip","vanilla","file/Á.zip"]','DefaultResourcePacks=["upper"]','defaultResourcePacks=["one"] defaultResourcePacks=["two"]',"defaultResourcePacks=['ignored']",'defaultResourcePacks=[["nested"],"second"]',"defaultResourcePacks=[`n#`"comment`"`n`"active`"`n]",'defaultResourcePacks=["escaped\/name","\u00E1"]',"defaultResourcePacks=[`n# ]`n`"outside`"`n]",'defaultResourcePacks=["" ]','defaultResourcePacks=["\U0001F600"]','defaultResourcePacks=["open"','')) {
        [IO.File]::WriteAllText($path,$text); Assert-OrderPair
    }
    [IO.File]::WriteAllText($path,'defaultResourcePacks=["A","B"]')
    $exact=Invoke-NativeTestProcess $Executable @('--inspect-resource-order',$fixture,'--max-items','2')
    $over=Invoke-NativeTestProcess $Executable @('--inspect-resource-order',$fixture,'--max-items','1')
    if($exact.Exit -ne 0 -or $over.Exit -ne 1 -or $over.Stderr -cne '' -or ($over.Stdout|ConvertFrom-Json).error.id -cne 'ModpackTools.Content.ReadLimitExceeded'){throw 'Resource-order count boundary failed'}
    [IO.File]::Delete($path)
    $invalid=Invoke-NativeTestProcess $Executable @('--inspect-resource-order',$fixture,'--max-items','0')
    if($invalid.Exit -ne 2 -or $invalid.Stderr -cne '' -or ($invalid.Stdout|ConvertFrom-Json).error.id -cne 'Core.InputReadFailed'){throw 'Invalid resource-order policy became empty success'}
    [IO.File]::WriteAllText($path, ("`n"*524288)+'unrelated=1')
    $blank=Invoke-NativeTestProcess $Executable @('--inspect-resource-order',$fixture)
    if($blank.Exit -ne 0 -or $blank.Stderr -cne '' -or $blank.Stdout -cne '[]'){throw 'Blank-line resource-order regression failed'}
    "Native resource order: $success differential successes, $failure failures, exact/over/invalid policy and blank-line control passed"
} finally {
    $resolved=[IO.Path]::GetFullPath($fixture)
    if(-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-resource-order-*'){throw 'Unsafe fixture cleanup target'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
