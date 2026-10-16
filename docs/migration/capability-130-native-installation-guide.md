# Capability130: native installation entrypoint and shipped guidance

Product0534cf0 replaces the source Nu installer with explicit native setup
invocation. It requires a new application root, native ZIP and trusted SHA256;
--setup selects an extracted native helper and --state selects recovery state.
It does not acquire any shell, change profiles/PATH/plugins or infer Documents.
The shipped README and source Nu guide explain native installation, session
selection, human/machine use, update, rollback and explicit retirement.

Parent Test-NativeNuInstaller.ps1 passes a real package/install/launcher cycle
with private configuration/state/AppData/TEMP, empty PATH and paths with spaces.
Payload has no PowerShell scripts; shipped README no longer offers legacy setup.
Nu syntax and diff checks pass. No C# changed; native inputs remain CLI196,
Launcher193 and Setup194. No additional SDK publication was necessary.

White source review agrees with native wrapper behavior and found one package-
relative documentation link and pre-existing corrupted diagram characters.
Follow-up c14428a fixes these using ASCII diagrams and source-only references.
An initial text-fix helper failed because it omitted explicit UTF8 decoding;
the corrected helper succeeded in the follow-up commit rather than amending history.

Report: assessment/review-whitebox/capability-130-native-docs-source-review.md.
Public assets are not yet published. No live installation was activated.
