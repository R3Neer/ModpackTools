const MODULE_DIR = path self .
const BRIDGE = ($MODULE_DIR | path join 'Invoke-ModpackBridge.ps1')

def field [value: any, name: string, default_value: any = null] {
    if (($value | describe) !~ '^record') { return $default_value }
    let result = ($value | get --optional $name)
    if $result == null { $default_value } else { $result }
}

def data-field [data: any, name: string] {
    field $data $name
}

def wants-machine-output [args: list<string>] {
    $args | any {|token| $token == '--no-human' }
}

def validate-nu-option-style [args: list<string>] {
    mut parse_options = true

    for token in $args {
        if not $parse_options {
            continue
        }

        if $token == '--' {
            $parse_options = false
            continue
        }

        if ($token | str starts-with '--') {
            if $token !~ '^--[a-z][a-z0-9-]*(=.*)?$' {
                error make {
                    msg: $"Nushell options must use lowercase '--long-option' or one-letter '-s' syntax. Option '($token)' is not valid Nushell style."
                }
            }
            continue
        }

        if ($token =~ '^-[A-Za-z]') and ($token != '-V') and ($token !~ '^-[a-z]$') {
            error make {
                msg: $"Nushell options must use lowercase '--long-option' or one-letter '-s' syntax. PowerShell-style option '($token)' is not supported by the Nu adapter."
            }
        }
    }
}

def invoke-bridge [request: string] {
    # Keep stdin attached to the terminal so interactive confirmations inside the
    # PowerShell engine can wait for an answer. The request and stdout use separate
    # temporary files; explicit decoding preserves the external UTF-8 byte stream.
    let request_path = ($nu.temp-dir | path join $'modpacktools-((random uuid)).request.json')
    let capture_path = ($nu.temp-dir | path join $'modpacktools-((random uuid)).json')
    $request | save --force $request_path
    let stdout = try {
        ^pwsh -NoLogo -NoProfile -File $BRIDGE -RequestPath $request_path o> $capture_path
        if ($capture_path | path exists) {
            let captured = (open --raw $capture_path)
            if (($captured | describe) == 'binary') {
                $captured | decode utf-8
            } else {
                $captured
            }
        } else {
            ''
        }
    } catch {|err|
        if ($request_path | path exists) { rm --force $request_path }
        if ($capture_path | path exists) { rm --force $capture_path }
        error make { msg: $'Could not run the ModpackTools bridge: ($err.msg)' }
    }

    if ($request_path | path exists) { rm --force $request_path }
    if ($capture_path | path exists) { rm --force $capture_path }
    $stdout
}

def owns-native-command [args: list<string>] {
    let executable = ($env.MODPACKTOOLS_CORE_PATH? | default '')
    if ($executable == '') or not (wants-machine-output $args) { return false }
    let command = ($args | get --optional 0 | default '')
    if $command in ['--version' '-V'] { return true }
    let operation = ($args | get --optional 1 | default '')
    (($command | str lowercase) == 'project') and (($operation | str lowercase) == 'list') and not ($args | any {|token| $token in ['--help' '-h'] })
}

def validate-string-fields [value: any, names: list<string>, nullable: bool = false] {
    if (($value | describe) !~ '^record') {
        error make { msg: 'ModpackTools native executable returned an invalid record.' }
    }
    for name in $names {
        if not ($name in ($value | columns)) {
            error make { msg: $'ModpackTools native executable omitted field ($name).' }
        }
        let item = ($value | get $name)
        if ($item != null or not $nullable) and (($item | describe) != 'string') {
            error make { msg: $'ModpackTools native executable returned an invalid field ($name).' }
        }
    }
}

def invoke-native-command [args: list<string>] {
    let executable = $env.MODPACKTOOLS_CORE_PATH
    if not ($executable | path exists) {
        error make { msg: 'The configured ModpackTools native executable does not exist.' }
    }
    let native_args = if '--json' in $args { $args } else { $args | append '--json' }
    let request_path = ($nu.temp-dir | path join $'modpacktools-((random uuid)).native.json')
    {schema_version: 1, arguments: $native_args} | to json | save --force $request_path
    let capture = try {
        ^$executable --request-path $request_path | complete
    } catch {|err|
        rm --force $request_path
        error make { msg: $'Could not run the ModpackTools native executable: ($err.msg)' }
    }
    rm --force $request_path
    if $capture.stderr != '' {
        error make { msg: $'ModpackTools native transport failed: ($capture.stderr)' }
    }
    let result = try { $capture.stdout | from json } catch {
        error make { msg: 'ModpackTools native executable returned invalid JSON.' }
    }
    if ((field $result schema_version 0 | describe) != 'int') or (field $result schema_version 0) != 1 {
        error make { msg: 'ModpackTools native executable returned an unsupported result schema.' }
    }
    if (($result | describe) !~ '^record') or ((field $result ok null | describe) != 'bool') or ((field $result command null | describe) != 'string') or ((field $result arguments null | describe) !~ '^list') {
        error make { msg: 'ModpackTools native executable returned an invalid result envelope.' }
    }
    if ($result.arguments | any {|token| ($token | describe) != 'string' }) {
        error make { msg: 'ModpackTools native executable returned invalid argument types.' }
    }
    if (field $result ok false) {
        if 'error' in ($result | columns) {
            error make { msg: 'ModpackTools native executable returned contradictory success fields.' }
        }
        if ($args | get 0) in ['--version' '-V'] {
            if $result.command != '--version' or ((field (field (field $result data {}) version {}) version null | describe) != 'string') {
                error make { msg: 'ModpackTools native executable returned an invalid version result.' }
            }
        } else {
            let projects = (field (field $result data {}) projects {})
            validate-string-fields $projects ['root']
            if $result.command != $args.0 or ($result.arguments | length) != 1 or $result.arguments.0 != $args.1 or ((field $projects items null | describe) !~ '^(list|table)') {
                error make { msg: 'ModpackTools native executable returned an invalid project catalog.' }
            }
            for item in $projects.items {
                validate-string-fields $item ['id' 'root' 'output_name']
                validate-string-fields $item ['name' 'version' 'minecraft' 'loader' 'loader_version'] true
            }
        }
    } else {
        if 'data' in ($result | columns) {
            error make { msg: 'ModpackTools native executable returned contradictory failure fields.' }
        }
        let failure = (field $result error {})
        if (($failure | describe) !~ '^record') or not ('target' in ($failure | columns)) or ((field $failure id null | describe) != 'string') or ((field $failure message null | describe) != 'string') or ((field $failure category null | describe) != 'string') {
            error make { msg: 'ModpackTools native executable returned an invalid error result.' }
        }
    }
    let valid_exit = if (field $result ok false) { $capture.exit_code == 0 } else { $capture.exit_code in [1 2] }
    if not $valid_exit {
        error make { msg: 'ModpackTools native exit status disagrees with its result.' }
    }
    $capture.stdout
}

def unwrap-result [envelope: record] {
    let command = (field $envelope command '')
    let arguments = (field $envelope arguments [])
    let operation = ($arguments | get --optional 0 | default '')
    let data = (field $envelope data {})

    match $command {
        '--version' => {
            let version = (data-field $data version)
            if $version == null { $data } else { $version }
        }
        'project' => {
            if $operation == 'list' {
                let projects = (data-field $data projects)
                if $projects == null { $data } else { field $projects items [] }
            } else { $data }
        }
        'content' => {
            match $operation {
                'list' => {
                    let inventory = (data-field $data inventory)
                    if $inventory == null { $data } else { field $inventory items [] }
                }
                'search' => {
                    let search = (data-field $data search)
                    if $search == null { $data } else { field $search results [] }
                }
                'versions' => {
                    let versions = (data-field $data versions)
                    if $versions == null { $data } else { field $versions items [] }
                }
                _ => {
                    let transaction = (data-field $data transaction)
                    if $transaction == null { $data } else { $transaction }
                }
            }
        }
        'category' => {
            let categories = (data-field $data categories)
            if $categories != null { field $categories items [] } else {
                let transaction = (data-field $data transaction)
                if $transaction == null { $data } else { $transaction }
            }
        }
        'doctor' => {
            let doctor = (data-field $data doctor)
            if $doctor == null { $data } else { $doctor }
        }
        'diff' => {
            let diff = (data-field $data diff)
            if $diff == null { $data } else { $diff }
        }
        'build' => {
            let build = (data-field $data build)
            if $build == null { $data } else { $build }
        }
        _ => {
            let transaction = (data-field $data transaction)
            if $transaction == null { $data } else { $transaction }
        }
    }
}

# Unmigrated commands use the PowerShell engine. The bridge always requests one JSON
# envelope internally. Normal Nu usage consumes that machine payload silently and
# leaves only R3CLI presentation visible; --no-human exposes the parsed Nu value.
export def --env --wrapped main [...args: string] {
    validate-nu-option-style $args
    let machine_output = (wants-machine-output $args)
    let request = ({
        arguments: $args
        human_stderr_terminal: (is-terminal --stderr)
    } | to json)
    let raw = if (owns-native-command $args) { invoke-native-command $args } else { invoke-bridge $request }
    let text = ($raw | str trim)

    if $text == '' {
        error make { msg: 'ModpackTools bridge returned no JSON data.' }
    }

    let envelope = try {
        $text | from json
    } catch {|err|
        error make { msg: $'ModpackTools returned invalid JSON: ($err.msg)' }
    }

    if not (field $envelope ok false) {
        let failure = (field $envelope error {})
        let message = (field $failure message 'ModpackTools failed.')
        let id = (field $failure id '')
        let category = (field $failure category '')
        let suffix = ([
            (if $id == '' { null } else { $id })
            (if $category == '' { null } else { $category })
        ] | compact | str join ' · ')
        error make { msg: (if $suffix == '' { $message } else { $'($message) [($suffix)]' }) }
    }

    if ((field $envelope command '') == 'project') and (($envelope.arguments | get --optional 0 | default '') == 'use') {
        let use_args = (field $envelope arguments [])
        if not ($use_args | any {|token| $token == '--help' }) {
            let active_project = (data-field (field $envelope data {}) active_project)
            if $active_project != null {
                $env.MODPACKTOOLS_PROJECT = ($active_project | into string)
            }
        }

        if $machine_output {
            return (unwrap-result $envelope)
        }
        return
    }

    if $machine_output {
        return (unwrap-result $envelope)
    }
}
