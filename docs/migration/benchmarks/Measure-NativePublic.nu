module legacy {export use 'D:/Juegos/Minecraft/Herramientas/ModpackTools/Nushell/modpack.nu' *}
use legacy
module native {export use '../../../../../assessment/native-final/package/Nushell/modpack.nu' *}
use native

def canonical [value: any] {
    let kind = ($value | describe)
    if $kind =~ '^record' {
        $value | columns | sort | reduce --fold {} {|key, acc| $acc | insert $key (canonical ($value | get $key))}
    } else if $kind =~ '^list' or $kind =~ '^table' {
        $value | each {|item| canonical $item}
    } else {$value}
}

def main [run: path, --samples: int = 31] {
    if $samples < 31 {error make {msg: 'Public acceptance requires at least 31 samples'}}
    $env.MODPACKTOOLS_CORE_PATH = ($run | path join 'native-executable.txt' | open --raw | str trim)
    $env.MODPACKTOOLS_STATE_HOME = ($run | path join 'native-state')
    $env.MODPACKTOOLS_PROJECT = 'benchmark'
    $env.NO_COLOR = '1'
    $env.COLUMNS = '80'
    let workloads = [
        {name: version, fixture: local, args: ['--version' '--no-human']}
        {name: local20-inventory, fixture: local, args: ['content' 'list' '--no-human']}
        {name: managed100-inventory, fixture: managed, args: ['content' 'list' '--no-human']}
        {name: local20-human, fixture: local, args: ['content' 'list' '--ascii' '--color' 'never']}
    ]
    mut rows = []
    for workload in $workloads {
        mut first = {}
        for sample in 0..$samples {
            let engines = if ($sample mod 2) == 0 {['legacy' 'native']} else {['native' 'legacy']}
            for engine in $engines {
                $env.MODPACKTOOLS_CONFIG_HOME = ($run | path join $workload.fixture $engine)
                let start = date now
                let result = if $engine == 'legacy' {legacy modpack ...$workload.args} else {native modpack ...$workload.args}
                let elapsed = (((date now) - $start) / 1ms)
                if $workload.name == 'version' and $result != {version: '4.0.2'} {error make {msg: 'Version result differs'}}
                if $sample == 0 {$first = ($first | insert $engine (canonical $result))}
                if $sample == 0 and ($first | columns | length) == 2 and $first.legacy != $first.native {
                    $first | to json --raw | save ($run | path join $'mismatch-($workload.name).json')
                    error make {msg: $'Full public value differs: ($workload.name)'}
                }
                if $sample > 0 and (canonical $result) != ($first | get $engine) {error make {msg: $'Public value changed within timing series: ($workload.name) / ($engine)'}}
                $rows = ($rows | append {workload: $workload.name, engine: $engine, sample: $sample, milliseconds: $elapsed})
                # Retain incremental evidence even if a later workload fails.
                $rows | to json --raw | save --force ($run | path join 'nu-samples.json')
            }
        }
    }
    $rows | to json --raw
}
