const MODULE_DIR = path self .
const BRIDGE = ($MODULE_DIR | path join 'Invoke-ModpackBridge.ps1')

const PROJECT_COMMANDS = [
    'status'
    'inventory'
    'search'
    'add'
    'remove'
    'classify'
    'resource'
    'side'
    'versions'
    'update'
    'build'
    'diff'
    'doctor'
    'pin'
    'unpin'
]

const POSITIONAL_PROJECT_COMMANDS = [
    'status'
    'inventory'
    'build'
    'diff'
]

def field [value: any, name: string, default_value: any = null] {
    if (($value | describe) !~ '^record') { return $default_value }
    let result = ($value | get --optional $name)
    if $result == null { $default_value } else { $result }
}

def data-field [data: any, name: string] {
    field $data $name
}

def find-command [args: list<string>] {
    mut skip_next = false
    for entry in ($args | enumerate) {
        let token = $entry.item
        if $skip_next {
            $skip_next = false
            continue
        }
        if $token == '--colour' {
            $skip_next = true
            continue
        }
        if (
            $token == '--ascii'
            or $token == '--json'
            or $token == '--no-human'
            or ($token | str starts-with '--colour=')
        ) {
            continue
        }
        return { index: $entry.index, name: ($token | str lowercase) }
    }
    null
}

def has-explicit-project-option [args: list<string>] {
    $args | any {|token|
        $token == '--project' or ($token | str starts-with '--project=')
    }
}

def has-positional-project [args: list<string>, command_index: int, command: string] {
    if not ($command in $POSITIONAL_PROJECT_COMMANDS) { return false }

    let value_options = if $command == 'inventory' {
        ['--project' '--type' '--category' '--side' '--source' '--state' '--search']
    } else {
        ['--project']
    }

    mut skip_next = false
    for token in ($args | skip ($command_index + 1)) {
        if $skip_next {
            $skip_next = false
            continue
        }

        if $token == '--colour' {
            $skip_next = true
            continue
        }
        if $token in $value_options {
            $skip_next = true
            continue
        }
        if ($token | str starts-with '--') { continue }
        return true
    }

    false
}

def with-active-project [args: list<string>] {
    let selected = ($env.MODPACKTOOLS_PROJECT? | default '' | into string)
    if $selected == '' { return $args }

    let command_info = (find-command $args)
    if $command_info == null { return $args }

    let command = $command_info.name
    if not ($command in $PROJECT_COMMANDS) { return $args }
    if ($args | any {|token| $token == '--help' }) { return $args }
    if (has-explicit-project-option $args) { return $args }
    if (has-positional-project $args $command_info.index $command) { return $args }

    $args | append '--project' | append $selected
}

def invoke-bridge [request: string] {
    # stdout is redirected to a temporary file so stderr remains attached to the
    # terminal. This preserves live R3CLI output and gives us the actual child exit
    # code without relying on LAST_EXIT_CODE escaping a Nushell subexpression.
    let capture_path = ($nu.temp-dir | path join $'modpacktools-((random uuid)).json')
    let result = try {
        $request | ^pwsh -NoLogo -NoProfile -File $BRIDGE o> $capture_path
        let exit_code = $env.LAST_EXIT_CODE
        let stdout = if ($capture_path | path exists) {
            open --raw $capture_path
        } else {
            ''
        }
        { stdout: $stdout, exit_code: $exit_code }
    } catch {|err|
        if ($capture_path | path exists) { rm --force $capture_path }
        error make { msg: $'Could not run the ModpackTools bridge: ($err.msg)' }
    }

    if ($capture_path | path exists) { rm --force $capture_path }
    $result
}

def unwrap-result [envelope: record] {
    let command = (field $envelope command '')
    let data = (field $envelope data {})

    match $command {
        '--version' => {
            let version = (data-field $data version)
            if $version == null { $data } else { $version }
        }
        'list' => {
            let projects = (data-field $data projects)
            if $projects == null { $data } else { field $projects items [] }
        }
        'inventory' => {
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
        'classify' => {
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

# PowerShell remains the canonical engine. The bridge reserves stdout for one JSON envelope;
# R3CLI human output continues on stderr and therefore never contaminates the Nu pipeline.
export def --env --wrapped main [...args: string] {
    let invocation_args = (with-active-project $args)
    let request = ({ arguments: $invocation_args } | to json)
    let completed = (invoke-bridge $request)
    let exit_code = $completed.exit_code
    let text = ($completed.stdout | into string | str trim)

    if $text == '' {
        error make { msg: $'ModpackTools bridge returned no JSON data; exit code ($exit_code).' }
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

    if $exit_code != 0 {
        error make { msg: $'ModpackTools exited with code ($exit_code) after returning a success envelope.' }
    }

    if (field $envelope command '') == 'use' {
        let use_args = (field $envelope arguments [])
        if ($use_args | is-empty) {
            return { active_project: ($env.MODPACKTOOLS_PROJECT? | default null) }
        }
        let project = ($use_args | first)
        $env.MODPACKTOOLS_PROJECT = $project
        return { active_project: $project }
    }

    unwrap-result $envelope
}
