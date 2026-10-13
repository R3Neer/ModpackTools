param([Parameter(Mandatory)][string]$Executable,[string]$LegacyRoot='D:\Tools\ModpackTools')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-localzip-'+[guid]::NewGuid().ToString('N'))
try {
    $pack=Join-Path $fixture "pack á O'Brien"
    [void][IO.Directory]::CreateDirectory((Join-Path $pack '.modpack'))
    [IO.File]::WriteAllText((Join-Path $pack '.modpack/project.psd1'),"@{SchemaVersion=1;Id='fixture'}")
    [IO.File]::WriteAllText((Join-Path $pack 'pack.toml'),"name = `"Fixture`"`n")
    $runner=Join-Path $fixture 'legacy.ps1'
    [IO.File]::WriteAllText($runner,@'
param($ModulePath,$RootPath)
Import-Module $ModulePath -Force
& (Get-Module ModpackTools) {
    param($root)
    $project=[pscustomobject]@{Root=$root}
    $knownResources=@(Get-PackwizItems -Project $project -Directory resourcepacks -Kind resourcepack)
    $knownShaders=@(Get-PackwizItems -Project $project -Directory shaderpacks -Kind shaderpack)
    $items=@(
        Get-LocalZipItems -Project $project -Directory resourcepacks -Kind resourcepack -KnownItems $knownResources
        Get-LocalZipItems -Project $project -Directory shaderpacks -Kind shaderpack -KnownItems $knownShaders
    )
    $projection=@($items|ForEach-Object{[ordered]@{id=$_.Id;kind=$_.Kind;name=$_.Name;filename=$_.Filename;side=$_.Side;source=$_.Source;metadata_path=$_.MetadataPath;category=$_.Category}})
    ConvertTo-Json -InputObject $projection -Depth 10 -Compress
} $RootPath
'@)
    function Assert-LocalZipPair {
        $legacy=Invoke-NativeTestProcess (Get-Command pwsh).Source @('-NoProfile','-File',$runner,'-ModulePath',(Join-Path $LegacyRoot 'ModpackTools.psd1'),'-RootPath',$pack)
        $native=Invoke-NativeTestProcess $Executable @('--inspect-local-zips',$pack)
        if($native.Exit -ne 0 -or $native.Stderr -cne '' -or $legacy.Exit -ne 0){throw "Local ZIP transport failed: $($native.Stdout) $($legacy.Stderr)"}
        $expected=ConvertFrom-Json -InputObject $legacy.Stdout -NoEnumerate
        $actual=ConvertFrom-Json -InputObject $native.Stdout -NoEnumerate
        if((ConvertTo-Json -InputObject $expected -Depth 20 -Compress) -cne (ConvertTo-Json -InputObject $actual -Depth 20 -Compress)){throw "Local ZIP mismatch:`n$($legacy.Stdout)`n$($native.Stdout)"}
    }
    Assert-LocalZipPair
    foreach($relative in @('resourcepacks/z.zip','resourcepacks/a.ZIP','resourcepacks/.zip','resourcepacks/nested/ignored.zip','shaderpacks/basic.zip')) {
        $path=Join-Path $pack $relative; [void][IO.Directory]::CreateDirectory((Split-Path $path -Parent))
        [IO.File]::WriteAllText($path,'not an archive')
    }
    Assert-LocalZipPair
    $system=Join-Path $pack 'resourcepacks/z.zip'; [IO.File]::SetAttributes($system,[IO.FileAttributes]::System)
    $hidden=Join-Path $pack 'shaderpacks/hidden.zip'; [IO.File]::WriteAllText($hidden,'hidden')
    [IO.File]::SetAttributes($hidden,[IO.FileAttributes]::Hidden)
    Assert-LocalZipPair
    $metadata=Join-Path $pack 'resourcepacks/nested/known.pw.toml'
    [IO.File]::WriteAllText($metadata,"filename = `"A.zip`"`n")
    Assert-LocalZipPair
    $unicode=Join-Path $pack 'resourcepacks/café.zip'; [IO.File]::WriteAllText($unicode,'invalid archive')
    [IO.File]::WriteAllText($metadata,"filename = `"café.zip`"`n")
    Assert-LocalZipPair
    [void][IO.Directory]::CreateDirectory((Join-Path $pack 'mods'))
    [IO.File]::WriteAllText((Join-Path $pack 'mods/invalid.pw.toml'),'name = [')
    Assert-LocalZipPair
    [IO.File]::SetAttributes((Join-Path $pack 'resourcepacks'),[IO.FileAttributes]::Hidden)
    Assert-LocalZipPair
    $exact=Invoke-NativeTestProcess $Executable @('--inspect-local-zips',$pack,'--max-files','5')
    if($exact.Exit -ne 0 -or $exact.Stderr -cne ''){throw 'Exact local ZIP budget failed'}
    $over=Invoke-NativeTestProcess $Executable @('--inspect-local-zips',$pack,'--max-files','4')
    if($over.Exit -ne 1 -or $over.Stderr -cne '' -or ($over.Stdout|ConvertFrom-Json).error.id -cne 'ModpackTools.Content.ReadLimitExceeded'){throw 'Suppressed local ZIPs did not consume budget'}
    'Native local ZIP reader: 7 differential cases and 2 exact/over budget cases passed'
} finally {
    $resolved=[IO.Path]::GetFullPath($fixture)
    if(-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-localzip-*'){throw 'Unsafe fixture cleanup target'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
