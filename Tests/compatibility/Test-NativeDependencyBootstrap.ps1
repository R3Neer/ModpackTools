param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$PackwizExecutable)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$root=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-dependency bootstrap-'+[guid]::NewGuid().ToString('N'))
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
    $temporary=Join-Path $root 'temporary spaces';$configuration=Join-Path $root 'private configuration'
    [void][IO.Directory]::CreateDirectory($temporary);[void][IO.Directory]::CreateDirectory($configuration)
    $sentinel=Join-Path $temporary 'foreign.keep';[IO.File]::WriteAllText($sentinel,'Keep this unrelated temporary-parent file.')
    $configPath=Join-Path $configuration 'config.psd1';[IO.File]::WriteAllText($configPath,"@{Root='preserved';PackwizPath='preserved'}")
    $sentinelHash=(Get-FileHash -LiteralPath $sentinel).Hash;$configHash=(Get-FileHash -LiteralPath $configPath).Hash
    $request=@{ArchivePath=$archivePath;ConfigurationDirectory=$configuration;StateDirectory=(Join-Path $root 'stable state');TemporaryParent=$temporary;Version='fixture verified';ArchiveSha256=$archiveHash;ExecutableSha256=$executableHash;Uri='https://fixture.invalid/package.zip';ControlledHttp=$true;Repeat=$true}
    $requestPath=Join-Path $root 'request.json';[IO.File]::WriteAllText($requestPath,($request|ConvertTo-Json))
    $capture=Invoke-NativeTestProcess $Executable @('--inspect-dependency-bootstrap',$requestPath) -TimeoutMilliseconds 30000
    if($capture.Exit -ne 0 -or $capture.Stderr -ne ''){throw "Bootstrap failed: $($capture.Stdout) $($capture.Stderr)"}
    $result=$capture.Stdout|ConvertFrom-Json
    if(-not $result.Installed -or $result.RepeatedInstalled -or $result.Diagnostics.Count -ne 0 -or (Get-FileHash -LiteralPath $result.Path).Hash -cne $executableHash -or @(Get-ChildItem -LiteralPath $temporary -Directory -Filter '.modpacktools-bootstrap-*').Count -ne 0){throw 'Managed bootstrap/idempotence/cleanup mismatch'}
    [void]$request.Remove('ArchivePath');$request.Repeat=$false;$request.Failure='fixture transfer failure';$request.ConfigurationDirectory=Join-Path $root 'failed configuration'
    [IO.File]::WriteAllText($requestPath,($request|ConvertTo-Json))
    $capture=Invoke-NativeTestProcess $Executable @('--inspect-dependency-bootstrap',$requestPath)
    $failure=$capture.Stdout|ConvertFrom-Json
    if($capture.Exit -ne 1 -or $capture.Stderr -ne '' -or $failure.error.id -ne 'ModpackTools.Dependency.DownloadFailed' -or (Test-Path -LiteralPath $request.ConfigurationDirectory) -or @(Get-ChildItem -LiteralPath $temporary -Directory -Filter '.modpacktools-bootstrap-*').Count -ne 0){throw 'Failed bootstrap published or retained an unannounced workspace'}
    if((Get-FileHash -LiteralPath $archivePath).Hash -cne $archiveHash -or (Get-FileHash -LiteralPath $PackwizExecutable).Hash -cne $executableHash -or (Get-FileHash -LiteralPath $sentinel).Hash -cne $sentinelHash -or (Get-FileHash -LiteralPath $configPath).Hash -cne $configHash){throw 'Bootstrap changed an input, configuration or unrelated sentinel'}
    'Native managed dependency bootstrap/repeat/failure cleanup passed without changing configuration'
} finally {
    $resolved=[IO.Path]::GetFullPath($root)
    if(-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-dependency bootstrap-*'){throw 'Unsafe bootstrap fixture cleanup'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
