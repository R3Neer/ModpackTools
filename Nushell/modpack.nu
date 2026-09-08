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

def invoke-bridge [request: string] {
    # stdout is redirected to a temporary file while stderr stays attached to the
    # terminal. File redirection preserves the external byte stream, so decode it
    # explicitly instead of relying on Nushell's implicit UTF-8 coercion.
    let capture_path = ($nu.temp-dir | path join $'modpacktools-((random uuid)).json')
    let stdout = try {
        $request | ^pwsh -NoLogo -NoProfile -File $BRIDGE o> $capture_path
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
        if ($capture_path | path exists) { rm --force $capture_path }
        error make { msg: $'Could not run the ModpackTools bridge: ($err.msg)' }
    }

    if ($capture_path | path exists) { rm --force $capture_path }
    $stdout
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

# PowerShell remains the canonical engine. The bridge always requests one JSON
# envelope internally. Normal Nu usage consumes that machine payload silently and
# leaves only R3CLI presentation visible; --no-human exposes the parsed Nu value.
export def --env --wrapped main [...args: string] {
    let machine_output = (wants-machine-output $args)
    let request = ({
        arguments: $args
        human_stderr_terminal: (is-terminal --stderr)
    } | to json)
    let raw = (invoke-bridge $request)
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

    if (field $envelope command '') == 'use' {
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
