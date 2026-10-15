# Capability 102: native verified dependency download

Internal scope: caller-trusted HTTPS URI/SHA-256 and bounded policy produce one
complete owned temporary file with exact SHA-256 and length. This transfer result
is not yet a prepared ZIP/package or installed executable: feed it through100/101.
No download destination restriction to HOME/Documents, shell invocation, installer
activation, configuration/PATH update or automatic tool installation in this unit.

Production handler disables auto-redirect and decompression, bounds headers, and
validates every redirect destination as absolute HTTPS without credentials or
fragment before sending again. One deadline spans headers/redirects/body; actual
stream bytes are checked before each write independent of Content-Length. An
excess declared length fails before creating a file. Final SHA-256 is computed
from those exact streamed bytes; expected/actual digest detail is retained for
mismatch. Returned path is a unique CreateNew leaf under a canonical held parent.
Caller owns successful temporary lifecycle. Failure cleans only its own GUID
file; uncertain cleanup retains it with exact path and RetainStage. Cancellation
is terminal and distinct from deadline expiry. Parent empty container may remain.

Finite evidence planned: black original valid transfer and transfer exception use
the original module's supported manifest/config overrides and an in-memory
Invoke-WebRequest stub, isolated process-start temp/profile. Native inspector
uses controlled HttpMessageHandler responses. Compare transfer bytes and errors,
not old original's extra private installation or a claim of real HTTP/TLS/network
coverage. Source URL replaces the obsolete ephemeral copied-ZIP error target.
White two complements: body without Content-Length exceeds actual byte quota;
redirect to HTTP is rejected before another send. Parent tests exact transfer,
wrong digest cleanup and pre-cancellation without a write. No external service.
Accepted product checkpoint `1c7c240`. Scoped format/Release/Native AOT succeed
without warnings. Core130 SHA-256:
2A5F001693B2C810F4164C931A388AD9877A4BAC8F4716EA87C772CAD3F80D35.
Parent3 controls pass; black2/2 transfers pass with exact10,841,652-byte original
ZIP/hash, one request and refined causal error. Original18 manifest entries,
three sources/ZIP bytes+mtime intact,10 exclusive handle checks pass. White2/2
controls pass:131072-byte unknown-length body with70000 quota rejects after a
partial write and removes it, foreign sentinel retained; redirect downgrade logs
only one HTTPS request and never creates staging. A white harness setup attempt
sent empty JSON by using PowerShell automatic variable input, rejected before
handler; preserved separately, corrected final two calls pass. No product bug or
extra semantic case attributed to that harness error.
External assessment/native-dependency-download130-results.md and independent
review reports packwiz-download102-final-report.md / capability-102-dependency-
download-final.md retain evidence. Real pinned HTTPS acquisition remains a later
integration check before claiming end-to-end native bootstrap.
