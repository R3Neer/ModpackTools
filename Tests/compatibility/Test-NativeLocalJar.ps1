param([Parameter(Mandatory)][string]$Executable,[string]$LegacyRoot='D:\Tools\ModpackTools')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-localjar-'+[guid]::NewGuid().ToString('N'))
try {
    $pack=Join-Path $fixture "pack á O'Brien"; [void][IO.Directory]::CreateDirectory((Join-Path $pack '.modpack'))
    [IO.File]::WriteAllText((Join-Path $pack '.modpack/project.psd1'),"@{SchemaVersion=1;Id='fixture'}")
    [IO.File]::WriteAllText((Join-Path $pack 'pack.toml'),"name = `"Fixture`"`n")
    $runner=Join-Path $fixture 'legacy.ps1'
    [IO.File]::WriteAllText($runner,@'
param($ModulePath,$RootPath)
Import-Module $ModulePath -Force
& (Get-Module ModpackTools) {
    param($root)
    $project=[pscustomobject]@{Root=$root}
    $known=@(Get-PackwizItems -Project $project -Directory mods -Kind mod)
    $items=@(Get-LocalModItems -Project $project -KnownItems $known)
    $projection=@($items|ForEach-Object{[ordered]@{id=$_.Id;kind=$_.Kind;name=$_.Name;filename=$_.Filename;side=$_.Side;source=$_.Source;metadata_path=$_.MetadataPath;category=$_.Category}})
    ConvertTo-Json -InputObject $projection -Depth 10 -Compress
} $RootPath
'@)
    function Write-LocalJar([string]$Relative) {
        $path=Join-Path $pack $Relative; [void][IO.Directory]::CreateDirectory((Split-Path $path -Parent))
        $archive=[IO.Compression.ZipFile]::Open($path,[IO.Compression.ZipArchiveMode]::Create)
        try {$writer=[IO.StreamWriter]::new($archive.CreateEntry('fabric.mod.json').Open(),[Text.UTF8Encoding]::new($false));try {$writer.Write('{"name":"Display","environment":"client"}')}finally{$writer.Dispose()}}finally{$archive.Dispose()}
    }
    function Assert-LocalJarPair {
        $legacy=Invoke-NativeTestProcess (Get-Command pwsh).Source @('-NoProfile','-File',$runner,'-ModulePath',(Join-Path $LegacyRoot 'ModpackTools.psd1'),'-RootPath',$pack)
        $native=Invoke-NativeTestProcess $Executable @('--inspect-local-jars',$pack)
        if($legacy.Exit -ne 0 -or $native.Exit -ne 0 -or $native.Stderr -cne ''){throw "Local JAR transport failed: $($legacy.Stderr) $($native.Stdout)"}
        $old=ConvertFrom-Json -InputObject $legacy.Stdout -NoEnumerate
        $actual=ConvertFrom-Json -InputObject $native.Stdout -NoEnumerate
        $new=@($actual|ForEach-Object{[ordered]@{id=$_.id;kind=$_.kind;name=$_.name;filename=$_.filename;side=$_.side;source=$_.source;metadata_path=$_.metadata_path;category=$_.category}})
        if((ConvertTo-Json -InputObject $old -Depth 20 -Compress) -cne (ConvertTo-Json -InputObject $new -Depth 20 -Compress)){throw "Local JAR mismatch: $($legacy.Stdout) $($native.Stdout)"}
    }
    Assert-LocalJarPair
    $invalid=Invoke-NativeTestProcess $Executable @('--inspect-local-jars',$pack,'--max-entry-bytes','0')
    if($invalid.Exit -ne 2 -or $invalid.Stderr -cne '' -or ($invalid.Stdout|ConvertFrom-Json).error.id -cne 'Core.InputReadFailed'){throw 'Invalid archive policy succeeded on an empty project'}
    foreach($relative in @('mods/root.jar','mods/nested/root.jar','mods/café.jar')){Write-LocalJar $relative}
    Assert-LocalJarPair
    $known=Join-Path $pack 'mods/known.pw.toml'; [IO.File]::WriteAllText($known,"filename = `"/root.jar`"`n")
    Assert-LocalJarPair
    [IO.File]::WriteAllText($known,"filename = `"café.jar`"`n")
    Assert-LocalJarPair
    [IO.File]::WriteAllText((Join-Path $pack 'mods/nested/known.pw.toml'),"filename = `"root.jar`"`n")
    Assert-LocalJarPair
    [IO.File]::WriteAllText($known,"filename = `"C:\\else\\root.jar`"`n")
    [IO.File]::WriteAllText((Join-Path $pack 'mods/corrupt.jar'),'not a ZIP')
    [IO.File]::SetAttributes((Join-Path $pack 'mods/root.jar'),[IO.FileAttributes]::System)
    [IO.File]::WriteAllText((Join-Path $pack 'mods/hidden.jar'),'hidden')
    [IO.File]::SetAttributes((Join-Path $pack 'mods/hidden.jar'),[IO.FileAttributes]::Hidden)
    [void][IO.Directory]::CreateDirectory((Join-Path $pack 'resourcepacks'))
    [IO.File]::WriteAllText((Join-Path $pack 'resourcepacks/invalid.pw.toml'),'name = [')
    Assert-LocalJarPair
    [IO.File]::SetAttributes((Join-Path $pack 'mods'),[IO.FileAttributes]::Hidden)
    Assert-LocalJarPair
    foreach($option in @('--max-files','--max-directories','--max-bytes')) {
        $limited=Invoke-NativeTestProcess $Executable @('--inspect-local-jars',$pack,$option,'1')
        if($limited.Exit -ne 1 -or $limited.Stderr -cne '' -or ($limited.Stdout|ConvertFrom-Json).error.id -cne 'ModpackTools.Content.ReadLimitExceeded'){throw "Aggregate JAR budget escaped: $option"}
    }
    $perEntry=Invoke-NativeTestProcess $Executable @('--inspect-local-jars',$pack,'--max-entry-bytes','1')
    $items=ConvertFrom-Json -InputObject $perEntry.Stdout -NoEnumerate
    if($perEntry.Exit -ne 0 -or $perEntry.Stderr -cne '' -or @($items|Where-Object status -eq limited).Count -ne 2){throw 'Per-archive uncertainty was not retained'}
    'Native local JAR reader: 7 differential cases, invalid empty-project policy, 3 aggregate budgets and per-archive uncertainty passed'
} finally {
    $resolved=[IO.Path]::GetFullPath($fixture)
    if(-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-localjar-*'){throw 'Unsafe fixture cleanup target'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
