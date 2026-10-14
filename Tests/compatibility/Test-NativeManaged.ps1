param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$LegacyRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-managed-'+[guid]::NewGuid().ToString('N'))
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
    $items=@(
        Get-PackwizItems -Project $project -Directory mods -Kind mod
        Get-PackwizItems -Project $project -Directory resourcepacks -Kind resourcepack
        Get-PackwizItems -Project $project -Directory shaderpacks -Kind shaderpack
    )
    $projection=@($items|ForEach-Object{[ordered]@{id=$_.Id;kind=$_.Kind;name=$_.Name;filename=$_.Filename;side=$_.Side;version_id=$_.VersionId;source=$_.Source;metadata_path=$_.MetadataPath;category=$_.Category;pinned=$_.Pinned}})
    ConvertTo-Json -InputObject $projection -Depth 10 -Compress
} $RootPath
'@)
    function Assert-ManagedPair {
        $legacy=Invoke-NativeTestProcess (Get-Command pwsh).Source @('-NoProfile','-File',$runner,'-ModulePath',(Join-Path $LegacyRoot 'ModpackTools.psd1'),'-RootPath',$pack)
        $native=Invoke-NativeTestProcess $Executable @('--inspect-managed',$pack)
        if($native.Exit -ne 0 -or $native.Stderr -cne '' -or $legacy.Exit -ne 0){throw "Managed transport failed: $($native.Stdout) $($legacy.Stderr)"}
        $expected=ConvertFrom-Json -InputObject $legacy.Stdout -NoEnumerate
        $actual=ConvertFrom-Json -InputObject $native.Stdout -NoEnumerate
        if((ConvertTo-Json -InputObject $expected -Depth 20 -Compress) -cne (ConvertTo-Json -InputObject $actual -Depth 20 -Compress)){throw "Managed mismatch:`n$($legacy.Stdout)`n$($native.Stdout)"}
    }
    Assert-ManagedPair
    foreach($directory in @('mods','resourcepacks','shaderpacks')) {
        [void][IO.Directory]::CreateDirectory((Join-Path $pack $directory))
        [IO.File]::WriteAllText((Join-Path $pack "$directory/basic.pw.toml"),"name = `"Managed $directory`"`nfilename = `"physical.zip`"`nside = `"HOST`"`npin = true`n[update.modrinth]`nmod-id = `"same`"`nversion = `"v1`"`n")
    }
    Assert-ManagedPair
    $edge=Join-Path $pack 'mods/edge.pw.toml'
    foreach($text in @(
        "name = 'literal'`nfilename = `"edge.jar`"`n[update.curseforge]`nproject-id = `"12`"`n",
        "name = `"Quoted header`"`n[`"update.modrinth`"]`nmod-id = `"ignored`"`n",
        "name = `"Comment header`"`n[update.modrinth] #comment`nmod-id = `"ignored`"`n",
        "name = `"Numeric CF`"`n[update.curseforge]`nproject-id = 12`n",
        "name = `"Indented pin`"`n pin = true`nside = `"ArbitrarySide`"`n",
        "name = `"Array table`"`n[[update.modrinth]]`nmod-id = `"ignored`"`n",
        "name = `"Standalone version`"`n[update.modrinth]`nversion = `"v-only`"`n",
        "description = '''`n[not-table]`n'''`nname = `"After`"`npin = true`n",
        "description = '''`npin = true`n'''`nname = `"After`"`n",
        "description = '''`nname = `"Inside`"`n'''`nname = `"After`"`n"
    )) {[IO.File]::WriteAllText($edge,$text); Assert-ManagedPair}
    foreach($relative in @('mods/a/deep/a.pw.toml','mods/a/z.pw.toml','mods/b/a.pw.toml')) {
        $path=Join-Path $pack $relative; [void][IO.Directory]::CreateDirectory((Split-Path $path -Parent))
        [IO.File]::WriteAllText($path,"name = `"$relative`"`n")
    }
    [IO.File]::SetAttributes($edge,[IO.FileAttributes]::System)
    $hidden=Join-Path $pack 'mods/hidden.pw.toml'; [IO.File]::WriteAllText($hidden,'invalidTOML')
    [IO.File]::SetAttributes($hidden,[IO.FileAttributes]::Hidden)
    Assert-ManagedPair
    [IO.File]::SetAttributes($edge,[IO.FileAttributes]::Normal)
    [IO.File]::WriteAllText($edge,"name = `"`"`nfilename = `"`"`nside = `"`"`n[update.modrinth]`nmod-id = `"`"`nversion = `"`"`n")
    $empty=Invoke-NativeTestProcess $Executable @('--inspect-managed',$pack)
    $items=ConvertFrom-Json -InputObject $empty.Stdout -NoEnumerate
    $item=$items|Where-Object metadata_path -eq $edge
    if($empty.Exit -ne 0 -or $empty.Stderr -cne '' -or $item.id -cne 'packwiz:mods/edge.pw.toml' -or $item.name -cne 'edge' -or $item.filename -cne '' -or $item.version_id -cne '' -or $item.side -cne 'unknown'){throw 'E05 empty string policy mismatch'}
    foreach($option in @('--max-files','--max-directories','--max-bytes')) {
        $limited=Invoke-NativeTestProcess $Executable @('--inspect-managed',$pack,$option,'1')
        if($limited.Exit -ne 1 -or $limited.Stderr -cne '' -or ($limited.Stdout|ConvertFrom-Json).error.id -cne 'ModpackTools.Content.ReadLimitExceeded'){throw "Managed budget failed: $option"}
    }
    foreach($text in @('',"name = [",('x='+('['*5000)+'0'+(']'*5000)), 'name = "\U0001F600"')) {
        [IO.File]::WriteAllText($edge,$text)
        $invalid=Invoke-NativeTestProcess $Executable @('--inspect-managed',$pack)
        if($invalid.Exit -ne 1 -or $invalid.Stderr -cne '' -or ($invalid.Stdout|ConvertFrom-Json).error.id -cne 'ModpackTools.Content.InvalidMetadata'){throw 'Invalid managed data escaped structured failure'}
    }
    foreach($count in @(65536,262144)) {
        [IO.File]::WriteAllText($edge,(("`n"*$count)+'name="Blank lines"'))
        $blank=Invoke-NativeTestProcess $Executable @('--inspect-managed',$pack)
        $item=(ConvertFrom-Json -InputObject $blank.Stdout -NoEnumerate)|Where-Object metadata_path -eq $edge
        if($blank.Exit -ne 0 -or $blank.Stderr -cne '' -or $item.name -cne 'Blank lines' -or $item.pinned -ne $false){throw 'Valid blank-line metadata failed or changed values'}
    }
    'Native managed reader: 13 differential cases, E05, 3 budgets, 4 malformed/resource/escape and 2 blank-line regression cases passed'
} finally {
    $resolved=[IO.Path]::GetFullPath($fixture)
    if(-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-managed-*'){throw 'Unsafe fixture cleanup target'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
