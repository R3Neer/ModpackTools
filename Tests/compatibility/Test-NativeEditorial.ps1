param([Parameter(Mandatory)][string]$Executable,[string]$LegacyRoot='D:\Tools\ModpackTools')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-editorial-'+[guid]::NewGuid().ToString('N'))
try {
    [void][IO.Directory]::CreateDirectory((Join-Path $fixture '.modpack'))
    [IO.File]::WriteAllText((Join-Path $fixture '.modpack/project.psd1'),"@{SchemaVersion=1;Id='fixture'}")
    [IO.File]::WriteAllText((Join-Path $fixture 'pack.toml'),"name = `"Fixture`"`n")
    $path=Join-Path $fixture '.modpack/metadata.psd1'; $runner=Join-Path $fixture 'legacy.ps1'
    [IO.File]::WriteAllText($runner,@'
param($ModulePath,$RootPath)
Import-Module $ModulePath -Force
& (Get-Module ModpackTools) {
 param($root)
 try { Get-ModpackMetadata -Project ([pscustomobject]@{Root=$root})|ConvertTo-Json -Depth 100 -Compress }
 catch { [ordered]@{failure=$true;id=$_.Exception.Data['ModpackTools.ErrorId'];category=[string][Management.Automation.ErrorCategory]$_.Exception.Data['ModpackTools.ErrorCategory'];target=$_.Exception.Data['ModpackTools.TargetObject']}|ConvertTo-Json -Depth 100 -Compress }
} $RootPath
'@)
    function ConvertTo-CanonicalValue($value) {
        if($value -is [Collections.IDictionary]) {
            $result=[ordered]@{}; foreach($key in @($value.Keys|Sort-Object)){$result[$key]=ConvertTo-CanonicalValue $value[$key]}; return $result
        }
        if($value -is [array]) { $items=@(foreach($child in $value){ConvertTo-CanonicalValue $child}); return ,$items }
        return $value
    }
    function Assert-EditorialPair {
        $legacy=Invoke-NativeTestProcess (Get-Command pwsh).Source @('-NoProfile','-File',$runner,'-ModulePath',(Join-Path $LegacyRoot 'ModpackTools.psd1'),'-RootPath',$fixture)
        $native=Invoke-NativeTestProcess $Executable @('--inspect-editorial',$fixture)
        if($legacy.Exit -ne 0 -or $native.Stderr -cne ''){throw "Editorial transport failed $($legacy.Stderr) $($native.Stderr)"}
        $old=ConvertFrom-Json -InputObject $legacy.Stdout -AsHashtable
        $new=ConvertFrom-Json -InputObject $native.Stdout -AsHashtable
        if($old.ContainsKey('failure')) {
            if($native.Exit -ne 1 -or $new.error.id -cne $old.id -or $new.error.category -cne $old.category -or $new.error.target -cne $old.target){throw "Editorial failure mismatch: $($legacy.Stdout) $($native.Stdout)"}
        } else {
            $oldJson=ConvertTo-Json -InputObject (ConvertTo-CanonicalValue $old) -Depth 100 -Compress
            $newJson=ConvertTo-Json -InputObject (ConvertTo-CanonicalValue $new) -Depth 100 -Compress
            if($native.Exit -ne 0 -or $oldJson -cne $newJson){throw "Editorial data mismatch: $oldJson $newJson"}
        }
    }
    Assert-EditorialPair
    foreach($text in @(
        '@{}',
        "@{categories=@{perf=@{Name='Performance';Order=1.0}};Mods=@{'modrinth:test'=@{Name='Display';Side='both'}};ResourcePacks=@{};Unknown=@{Nested=@(1,2,'á',`$null)}}",
        '@{Categories=$null;ContentSchemaVersion=2;Content=$null}',
        '@{Mods=1;ResourcePacks=1}',
        '@{ResourcePacks=@(1)}',
        '@{ContentSchemaVersion=2;Content=$null}',
        '@{ContentSchemaVersion=@(0);Content=$null}',
        '@{ContentSchemaVersion=$true;Content=@{}}',
        '@{ContentSchemaVersion=1.0000000000000001}',
        "@{ContentSchemaVersion='1.0'}",
        '@{ContentSchemaVersion=@($false,0)}',
        '@{ContentSchemaVersion=@(1,$null)}',
        "@{ContentSchemaVersion=@('')}",
        '@{ContentSchemaVersion=@()}',
        '@{Unknown=99999999999999999999999999999999}',
        '@{Unknown=79228162514264337593543950335}',
        '@{Unknown=-9223372036854775808}',
        '@{Unknown=-9223372036854775809}')) {
        [IO.File]::WriteAllText($path,$text); Assert-EditorialPair
    }
    [IO.File]::WriteAllText($path,'@{Mods=')
    $invalid=Invoke-NativeTestProcess $Executable @('--inspect-editorial',$fixture)
    $result=$invalid.Stdout|ConvertFrom-Json
    if($invalid.Exit -ne 1 -or $invalid.Stderr -cne '' -or $result.error.id -cne 'ModpackTools.Metadata.InvalidFile' -or $result.error.target -cne $path -or -not $result.error.details){throw 'Invalid editorial PSD1 escaped a structured file diagnostic'}
    'Native editorial reader: 19 differential cases and malformed-PSD1 diagnostic passed'
} finally {
    $resolved=[IO.Path]::GetFullPath($fixture)
    if(-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-editorial-*'){throw 'Unsafe fixture cleanup target'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
