param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$PackwizExecutable)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$executablePath=[IO.Path]::GetFullPath($Executable)
$temporary=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$run=Join-Path $temporary ('modpacktools-reviewed doctor-'+[guid]::NewGuid().ToString('N'))
$names=@('PATH','APPDATA','LOCALAPPDATA','MODPACKTOOLS_CONFIG_HOME','MODPACKTOOLS_STATE_HOME','MODPACKTOOLS_PROJECT')
$previous=@{};foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name)}
function Snapshot([string]$Root) {
    $result=[ordered]@{}
    foreach($file in Get-ChildItem -LiteralPath $Root -File -Recurse -Force | Sort-Object FullName){$result[[IO.Path]::GetRelativePath($Root,$file.FullName)]=@((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash,$file.LastWriteTimeUtc.Ticks)}
    return ($result|ConvertTo-Json -Depth 10 -Compress)
}
function Invoke-Phase([string]$Scope,[string[]]$Tokens,[string]$Review='',[string]$Project='reviewed') {
    $request=@{schema_version=1;arguments=$Tokens;context=@{active_project=$Project};doctor_scope=$Scope}
    if($Review){$request.review_token=$Review}
    $path=Join-Path $run ('request-'+[guid]::NewGuid().ToString('N')+'.json')
    [IO.File]::WriteAllText($path,($request|ConvertTo-Json -Depth 16 -Compress))
    $capture=Invoke-NativeTestProcess $executablePath @('--request-path',$path) -TimeoutMilliseconds 60000
    if($capture.Stderr -ne ''){throw "Unexpected native stderr: $($capture.Stderr)"}
    return [pscustomobject]@{Exit=$capture.Exit;Result=($capture.Stdout|ConvertFrom-Json)}
}
try {
    $configuration=Join-Path $run 'configuration spaces';$catalog=Join-Path $run 'catalog';$project=Join-Path $catalog 'reviewed project'
    [void][IO.Directory]::CreateDirectory((Join-Path $project '.modpack'));[void][IO.Directory]::CreateDirectory($configuration)
    [IO.File]::WriteAllText((Join-Path $project '.modpack/project.psd1'),"@{SchemaVersion=1;Id='reviewed'}")
    [IO.File]::WriteAllText((Join-Path $project '.modpack/metadata.psd1'),"@{Categories=@{};Mods=@{};ResourcePacks=@{}}")
    [IO.File]::WriteAllText((Join-Path $project 'pack.toml'),"name=`"Reviewed fixture`"`nversion=`"1.0`"`n[index]`nfile=`"index.toml`"`nhash-format=`"sha256`"`n[versions]`nminecraft=`"1.21.1`"`nfabric=`"0.16.10`"`n")
    [IO.File]::WriteAllText((Join-Path $project 'index.toml'),"hash-format=`"sha256`"`nfiles=[]`n")
    [IO.File]::WriteAllText((Join-Path $project 'fixture.txt'),'stale index fixture')
    $configPath=Join-Path $configuration 'config.psd1'
    $configText="@{Root='$($catalog.Replace("'","''"))';PackwizPath='$($PackwizExecutable.Replace("'","''"))';Sentinel='preserved'}"
    [IO.File]::WriteAllText($configPath,$configText)
    $env:PATH='';$env:MODPACKTOOLS_CONFIG_HOME=$configuration;$env:MODPACKTOOLS_STATE_HOME=Join-Path $run 'state';$env:MODPACKTOOLS_PROJECT='ignored-process-project'
    $env:APPDATA=Join-Path $run 'Roaming';$env:LOCALAPPDATA=Join-Path $run 'Local'
    [void][IO.Directory]::CreateDirectory($env:APPDATA);[void][IO.Directory]::CreateDirectory($env:LOCALAPPDATA)
    $before=Snapshot $project
    $environment=Invoke-Phase environment @('doctor','--fix','--yes','--json') -Project missing-project
    if($environment.Exit -ne 0 -or $environment.Result.data.doctor.repair.applied -or @($environment.Result.data.doctor.checks).Count -ne 1 -or $environment.Result.data.doctor.checks[0].section -ne 'PACKWIZ' -or (Snapshot $project) -cne $before){throw 'Environment phase inspected/applied a project or changed source'}
    [IO.File]::WriteAllText($configPath,'invalid PSD1')
    $invalid=Invoke-Phase environment @('doctor','--fix','--yes','--json') -Review ('0'*64)
    if($invalid.Exit -ne 1 -or $invalid.Result.error.id -ne 'ModpackTools.Transaction.InvalidReview' -or [IO.File]::ReadAllText($configPath) -ne 'invalid PSD1'){throw 'Invalid environment review reached configuration/domain effects'}
    [IO.File]::WriteAllText($configPath,$configText)
    $preview=Invoke-Phase project @('doctor','--fix','--json')
    $token=$preview.Result.presentation.review_token
    if($preview.Exit -ne 0 -or $token -notmatch '^[0-9A-F]{64}$' -or $preview.Result.presentation.kind -ne 'doctor-repair' -or $preview.Result.data.doctor.repair.applied -or $preview.Result.data.doctor.repair.change_count -lt 1 -or (Snapshot $project) -cne $before){throw 'Project review did not return a nonmutating bound preview'}
    [IO.File]::WriteAllText($configPath,$configText+"`n# external change")
    $changed=Invoke-Phase project @('doctor','--fix','--yes','--json') -Review $token
    if($changed.Exit -ne 1 -or $changed.Result.error.id -ne 'ModpackTools.Transaction.ReviewChanged' -or (Snapshot $project) -cne $before -or ![IO.File]::ReadAllText($configPath).Contains('external change')){throw 'Changed configuration was not rejected before project publication'}
    [IO.File]::WriteAllText($configPath,$configText)
    $applied=Invoke-Phase project @('doctor','--fix','--yes','--json') -Review $token.ToLowerInvariant()
    if($applied.Exit -ne 0 -or !$applied.Result.data.doctor.repair.project_applied -or $applied.Result.data.doctor.repair.packwiz_installed -or $applied.Result.data.doctor.repair.configuration_updated -or [IO.File]::ReadAllText($configPath) -cne $configText){throw 'Reviewed application did not preserve phase/configuration truth'}
    $after=Snapshot $project
    $noop=Invoke-Phase project @('doctor','--fix','--json')
    if($noop.Exit -ne 0 -or $noop.Result.data.doctor.repair.change_count -ne 0 -or !$noop.Result.presentation.review_token){throw 'No-op repair omitted its review callback'}
    $rejected=Invoke-Phase project @('doctor','--fix','--yes','--json') -Review ('0'*64)
    if($rejected.Exit -ne 1 -or $rejected.Result.error.id -ne 'ModpackTools.Transaction.ReviewChanged' -or (Snapshot $project) -cne $after){throw 'No-op repair ignored a mismatched token'}
    [IO.File]::WriteAllText($configPath,"@{Root='$($catalog.Replace("'","''"))';PackwizPath='$((Join-Path $run 'missing.exe').Replace("'","''"))'}")
    $missingHash=(Get-FileHash -LiteralPath $configPath).Hash
    $missing=Invoke-Phase project @('doctor','--fix','--yes','--json')
    if($missing.Exit -ne 1 -or $missing.Result.error.id -ne 'ModpackTools.Dependency.PackwizNotFound' -or (Get-FileHash -LiteralPath $configPath).Hash -cne $missingHash -or (Test-Path -LiteralPath (Join-Path $configuration 'tools')) -or (Snapshot $project) -cne $after){throw 'Project phase installed/configured a missing dependency'}
    'PASS: independent environment, pre-effect token guard, config-bound project review/application, no-op validation and missing-tool isolation'
} finally {
    foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name])}
    $resolved=[IO.Path]::GetFullPath($run)
    if(!$resolved.StartsWith($temporary.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-reviewed doctor-*'){throw 'Unsafe temporary cleanup path'}
    if([IO.Directory]::Exists($resolved)){[IO.Directory]::Delete($resolved,$true)}
}
