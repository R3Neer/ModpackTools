# Capability129: native-only Nushell routing

Product29ca684 removes the PowerShell bridge, request capture and incremental
command ownership heuristics. Every request reaches the native host. The packaged
module defaults to its sibling modpacktools.exe; an explicit CORE_PATH override
remains for development and controlled fixtures. Missing/non-string/directory
overrides fail with actionable diagnostics. Unknown commands return Command.Unknown.

CLI196 SHA256 FE94DB06E2F68EAB2EA8AE529AA1D9E8CFFF3768683F375660B7D37337E2A7C9.
Formatting, warning-free Release/AOT, protocol15 and broad Nu/real Packwiz export
checks pass. Parent packaged default/version/unknown/missing tests pass with empty
PATH. One initial C# build failed because a diagnostic target needed TextTarget;
one test-script interpolation failed before reaching the product. Both corrected;
neither failure is hidden as a passing initial attempt.

White source audit finds no PowerShell invocation in native doctor, installer,
launcher, dependency acquisition or R3 renderer. Exactly2 fresh-Nu negative
overrides pass with zero core calls and no session/environment mutations.
Black exactly2 fresh-Nu calls pass version and unknown-command behavior without
CORE_PATH and with empty PATH. Its initial error matcher expected a longer ID
prefix than the public formatted Nu error; the comparator was corrected against
the captured response without rerunning the product.

Known distribution follow-up: the README selected by the package builder still
describes the legacy installer. This must be corrected before final packaging.
ZIP129 SHA171EBD9580BF15523AA95E93780353193D77F710C56586541630DF5BAA63EECD
is a frozen review fixture, not the final deliverable.

Reports: assessment/review-whitebox/capability-129-native-only-final.md and
assessment/review-blackbox/native-only129-final-report.md (see reviewer directory
for its final report filename). No live installation/profile/publication changed.
