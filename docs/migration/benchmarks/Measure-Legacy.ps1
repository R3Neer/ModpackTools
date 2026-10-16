param(
    [string]$Repository = 'C:\Users\Usuario\Desktop\ReorganizacionPC\work\ModpackTools',
    [string]$ProjectRoot = 'D:\Minecraft\VanillaPlus-26.2',
    [string]$NuPath = 'D:\Tools\nushell-miw\target\x86_64-pc-windows-gnu\release\nu.exe',
    [int]$Samples = 21
)
$ErrorActionPreference = 'Stop'
$runRoot = Join-Path $PSScriptRoot ('run-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($runRoot)
$configRoot = Join-Path $runRoot 'config'
[void][IO.Directory]::CreateDirectory($configRoot)
$escape = { param($v) "'" + $v.Replace("'", "''") + "'" }
$repoLiteral = & $escape (Join-Path $Repository 'ModpackTools.psd1')
$projectLiteral = & $escape $ProjectRoot
$configLiteral = & $escape $configRoot
$parentLiteral = & $escape (Split-Path -Parent $ProjectRoot)
[IO.File]::WriteAllText((Join-Path $configRoot 'config.psd1'), "@{ Root = $parentLiteral }")
$pwshPath = (Get-Command pwsh).Source
$base = "`$env:MODPACKTOOLS_CONFIG_HOME=$configLiteral; Import-Module $repoLiteral -Force; "
$requestVersion = Join-Path $runRoot 'version.json'
$requestInventory = Join-Path $runRoot 'inventory.json'
[IO.File]::WriteAllText($requestVersion, '{"arguments":["--version","--no-human"],"human_stderr_terminal":false}')
[IO.File]::WriteAllText($requestInventory, '{"arguments":["content","list","--project","vp26","--no-human"],"human_stderr_terminal":false}')
$previousConfig = $env:MODPACKTOOLS_CONFIG_HOME
$env:MODPACKTOOLS_CONFIG_HOME = $configRoot
$rows = [Collections.Generic.List[object]]::new()
function Run-Process {
    param([string]$Name, [string]$Executable, [string[]]$Arguments, [int]$Index)
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $watch.Stop()
        if ($process.ExitCode -ne 0) { throw "$Name failed: $($stderr.Result)" }
        if ($Name -notin @('startup','import','phases')) {
            $value = $stdout.Result | ConvertFrom-Json
            if ($Name -like 'nu-*') {
                if ($Name -eq 'nu-version' -and -not $value.version) { throw 'Missing Nu version' }
                if ($Name -eq 'nu-inventory' -and @($value).Count -eq 0) { throw 'Empty Nu inventory' }
            } elseif (-not $value.ok) { throw "Invalid envelope: $($stdout.Result)" }
        }
        $peak = $null
        try { $peak = $process.PeakWorkingSet64; if($peak -le 0){$peak=$null} } catch { }
        $row = [ordered]@{ name=$Name; sample=$Index; wall_ms=$watch.Elapsed.TotalMilliseconds; peak_working_set_bytes=$peak }
        if ($Name -eq 'phases') { $row.phases = $stdout.Result | ConvertFrom-Json }
        $rows.Add([pscustomobject]$row)
    } finally { $process.Dispose() }
}
function Run-Code { param($Name,$Code,$Index)
    Run-Process $Name $pwshPath @('-NoLogo','-NoProfile','-NonInteractive','-EncodedCommand',[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Code))) $Index
}
try {
    foreach ($case in @('startup','import','version','bridge-version','bridge-inventory','nu-version','nu-inventory')) {
        Write-Host "Measuring $case ($Samples measured runs + first run)"
        for ($i=0; $i -le $Samples; $i++) {
            switch ($case) {
                startup { Run-Code $case 'exit 0' $i }
                import { Run-Code $case $base $i }
                version { Run-Code $case ($base + 'modpack --version --json --no-human') $i }
                bridge-version { Run-Process $case $pwshPath @('-NoLogo','-NoProfile','-NonInteractive','-File',(Join-Path $Repository 'Nushell/Invoke-ModpackBridge.ps1'),'-RequestPath',$requestVersion) $i }
                bridge-inventory { Run-Process $case $pwshPath @('-NoLogo','-NoProfile','-NonInteractive','-File',(Join-Path $Repository 'Nushell/Invoke-ModpackBridge.ps1'),'-RequestPath',$requestInventory) $i }
                nu-version { $adapter=(Join-Path $Repository 'Nushell/modpack.nu').Replace('\','/'); Run-Process $case $NuPath @('--no-config-file','-c',"use '$adapter' main; modpack --version --no-human | to json") $i }
                nu-inventory { $adapter=(Join-Path $Repository 'Nushell/modpack.nu').Replace('\','/'); Run-Process $case $NuPath @('--no-config-file','-c',"use '$adapter' main; modpack content list --project vp26 --no-human | to json") $i }
            }
        }
    }
    $phaseCode = $base + @"
`$m=Get-Module ModpackTools
& `$m { param(`$root)
  `$p=Read-ModpackProject `$root
  `$results=[Collections.Generic.List[object]]::new()
  foreach (`$case in @('inventory','tree-hash','health-cache-miss','health-cache-hit')) {
    for (`$i=0; `$i -lt 7; `$i++) {
      if (`$case -eq 'health-cache-miss') { `$script:ConfigHomeOverride=Join-Path $configLiteral ('phase-' + [guid]::NewGuid().ToString('N')) }
      `$sw=[Diagnostics.Stopwatch]::StartNew()
      switch (`$case) {
        inventory { `$value=Get-ModpackInventory `$p }
        tree-hash { `$value=Get-MpTreeState `$root }
        default { `$value=Get-MpProjectHealth `$p }
      }
      `$sw.Stop()
      `$results.Add([pscustomobject]@{name=`$case;sample=`$i;wall_ms=`$sw.Elapsed.TotalMilliseconds})
    }
  }
  `$results | ConvertTo-Json -Depth 5 -Compress
} $projectLiteral
"@
    Run-Code 'phases' $phaseCode 0
    $rawPath = Join-Path $runRoot 'raw.json'
    [IO.File]::WriteAllText($rawPath, ($rows | ConvertTo-Json -Depth 10))
    $summary = foreach ($name in @($rows.name | Select-Object -Unique | Where-Object {$_ -ne 'phases'})) {
        $selected=@($rows | Where-Object { $_.name -eq $name -and $_.sample -gt 0 } | Sort-Object wall_ms)
        $peakValues=@($selected|Where-Object {$null -ne $_.peak_working_set_bytes})
        [pscustomobject]@{name=$name;n=$selected.Count;first_ms=($rows|Where-Object {$_.name -eq $name -and $_.sample -eq 0}).wall_ms;median_ms=$selected[[int][math]::Floor($selected.Count/2)].wall_ms;p95_ms=$selected[[int][math]::Ceiling($selected.Count*0.95)-1].wall_ms;max_peak_mib=$(if($peakValues.Count){($peakValues|Measure-Object peak_working_set_bytes -Maximum).Maximum/1MB}else{$null})}
    }
    $phaseRows=@(($rows|Where-Object name -eq phases).phases)
    $phaseSummary=foreach($name in @($phaseRows.name|Select-Object -Unique)) {
        $selected=@($phaseRows|Where-Object {$_.name -eq $name -and $_.sample -gt 0}|Sort-Object wall_ms)
        [pscustomobject]@{name=$name;n=$selected.Count;first_ms=($phaseRows|Where-Object {$_.name -eq $name -and $_.sample -eq 0}).wall_ms;median_ms=$selected[[int][math]::Floor($selected.Count/2)].wall_ms;p95_ms=$selected[[int][math]::Ceiling($selected.Count*0.95)-1].wall_ms}
    }
    $result=[ordered]@{utc=[datetime]::UtcNow.ToString('o');repository=$Repository;project=$ProjectRoot;pwsh=$PSVersionTable.PSVersion.ToString();nu=(& $NuPath --version);samples=$Samples;process_summary=@($summary);phase_summary=@($phaseSummary);raw_path=$rawPath;note='Sequential samples; OS cache not evicted. First run is not guaranteed disk-cold. Peak memory queried after exit may be unavailable and is null, never interpreted as zero. No network verification.'}
    [IO.File]::WriteAllText((Join-Path $runRoot 'summary.json'),($result|ConvertTo-Json -Depth 8))
    $result|ConvertTo-Json -Depth 8
} finally { $env:MODPACKTOOLS_CONFIG_HOME=$previousConfig }
