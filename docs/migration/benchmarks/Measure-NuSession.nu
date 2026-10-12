const ADAPTER = path self ../work/ModpackTools/Nushell/modpack.nu
use $ADAPTER main

def main [config_home: string, --samples: int = 21] {
    $env.MODPACKTOOLS_CONFIG_HOME = $config_home
    mut rows = []
    for operation in ['version' 'inventory'] {
        for sample in 0..$samples {
            let start = (date now)
            let value = if $operation == 'version' {
                modpack --version --no-human
            } else {
                modpack content list --project vp26 --no-human
            }
            let elapsed = (((date now) - $start) / 1ms)
            if $operation == 'version' and ($value.version? | default '') == '' {
                error make {msg: 'Missing version'}
            }
            if $operation == 'inventory' and ($value | length) == 0 {
                error make {msg: 'Empty inventory'}
            }
            $rows = ($rows | append {operation: $operation, sample: $sample, wall_ms: $elapsed})
        }
    }
    {samples: $samples, rows: $rows, note: 'Measured inside one Nu process; host Nu startup excluded. Config/OS caches already warmed by prior measurements. No network verification.'} | to json
}
