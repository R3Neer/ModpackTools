param([Parameter(Mandatory)][string]$Inspector,[switch]$Online)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$inspectorPath=[IO.Path]::GetFullPath($Inspector);$temporary=[IO.Path]::GetFullPath([IO.Path]::GetTempPath());$run=Join-Path $temporary ('modpacktools-native release-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($run)
function Invoke-Case([string]$Name,[string]$Metadata,[hashtable]$Overrides=@{}) {
    $metadataPath=Join-Path $run ($Name+'.metadata.json');[IO.File]::WriteAllText($metadataPath,$Metadata)
    $request=@{MetadataPath=$metadataPath};foreach($key in $Overrides.Keys){$request[$key]=$Overrides[$key]}
    $requestPath=Join-Path $run ($Name+'.request.json');[IO.File]::WriteAllText($requestPath,($request|ConvertTo-Json -Compress))
    $capture=Invoke-NativeTestProcess $inspectorPath @('--inspect-native-release',$requestPath) -TimeoutMilliseconds 30000
    if($capture.Stderr){throw "Unexpected release stderr: $($capture.Stderr)"}
    [pscustomobject]@{Exit=$capture.Exit;Result=($capture.Stdout|ConvertFrom-Json)}
}
try {
    $release=[ordered]@{tag_name='v4.0.3';draft=$false;prerelease=$false;html_url='https://github.com/R3Neer/ModpackTools/releases/tag/v4.0.3';assets=@(@{name='ModpackTools-Native-win-x64-4.0.3.zip';browser_download_url='https://github.com/R3Neer/ModpackTools/releases/download/v4.0.3/ModpackTools-Native-win-x64-4.0.3.zip';digest=('sha256:'+('ab'*32));size=123456;state='uploaded'})}
    $goodJson=$release|ConvertTo-Json -Depth 6 -Compress;$native=Invoke-Case native $goodJson
    if($native.Exit -ne 0 -or !$native.Result.native_available -or $native.Result.version -cne '4.0.3' -or $native.Result.sha256 -cne ('AB'*32) -or $native.Result.length -ne 123456){throw 'Native official release projection failed'}
    $release.assets[0].name='ModpackTools-4.0.3.zip';$legacy=Invoke-Case legacy ($release|ConvertTo-Json -Depth 6 -Compress)
    if($legacy.Exit -ne 0 -or $legacy.Result.native_available -or $null -ne $legacy.Result.asset_url -or $legacy.Result.unavailable_reason -notmatch 'legacy PowerShell'){throw 'Legacy ZIP was accepted as a native package'}
    $release.assets[0].name='ModpackTools-Native-win-x64-4.0.3.zip';$release.assets[0].browser_download_url='https://example.invalid/ModpackTools-Native-win-x64-4.0.3.zip'
    $foreign=Invoke-Case foreign ($release|ConvertTo-Json -Depth 6 -Compress)
    if($foreign.Exit -ne 1 -or $foreign.Result.id -cne 'ModpackTools.SelfUpdate.InvalidAsset'){throw 'Foreign native asset URL was accepted'}
    $duplicate=Invoke-Case duplicate ($goodJson -replace '"tag_name":','"draft":true,"tag_name":')
    if($duplicate.Exit -ne 1 -or $duplicate.Result.id -cne 'ModpackTools.SelfUpdate.InvalidRelease'){throw 'Duplicate release field was accepted'}
    $oversize=Invoke-Case oversize '{}' @{Oversize=$true}
    if($oversize.Exit -ne 1 -or $oversize.Result.id -cne 'ModpackTools.SelfUpdate.MetadataLimit'){throw 'Oversized metadata was accepted'}
    $redirect=Invoke-Case redirect $goodJson @{Status=302}
    if($redirect.Exit -ne 1 -or $redirect.Result.id -cne 'ModpackTools.SelfUpdate.InvalidRelease'){throw 'Release endpoint redirect was trusted'}
    $cancel=Invoke-Case cancel $goodJson @{CancelBefore=$true}
    if($cancel.Exit -ne 1 -or $cancel.Result.id -cne 'ModpackTools.Core.OperationCancelled'){throw 'Release cancellation lost its typed outcome'}
    if($Online){$actual=Invoke-Case online '{}' @{Online=$true};if($actual.Exit -ne 0){throw "Official live read failed: $($actual.Result|ConvertTo-Json -Compress)"};$actual.Result|ConvertTo-Json -Compress}
    'PASS: bounded official release selection, native-only asset identity, trusted URL/digest, duplicate metadata rejection, redirect refusal and cancellation'
} finally {
    $resolved=[IO.Path]::GetFullPath($run)
    if(!$resolved.StartsWith($temporary,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^modpacktools-native release-[0-9a-f]{32}$'){throw 'Unsafe temporary cleanup path'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
