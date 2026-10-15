use ../presentation/r3cli
use ./Native-Views.nu

def kind [status: string] {
    match $status {pass => 'success', fail => 'error', warn => 'warning', _ => 'info'}
}

# Invoked lazily in a separate Nu process: machine-only commands neither import
# presentation code nor read theme/resources. Every human line goes to stderr.
def main [request_path: string] {
    if (ls $request_path | first | get size | into int) > 67108864 {error make {msg: 'Presentation request exceeds 64 MiB.'}}
    let request = (open --raw $request_path | from json)
    mut extension = {client: '#748FFC', host: '#BE70FF', local: '#FF91CD'}
    if ($request.theme_path | path exists) {
        if (ls $request.theme_path | first | get size | into int) > 262144 {error make {msg: 'Theme exceeds its 256 KiB quota.'}}
        let theme = (open $request.theme_path)
        let declared = ($theme | get --optional colours | default ($theme | get --optional colors | default {}))
        if ($declared | describe) !~ '^record' {error make {msg: 'Theme colours must be a table.'}}
        $extension = ($extension | merge $declared)
    }
    let sink = {|text, _stream| print --stderr $text}
    let ui = (r3cli console --colour $request.colour --ascii=$request.ascii --width $request.width --theme-extension $extension --is-terminal $request.terminal --sink $sink)
    let notice = ($request | get --optional notice | default '')
    if $notice != '' {r3cli status $ui info $notice; return}
    if 'help' in ($request.envelope | get --optional presentation | default {} | columns) {
        r3cli help $ui $request.envelope.presentation.help $request.envelope.presentation.topic
        return
    }
    if ($request.envelope.command | str lowercase) != 'doctor' {
        Native-Views render $ui $request.envelope
        return
    }
    let doctor = $request.envelope.data.doctor
    for section in ['SYSTEM' 'PACKWIZ' 'PROJECT ROOT' 'PROJECT' 'BUILD ARTIFACT' 'OPTIONAL'] {
        let checks = ($doctor.checks | where section == $section)
        if ($checks | is-empty) {continue}
        r3cli heading $ui $section
        for check in $checks {
            r3cli status $ui (kind $check.status) $'($check.label): ($check.value)'
            if $check.detail != '' {r3cli line $ui [{text: $'  ($check.detail)', role: secondary}]}
            for item in $check.items {r3cli status $ui (kind $item.Status) $item.Text}
        }
    }
    r3cli line $ui
    if $doctor.failures > 0 {
        r3cli status $ui error (($doctor.failures | into string) + ' required issue(s) need attention. Run: modpack doctor --fix')
    } else {
        r3cli status $ui success 'No known required issues were found.'
    }
    if $doctor.warnings > 0 {r3cli status $ui warning (($doctor.warnings | into string) + ' optional recommendation(s) or incomplete verification.')}
}
