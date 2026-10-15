param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$PackwizExecutable)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$root=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-dependency install-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($root)
try {
    $archivePath=Join-Path $root 'verified package.zip'
    $archive=[IO.Compression.ZipArchive]::new([IO.File]::Create($archivePath),[IO.Compression.ZipArchiveMode]::Create)
    try {
        $destination=$archive.CreateEntry('packwiz.exe').Open();$source=[IO.File]::OpenRead($PackwizExecutable)
        try {$source.CopyTo($destination)} finally {$source.Dispose();$destination.Dispose()}
        $writer=[IO.StreamWriter]::new($archive.CreateEntry('LICENSE.packwiz.txt').Open(),[Text.UTF8Encoding]::new($false))
        try {$writer.Write('Private synthetic license fixture, not distributed.')} finally {$writer.Dispose()}
    } finally {$archive.Dispose()}
    $archiveHash=(Get-FileHash -LiteralPath $archivePath).Hash;$executableHash=(Get-FileHash -LiteralPath $PackwizExecutable).Hash
    $target=Join-Path $root 'chosen destination';[void][IO.Directory]::CreateDirectory($target)
    $sentinel=Join-Path $target 'foreign.keep';[IO.File]::WriteAllText($sentinel,'Keep this unrelated file.')
    $sentinelHash=(Get-FileHash -LiteralPath $sentinel).Hash
    $request=@{ArchivePath=$archivePath;StagingParent=(Join-Path $root 'unactivated staging');TargetDirectory=$target;StateDirectory=(Join-Path $root 'stable recovery');Version='fixture';ArchiveSha256=$archiveHash;ExecutableSha256=$executableHash;Repeat=$true}
    $requestPath=Join-Path $root 'request.json';[IO.File]::WriteAllText($requestPath,($request|ConvertTo-Json))
    $capture=Invoke-NativeTestProcess $Executable @('--inspect-dependency-installation',$requestPath) -TimeoutMilliseconds 30000
    if($capture.Exit -ne 0 -or $capture.Stderr -ne ''){throw "Dependency installation failed: $($capture.Stdout) $($capture.Stderr)"}
    $result=$capture.Stdout|ConvertFrom-Json
    if(-not $result.Installed -or $result.RepeatedInstalled -or $result.Version -ne 'fixture' -or $result.Diagnostics.Count -ne 0 -or (Get-FileHash -LiteralPath $result.Path).Hash -cne $executableHash){throw 'Installed package/idempotence mismatch'}
    [IO.File]::WriteAllText((Join-Path $target 'LICENSE.packwiz.txt'),'corrupt license')
    $request.Repeat=$false;[IO.File]::WriteAllText($requestPath,($request|ConvertTo-Json))
    $capture=Invoke-NativeTestProcess $Executable @('--inspect-dependency-installation',$requestPath) -TimeoutMilliseconds 30000
    $repair=$capture.Stdout|ConvertFrom-Json
    if($capture.Exit -ne 0 -or $capture.Stderr -ne '' -or -not $repair.Installed -or [IO.File]::ReadAllText((Join-Path $target 'LICENSE.packwiz.txt')) -cne 'Private synthetic license fixture, not distributed.'){throw 'Corrupt license was not repaired'}
    if((Get-FileHash -LiteralPath $archivePath).Hash -cne $archiveHash -or (Get-FileHash -LiteralPath $PackwizExecutable).Hash -cne $executableHash -or (Get-FileHash -LiteralPath $sentinel).Hash -cne $sentinelHash){throw 'Installation changed an input or unrelated target file'}
    if(@(Get-ChildItem -LiteralPath $request.StateDirectory -Recurse -File|Where-Object {$_.Name -in @('owner.json','journal.json') -or $_.Name -like '*.tmp'}).Count -ne 0){throw 'Successful installation left pending recovery records'}
    'Native offline dependency install/repeat/license repair passed with real owned --help and preserved inputs'
} finally {
    $resolved=[IO.Path]::GetFullPath($root)
    if(-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-dependency install-*'){throw 'Unsafe installation fixture cleanup'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
