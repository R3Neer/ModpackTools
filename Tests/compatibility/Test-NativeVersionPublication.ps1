param([Parameter(Mandatory)][string]$Inspector,[Parameter(Mandatory)][string]$NativeExecutable)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$inspectorPath=[IO.Path]::GetFullPath($Inspector)
$temporary=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$run=Join-Path $temporary ('modpacktools-version publication-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($run)
function Invoke-Publication($Request) {
    $path=Join-Path $run ('request-'+[guid]::NewGuid().ToString('N')+'.json');[IO.File]::WriteAllText($path,($Request|ConvertTo-Json -Depth 12 -Compress))
    $capture=Invoke-NativeTestProcess $inspectorPath @('--inspect-native-package',$path) -TimeoutMilliseconds 120000
    if($capture.Stderr){throw "Unexpected inspector stderr: $($capture.Stderr)"}
    [pscustomobject]@{Exit=$capture.Exit;Result=($capture.Stdout|ConvertFrom-Json)}
}
function Snapshot([string]$Root) {
    $files=[ordered]@{};foreach($file in Get-ChildItem -LiteralPath $Root -File -Recurse -Force|Sort-Object FullName){$files[[IO.Path]::GetRelativePath($Root,$file.FullName)]=@((Get-FileHash -LiteralPath $file.FullName).Hash,$file.LastWriteTimeUtc.Ticks)}
    $files|ConvertTo-Json -Depth 6 -Compress
}
try {
    $archive=Join-Path $run 'format fixture.zip'
    $fixture=Invoke-Publication @{CreateFixture=$true;ArchivePath=$archive;Executable=[IO.Path]::GetFullPath($NativeExecutable)}
    if($fixture.Exit -ne 0){throw 'Fixture creation failed'}
    $store=Join-Path $run 'versions with spaces';$stages=Join-Path $run 'source stages'
    $request=@{ArchivePath=$archive;ArchiveSha256=$fixture.Result.ArchiveSha256;StagingParent=$stages;PublishParent=$store}
    $first=Invoke-Publication $request
    if($first.Exit -ne 0 -or !$first.Result.Published -or $first.Result.Directory -cne (Join-Path $store $fixture.Result.BuildId) -or @($first.Result.Files).Count -ne 18){throw "Version publication failed: $($first.Result|ConvertTo-Json -Compress)"}
    foreach($file in $first.Result.Files){if((Get-FileHash -LiteralPath (Join-Path $first.Result.Directory $file.Path)).Hash -cne $file.Sha256){throw 'Published bytes differ from package'}}
    $before=Snapshot $store;$repeat=Invoke-Publication $request
    if($repeat.Exit -ne 0 -or $repeat.Result.Published -or $repeat.Result.FileIdentity -cne $first.Result.FileIdentity -or (Snapshot $store) -cne $before){throw 'Existing identical version was replaced or rewritten'}
    [IO.File]::AppendAllText((Join-Path $first.Result.Directory 'LICENSE'),'foreign installed edit');$changed=Snapshot $store
    $conflict=Invoke-Publication $request
    if($conflict.Exit -ne 1 -or $conflict.Result.Id -ne 'ModpackTools.Package.StageChanged' -or (Snapshot $store) -cne $changed){throw 'Changed existing version was overwritten or removed'}
    $foreignStore=Join-Path $run 'foreign retained versions';$foreignRequest=$request.Clone();$foreignRequest.PublishParent=$foreignStore;$foreignRequest.ChangePublishedFileAndCancel=$true
    $foreign=Invoke-Publication $foreignRequest
    if($foreign.Exit -ne 1 -or !$foreign.Result.RetainStage -or $foreign.Result.Id -ne 'ModpackTools.Package.PublicationStageRetained' -or ![IO.File]::ReadAllText((Join-Path $foreign.Result.Target 'LICENSE')).EndsWith('foreign publication edit') -or (Test-Path -LiteralPath (Join-Path $foreignStore $fixture.Result.BuildId))){throw 'Cancelled publication discarded foreign edits or activated a version'}
    $occupiedStore=Join-Path $run 'occupied versions';$occupiedRequest=$request.Clone();$occupiedRequest.PublishParent=$occupiedStore;$occupiedRequest.OccupyVersionTarget=$true
    $occupied=Invoke-Publication $occupiedRequest
    $occupiedTarget=Join-Path $occupiedStore $fixture.Result.BuildId
    if($occupied.Exit -eq 0 -or [IO.File]::ReadAllText((Join-Path $occupiedTarget 'foreign.txt')) -cne 'foreign occupied version' -or @(Get-ChildItem -LiteralPath $occupiedStore -Force).Count -ne 1){throw 'Target collision overwrote foreign contents or leaked a verified owned stage'}
    'PASS: content-addressed publication, identical reuse without rewrites, changed-version preservation, cancelled foreign-stage retention and atomic target collision'
} finally {
    $resolved=[IO.Path]::GetFullPath($run)
    if(!$resolved.StartsWith($temporary,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^modpacktools-version publication-[0-9a-f]{32}$'){throw 'Unsafe temporary cleanup path'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
