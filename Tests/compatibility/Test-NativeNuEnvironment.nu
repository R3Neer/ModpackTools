use ../../Nushell/modpack.nu

def main [] {
    let previous_path = $env.PATH
    let previous_project = $env.MODPACKTOOLS_PROJECT?
    let previous_conversions = $env.ENV_CONVERSIONS
    $env.PATH = []
    for test in [
        {value: 42, expected: '42'}
        {value: true, expected: 'true'}
        {value: 42.5, expected: '42.5'}
        {value: 2026-10-13T00:00:00+02:00, expected: '2026-10-13T00:00:00.000+02:00'}
        {value: ['one' 'two'], expected: null}
        {value: {one: 'two'}, expected: null}
    ] {
        $env.MODPACKTOOLS_PROJECT = $test.value
        if (modpack project current --no-human) != {active_project: $test.expected} { error make {msg: 'Native current bypassed Nu environment export semantics'} }
    }
    $env.ENV_CONVERSIONS = ($previous_conversions | upsert MODPACKTOOLS_PROJECT {
        from_string: {|value| $value}
        to_string: {|value| $'converted:($value)'}
    })
    $env.MODPACKTOOLS_PROJECT = 'alpha'
    if (modpack project current --no-human) != {active_project: 'converted:alpha'} { error make {msg: 'Custom string environment conversion was bypassed'} }
    $env.ENV_CONVERSIONS = ($previous_conversions | upsert MODPACKTOOLS_PROJECT {
        from_string: {|value| $value}
        to_string: {|value| if $value == 'raw-fixture' { 'fixture' } else { $value }}
    })
    $env.MODPACKTOOLS_PROJECT = 'raw-fixture'
    if (modpack project use ' ' --no-human) != {active_project: 'fixture'} or $env.MODPACKTOOLS_PROJECT != 'fixture' { error make {msg: 'Use ignored converted child environment or failed parent update'} }
    $env.ENV_CONVERSIONS = $previous_conversions
    $env.PATH = $previous_path
    if $previous_project == null { hide-env --ignore-errors MODPACKTOOLS_PROJECT } else { $env.MODPACKTOOLS_PROJECT = $previous_project }
    print 'Native Nu preserves primitive, date, omitted and custom environment conversions'
}
