param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$PackwizExecutable)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$root=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-dependency archive-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($root)
try {
    $archivePath=Join-Path $root 'verified package.zip'
    $archive=[IO.Compression.ZipArchive]::new([IO.File]::Create($archivePath),[IO.Compression.ZipArchiveMode]::Create)
    try {
        $destination=$archive.CreateEntry('packwiz.exe').Open();$source=[IO.File]::OpenRead($PackwizExecutable)
        try {$source.CopyTo($destination)} finally {$source.Dispose();$destination.Dispose()}
        foreach($entry in @(@('LICENSE.packwiz.txt','Private synthetic license fixture, not distributed.'),@('ignored.txt','Do not materialize this entry.'))) {
            $writer=[IO.StreamWriter]::new($archive.CreateEntry($entry[0]).Open(),[Text.UTF8Encoding]::new($false))
            try {$writer.Write($entry[1])} finally {$writer.Dispose()}
        }
    } finally {$archive.Dispose()}
    $archiveHash=(Get-FileHash -LiteralPath $archivePath).Hash;$executableHash=(Get-FileHash -LiteralPath $PackwizExecutable).Hash
    $parent=Join-Path $root 'unactivated staging'
    $request=@{ArchivePath=$archivePath;StagingParent=$parent;Version='fixture';ArchiveSha256=$archiveHash;ExecutableSha256=$executableHash}
    $requestPath=Join-Path $root 'request.json';[IO.File]::WriteAllText($requestPath,($request|ConvertTo-Json))
    $capture=Invoke-NativeTestProcess $Executable @('--inspect-dependency-archive',$requestPath)
    if($capture.Exit -ne 0 -or $capture.Stderr -ne ''){throw "Package preparation failed: $($capture.Stdout) $($capture.Stderr)"}
    $result=$capture.Stdout|ConvertFrom-Json
    if($result.Version -ne 'fixture' -or $result.Files.Count -ne 2 -or (Get-FileHash -LiteralPath (Join-Path $result.Directory 'packwiz.exe')).Hash -cne $executableHash -or (Test-Path -LiteralPath (Join-Path $result.Directory 'ignored.txt'))){throw 'Prepared package content mismatch'}
    foreach($file in $result.Files){$actual=Get-Item -LiteralPath (Join-Path $result.Directory $file.Name);if($actual.Length -ne $file.Length -or (Get-FileHash -LiteralPath $actual.FullName).Hash -cne $file.Sha256){throw 'Prepared package evidence mismatch'}}
    $request.StagingParent=Join-Path $root 'reject destination';$request.ArchiveSha256='0'*64
    [IO.File]::WriteAllText($requestPath,($request|ConvertTo-Json))
    $capture=Invoke-NativeTestProcess $Executable @('--inspect-dependency-archive',$requestPath)
    $failure=$capture.Stdout|ConvertFrom-Json
    if($capture.Exit -ne 1 -or $capture.Stderr -ne '' -or $failure.error.id -ne 'ModpackTools.Dependency.ArchiveHashMismatch' -or (Test-Path -LiteralPath $request.StagingParent)){throw 'Hash rejection did not preserve destination absence'}
    if((Get-FileHash -LiteralPath $archivePath).Hash -cne $archiveHash -or (Get-FileHash -LiteralPath $PackwizExecutable).Hash -cne $executableHash){throw 'Archive preparation changed input bytes'}
    'Native dependency archive verification/preparation passed; executable never invoked'
} finally {
    $resolved=[IO.Path]::GetFullPath($root)
    if(-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-dependency archive-*'){throw 'Unsafe package fixture cleanup'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
