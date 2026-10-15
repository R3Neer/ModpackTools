param([Parameter(Mandatory)][string]$Executable)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$root=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-dependency download-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($root)
try {
    # Opaque transfer bytes; ZIP structure belongs to DependencyArchive, not HTTP.
    $source=Join-Path $root 'response.bin';[IO.File]::WriteAllBytes($source,[byte[]](0..255))
    $sourceHash=(Get-FileHash -LiteralPath $source).Hash
    $request=@{Uri='https://fixture.invalid/package.zip';ExpectedSha256=$sourceHash;ResponsePath=$source;StagingParent=(Join-Path $root 'verified download')}
    $requestPath=Join-Path $root 'request.json';[IO.File]::WriteAllText($requestPath,($request|ConvertTo-Json))
    $capture=Invoke-NativeTestProcess $Executable @('--inspect-dependency-download',$requestPath)
    if($capture.Exit -ne 0 -or $capture.Stderr -ne ''){throw "Download fixture failed: $($capture.Stdout) $($capture.Stderr)"}
    $result=$capture.Stdout|ConvertFrom-Json
    if($result.Length -ne 256 -or $result.Requests -ne 1 -or $result.Sha256 -cne $sourceHash -or (Get-FileHash -LiteralPath $result.Path).Hash -cne $sourceHash){throw 'Verified download evidence mismatch'}
    $request.ExpectedSha256='0'*64;$request.StagingParent=Join-Path $root 'reject download'
    [IO.File]::WriteAllText($requestPath,($request|ConvertTo-Json))
    $capture=Invoke-NativeTestProcess $Executable @('--inspect-dependency-download',$requestPath)
    $failure=$capture.Stdout|ConvertFrom-Json
    if($capture.Exit -ne 1 -or $capture.Stderr -ne '' -or $failure.error.id -ne 'ModpackTools.Dependency.ArchiveHashMismatch' -or @(Get-ChildItem -LiteralPath $request.StagingParent -Force).Count -ne 0){throw 'Download hash rejection left a file or invalid failure'}
    $request.ExpectedSha256=$sourceHash;$request.StagingParent=Join-Path $root 'cancelled download';$request.Cancelled=$true
    [IO.File]::WriteAllText($requestPath,($request|ConvertTo-Json))
    $capture=Invoke-NativeTestProcess $Executable @('--inspect-dependency-download',$requestPath)
    $failure=$capture.Stdout|ConvertFrom-Json
    if($capture.Exit -ne 1 -or $capture.Stderr -ne '' -or $failure.error.id -ne 'ModpackTools.Core.OperationCancelled' -or (Test-Path -LiteralPath $request.StagingParent)){throw 'Pre-cancelled download wrote a file or lost cancellation'}
    if((Get-FileHash -LiteralPath $source).Hash -cne $sourceHash -or (Get-FileHash -LiteralPath $result.Path).Hash -cne $sourceHash){throw 'Download controls changed preserved bytes'}
    'Native controlled download/hash rejection/pre-cancellation passed without external network'
} finally {
    $resolved=[IO.Path]::GetFullPath($root)
    if(-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-dependency download-*'){throw 'Unsafe download fixture cleanup'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
