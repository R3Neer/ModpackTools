param([Parameter(Mandatory)][string]$Inspector,[Parameter(Mandatory)][string]$NativeExecutable)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$inspectorPath=[IO.Path]::GetFullPath($Inspector)
$temporary=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$run=Join-Path $temporary ('modpacktools-native package-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($run)
function Invoke-Package($Request) {
    $path=Join-Path $run ('request-'+[guid]::NewGuid().ToString('N')+'.json')
    [IO.File]::WriteAllText($path,($Request|ConvertTo-Json -Depth 12 -Compress))
    $capture=Invoke-NativeTestProcess $inspectorPath @('--inspect-native-package',$path) -TimeoutMilliseconds 120000
    if($capture.Stderr){throw "Unexpected inspector stderr: $($capture.Stderr)"}
    [pscustomobject]@{Exit=$capture.Exit;Result=($capture.Stdout|ConvertFrom-Json)}
}
function Rewrite-Archive([string]$Source,[string]$Destination,[string]$Mode) {
    $inputStream=[IO.File]::OpenRead($Source);$inputArchive=[IO.Compression.ZipArchive]::new($inputStream,[IO.Compression.ZipArchiveMode]::Read)
    $outputStream=[IO.File]::Open($Destination,[IO.FileMode]::CreateNew);$outputArchive=[IO.Compression.ZipArchive]::new($outputStream,[IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach($entry in $inputArchive.Entries){
            $sourceEntry=$entry.Open();$memory=[IO.MemoryStream]::new()
            try{$sourceEntry.CopyTo($memory);$bytes=$memory.ToArray()}finally{$memory.Dispose();$sourceEntry.Dispose()}
            if($Mode -eq 'hash' -and $entry.FullName -ceq 'LICENSE'){$bytes[0]=$bytes[0] -bxor 1}
            if($Mode -eq 'duplicate' -and $entry.FullName -ceq 'package.json'){
                $text=[Text.Encoding]::UTF8.GetString($bytes);$bytes=[Text.Encoding]::UTF8.GetBytes('{"schema_version":1,'+$text.Substring(1))
            }
            $target=$outputArchive.CreateEntry($entry.FullName).Open();try{$target.Write($bytes)}finally{$target.Dispose()}
        }
        if($Mode -eq 'traversal'){$target=$outputArchive.CreateEntry('../escape.txt').Open();try{$target.WriteByte(65)}finally{$target.Dispose()}}
        if($Mode -eq 'legacy'){$target=$outputArchive.CreateEntry('install.ps1').Open();try{$target.WriteByte(65)}finally{$target.Dispose()}}
    } finally {$outputArchive.Dispose();$outputStream.Dispose();$inputArchive.Dispose();$inputStream.Dispose()}
}
try {
    $archive=Join-Path $run 'valid fixture.zip'
    $created=Invoke-Package @{CreateFixture=$true;ArchivePath=$archive;Executable=[IO.Path]::GetFullPath($NativeExecutable)}
    if($created.Exit -ne 0){throw "Fixture creation failed: $($created.Result|ConvertTo-Json -Compress)"}
    $stages=Join-Path $run 'staging with spaces'
    $valid=Invoke-Package @{ArchivePath=$archive;ArchiveSha256=$created.Result.ArchiveSha256;StagingParent=$stages}
    if($valid.Exit -ne 0 -or @($valid.Result.Files).Count -ne 18 -or $valid.Result.ProgressEvents -lt 20 -or $valid.Result.BuildId -cne $created.Result.BuildId){throw 'Valid native package did not prepare the complete payload/progress/identity'}
    foreach($file in $valid.Result.Files){$path=Join-Path $valid.Result.Directory $file.Path;if((Get-Item -LiteralPath $path).Length -ne $file.Length -or (Get-FileHash -LiteralPath $path).Hash -cne $file.Sha256){throw "Prepared bytes differ: $path"}}
    $before=@(Get-ChildItem -LiteralPath $stages -Force).Count
    $wrong=Invoke-Package @{ArchivePath=$archive;ArchiveSha256=('0'*64);StagingParent=$stages}
    if($wrong.Exit -ne 1 -or $wrong.Result.Id -ne 'ModpackTools.Package.ArchiveHashMismatch' -or @(Get-ChildItem -LiteralPath $stages -Force).Count -ne $before){throw 'Untrusted hash reached staging'}
    foreach($mode in @('legacy','traversal','duplicate','hash')){
        $variant=Join-Path $run ($mode+'.zip');Rewrite-Archive $archive $variant $mode
        $result=Invoke-Package @{ArchivePath=$variant;ArchiveSha256=(Get-FileHash -LiteralPath $variant).Hash;StagingParent=$stages}
        $expected=@{legacy='ModpackTools.Package.LegacyArchive';traversal='ModpackTools.Package.ArchiveInvalid';duplicate='ModpackTools.Package.InvalidManifest';hash='ModpackTools.Package.PayloadHashMismatch'}[$mode]
        if($result.Exit -ne 1 -or $result.Result.Id -ne $expected -or $result.Result.RetainStage -or @(Get-ChildItem -LiteralPath $stages -Force).Count -ne $before){throw "Invalid package $mode was accepted or did not safely clean its own verified stage: $($result.Result|ConvertTo-Json -Compress)"}
    }
    $retained=Invoke-Package @{ArchivePath=$archive;ArchiveSha256=$created.Result.ArchiveSha256;StagingParent=$stages;ChangeCompletedFileAndCancel=$true}
    if($retained.Exit -ne 1 -or $retained.Result.Id -ne 'ModpackTools.Package.StageRetained' -or !$retained.Result.RetainStage -or ![IO.File]::ReadAllText((Join-Path $retained.Result.Target 'LICENSE')).EndsWith('foreign edit')){throw 'Cancellation erased foreign stage changes or failed to report their exact location'}
    $streams=Invoke-Package @{ArchivePath=$archive;ArchiveSha256=$created.Result.ArchiveSha256;StagingParent=$stages;AddStreamAndCancel=$true}
    if($streams.Exit -ne 1 -or $streams.Result.Id -ne 'ModpackTools.Package.StageRetained' -or !$streams.Result.RetainStage -or [IO.File]::ReadAllText((Join-Path $streams.Result.Target 'LICENSE:foreign')) -cne 'foreign stream'){throw 'Cancellation erased an unknown alternate data stream or omitted its retained path'}
    $directoryStreams=Invoke-Package @{ArchivePath=$archive;ArchiveSha256=$created.Result.ArchiveSha256;StagingParent=$stages;AddDirectoryStreamAndCancel=$true}
    if($directoryStreams.Exit -ne 1 -or $directoryStreams.Result.Id -ne 'ModpackTools.Package.StageRetained' -or !$directoryStreams.Result.RetainStage -or [IO.File]::ReadAllText(($directoryStreams.Result.Target+':foreign')) -cne 'foreign directory stream'){throw 'Cancellation erased an unknown directory stream or omitted its retained path'}
    'PASS: native package stage bytes/progress, trusted hash, legacy/traversal/duplicate rejection, owned hash-failure cleanup and foreign-file/file-ADS/directory-ADS retention'
} finally {
    $resolved=[IO.Path]::GetFullPath($run)
    if(!$resolved.StartsWith($temporary,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^modpacktools-native package-[0-9a-f]{32}$'){throw 'Unsafe temporary cleanup path'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
