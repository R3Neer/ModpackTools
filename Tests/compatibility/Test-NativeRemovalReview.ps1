param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$PackwizExecutable)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$temporaryBase=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$root=Join-Path $temporaryBase ('modpacktools-reviewed removal-'+[guid]::NewGuid().ToString('N'))
$names=@('MODPACKTOOLS_CONFIG_HOME','MODPACKTOOLS_STATE_HOME','MODPACKTOOLS_PROJECT','APPDATA','LOCALAPPDATA','PATH')
$previous=@{};foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
function Invoke-Request([string[]]$Tokens,[string]$ReviewToken=$null,[string]$ExpectedError=$null) {
    $request=@{schema_version=1;arguments=$Tokens}
    if($null -ne $ReviewToken -and $ReviewToken -ne ''){$request.review_token=$ReviewToken}
    $path=Join-Path $root 'request.json'
    [IO.File]::WriteAllText($path,($request|ConvertTo-Json -Depth 8 -Compress),[Text.UTF8Encoding]::new($false))
    $capture=Invoke-NativeTestProcess $Executable @('--request-path',$path) -TimeoutMilliseconds 120000
    if($capture.Stderr -cne ''){throw "Unexpected native stderr: $($capture.Stderr)"}
    $reply=$capture.Stdout|ConvertFrom-Json -Depth 32
    if($ExpectedError){if($capture.Exit -eq 0 -or $reply.ok -or $reply.error.id -cne $ExpectedError){throw "Expected $ExpectedError, received $($capture.Stdout)"}}
    elseif($capture.Exit -ne 0 -or -not $reply.ok){throw "Native invocation failed: $($capture.Stdout)"}
    return $reply
}
function Snapshot([string]$Directory) {
    @((Get-ChildItem -LiteralPath $Directory -Recurse -File | Sort-Object FullName)|ForEach-Object{[IO.Path]::GetRelativePath($Directory,$_.FullName)+' '+(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}) -join "`n"
}
try {
    foreach($directory in @($root,(Join-Path $root 'configuration'),(Join-Path $root 'projects'),(Join-Path $root 'roaming'),(Join-Path $root 'local'))){[void][IO.Directory]::CreateDirectory($directory)}
    $env:MODPACKTOOLS_CONFIG_HOME=Join-Path $root 'configuration';$env:MODPACKTOOLS_STATE_HOME=Join-Path $root 'state'
    $env:APPDATA=Join-Path $root 'roaming';$env:LOCALAPPDATA=Join-Path $root 'local';$env:PATH=''
    Remove-Item Env:MODPACKTOOLS_PROJECT -ErrorAction SilentlyContinue
    Invoke-Request @('config','set','root',(Join-Path $root 'projects'),'--json','--no-human')|Out-Null
    Invoke-Request @('config','set','packwiz',([IO.Path]::GetFullPath($PackwizExecutable)),'--json','--no-human')|Out-Null
    $roots=@{}
    foreach($id in @('review-apply','review-conflict')){
        Invoke-Request @('project','create',$id,'--name',('Review Fixture '+$id),'--minecraft','1.21.1','--loader','fabric','--loader-version','0.16.10','--json','--no-human')|Out-Null
        $pack=(Invoke-Request @('project','status','--project',$id,'--json','--no-human')).data.project.root
        $roots[$id]=$pack;[void][IO.Directory]::CreateDirectory((Join-Path $pack 'mods'))
        $archive=[IO.Compression.ZipArchive]::new([IO.File]::Create((Join-Path $pack 'mods/review.jar')),[IO.Compression.ZipArchiveMode]::Create)
        try{$writer=[IO.StreamWriter]::new($archive.CreateEntry('fabric.mod.json').Open(),[Text.UTF8Encoding]::new($false));try{$writer.Write('{"schemaVersion":1,"id":"review_fixture","version":"1.0","name":"Reviewed Local Mod","environment":"*"}')}finally{$writer.Dispose()}}finally{$archive.Dispose()}
    }
    $previewArgs=@('content','remove','review.jar','--project','review-apply','--json')
    $before=Snapshot $roots['review-apply']
    $preview=Invoke-Request $previewArgs
    $display=$preview.presentation
    if($display.kind -cne 'removal' -or $display.review_token -cnotmatch '^[0-9A-F]{64}$' -or $display.items.Count -ne 1 -or $display.items[0].name -cne 'Reviewed Local Mod' -or $display.items[0].reason -cne 'requested' -or -not $preview.data.transaction.preview){throw 'Reviewed removal display mismatch'}
    $repeat=Invoke-Request $previewArgs
    if($repeat.presentation.review_token -cne $display.review_token -or (Snapshot $roots['review-apply']) -cne $before){throw 'Preview token unstable or source changed'}
    $machine=Invoke-Request ($previewArgs+@('--no-human'))
    if('presentation' -in $machine.PSObject.Properties.Name -or ($machine.data|ConvertTo-Json -Depth 16 -Compress) -cne ($preview.data|ConvertTo-Json -Depth 16 -Compress)){throw 'Machine removal data changed'}
    $applied=Invoke-Request ($previewArgs+@('--yes')) $display.review_token.ToLowerInvariant()
    if(-not $applied.data.transaction.applied -or $applied.presentation.review_token -cne $display.review_token -or (Test-Path -LiteralPath (Join-Path $roots['review-apply'] 'mods/review.jar'))){throw 'Reviewed removal failed to apply the exact proposal'}
    $conflictArgs=@('content','remove','review.jar','--project','review-conflict','--json')
    $conflict=Invoke-Request $conflictArgs
    [IO.File]::WriteAllText((Join-Path $roots['review-conflict'] 'external note.txt'),'Preserve this external edit.')
    $external=Snapshot $roots['review-conflict']
    Invoke-Request ($conflictArgs+@('--yes')) $conflict.presentation.review_token 'ModpackTools.Transaction.ReviewChanged'|Out-Null
    if((Snapshot $roots['review-conflict']) -cne $external){throw 'Review rejection changed the external source'}
    # Request grammar and scope fail before attempting to read invalid config.
    [IO.File]::WriteAllText((Join-Path $env:MODPACKTOOLS_CONFIG_HOME 'config.psd1'),'not valid config')
    Invoke-Request @('--version','--json') ('A'*64) 'ModpackTools.Transaction.InvalidReview'|Out-Null
    Invoke-Request ($conflictArgs+@('--yes','--dry-run')) ('A'*64) 'ModpackTools.Transaction.InvalidReview'|Out-Null
    Invoke-Request ($conflictArgs+@('--yes')) ('G'*64) 'Core.InvalidRequest'|Out-Null
    'Native reviewed removal passed: stable proposal, exact apply, source conflict, unchanged machine data and request guards'
} finally {
    foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name],'Process')}
    $resolved=[IO.Path]::GetFullPath($root)
    if(-not $resolved.StartsWith($temporaryBase,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-reviewed removal-*'){throw 'Unsafe reviewed-removal fixture cleanup'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
