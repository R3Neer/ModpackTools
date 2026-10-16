param([int]$Samples=31)
$ErrorActionPreference='Stop'
$executables=[ordered]@{
    rust=(Join-Path $PSScriptRoot 'rust-probe/target/release/diagnostic-probe.exe')
    go=(Join-Path $PSScriptRoot 'startup-go.exe')
    dotnet_aot=(Join-Path $PSScriptRoot 'dotnet-aot/Startup.exe')
}
$rows=[Collections.Generic.List[object]]::new()
for($i=0;$i -le $Samples;$i++) {
    foreach($language in $executables.Keys) {
        $start=[Diagnostics.ProcessStartInfo]::new($executables[$language])
        $start.UseShellExecute=$false;$start.CreateNoWindow=$true
        $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
        $sw=[Diagnostics.Stopwatch]::StartNew()
        $process=[Diagnostics.Process]::Start($start)
        try {
            $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
            $process.WaitForExit();$sw.Stop()
            if($process.ExitCode -ne 0) { throw $stderr.Result }
            $payload=$stdout.Result|ConvertFrom-Json
            if(-not $payload.ok -or $payload.benchmark -ne 'startup-only') {throw 'Invalid fixture output'}
            $peak=$null;try{$peak=$process.PeakWorkingSet64;if($peak -le 0){$peak=$null}}catch{}
            $rows.Add([pscustomobject]@{language=$language;sample=$i;wall_ms=$sw.Elapsed.TotalMilliseconds;peak_bytes=$peak})
        }finally{$process.Dispose()}
    }
}
$summary=foreach($language in $executables.Keys) {
    $selected=@($rows|Where-Object {$_.language -eq $language -and $_.sample -gt 0}|Sort-Object wall_ms)
    $peakValues=@($selected|Where-Object {$null -ne $_.peak_bytes})
    [pscustomobject]@{language=$language;n=$selected.Count;first_ms=($rows|Where-Object {$_.language -eq $language -and $_.sample -eq 0}).wall_ms;median_ms=$selected[[int][math]::Floor($selected.Count/2)].wall_ms;p95_ms=$selected[[int][math]::Ceiling($selected.Count*0.95)-1].wall_ms;max_peak_mib=$(if($peakValues.Count){($peakValues|Measure-Object peak_bytes -Maximum).Maximum/1MB}else{$null})}
}
$result=[ordered]@{utc=[datetime]::UtcNow.ToString('o');samples=$Samples;summary=@($summary);raw=@($rows);note='Only a fixed JSON write; no product implementation. C# is Native AOT Release win-x64. Interleaved warm OS-cache samples; not evidence of resolver/inventory performance. Post-exit peak memory unavailable is recorded as null.'}
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'startup-results.json'),($result|ConvertTo-Json -Depth 8))
$summary|ConvertTo-Json
