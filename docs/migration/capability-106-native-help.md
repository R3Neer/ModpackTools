# Capability106: embedded typed native help

Accepted on CLI140. CLI help is a typed catalogue embedded in Native AOT, loaded
with generated JSON metadata. No source checkout, PowerShell, R3CLI package,
project discovery, provider or configuration read is needed to obtain machine
help. Human Nu uses the same catalogue with packaged R3CLI help rendering.

Initial text/order was captured once during development from the legacy catalogue;
that export is not a build/runtime dependency. Future native help edits update
the owned JSON data directly. Session wording now says shell rather than
PowerShell; command catalogue and options otherwise preserve legacy text/order.
Having a self-update help page is not a claim its native implementation exists.

Preserve legacy machine data: global help data.help.command=null; topic help data
is empty. Extra envelope.presentation carries the catalogue/topic to the adapter
and is validated before use, separate from structured domain data. Nu unwrap
still returns those legacy shapes. Unrequested presentation is rejected on normal
responses. Topic, command/argument echo, entries/options/text types/groups/unique
commands and legacy data are checked before rendering or returning values.

An empty native request now implements default help, replacing the temporary
Core.CapabilityUnavailable checkpoint behavior; the existing15-case protocol
suite updates that one expected success. Help takes precedence over project/domain
access. Global help rejects extra arguments; singleton error targets preserve the
existing scalar wire convention. Unknown command help retains Command.Unknown.

Finite acceptance: parent default/global/all10 topics, missing configuration and
nonexistent global project, three malformed help peers; parent bridge-free human
global/topic rendering plus existing machine/presentation regression. Black two
legacy full-envelope/catalogue/human cases. White two complementary error/peer
controls and source review. No live shell activation or release.

CLI140 SHA256435BDA6246B90D7A371C4B88BEC106628C86B0FB5CEF4FC6EFDBC145323D049B;
Nu290B12FB22FEE9788D2676C4EF1E2765EC849820750D10787E2990AAAF26E520;
renderer28204BDC1F7A1044B5DF22686050CEA8523DA920B48FC50B1A21E4FAE4BC43E8.
Locked restore unchanged/up-to-date, scoped format, Release and AOT zero warnings,
protocol15/version, complete Nu suite and bridge-free parent human help passed.
Parent covers default/ten-topic/global-project help with no configuration creation
and three malformed catalogues. No core-library implementation changed.

Black exactly2 scenarios, explicitly2 CLI plus2 Nu calls, passed complete recursive
catalogue comparison including all10 commands, envelopes excluding only additive
presentation metadata and complete human information after layout folding.
White exactly2 controls passed: singleton extra argument yields scalar target
before corrupt configuration/project access; contradictory peer topic is rejected.
Source review caught/fixed effective-empty Nu argument classification and scalar
target before freeze; unrequested presentation is also rejected. No additional
original cases or unrelated corpus were added.

Reports: assessment/review-blackbox/help106-final-report.md and
assessment/review-whitebox/capability-106-native-help-final.md.
