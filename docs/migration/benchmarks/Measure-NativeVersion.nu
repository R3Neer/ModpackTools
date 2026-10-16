const ADAPTER = path self ../../../Nushell/modpack.nu
use $ADAPTER main

def main [executable: string, --samples: int = 21] {
    mut rows = []
    for native in [false true] {
        $env.MODPACKTOOLS_CORE_PATH = if $native { $executable } else { '' }
        for sample in 0..$samples {
            let start = date now
            let result = (modpack --version --no-human)
            let elapsed = (((date now) - $start) / 1ms)
            if $result.version != '4.0.2' { error make {msg: 'Invalid version'} }
            $rows = ($rows | append {adapter: 'nu', native: $native, sample: $sample, ms: $elapsed})
        }
    }
    $rows | to json
}
