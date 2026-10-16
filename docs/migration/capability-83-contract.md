# Capability83: public config get/set and Nushell

Preserve original syntax/priorities: project context forbidden before argument
validation, known setting before operation dispatch, verb-specific arity. These
commands treat remaining tokens as positionals just as original (no new command
option grammar). Active session project does not affect global config.

Original structured output is data={} for BOTH reads/writes; keep that contract
rather than inventing new fields. Prepare complete typed empty-success bytes before
any Set; use accepted ConfigurationStore. Read root/executable discovery through
existing native readers without launching the discovered executable. Invalid UTF8
now maps to the same typed InvalidFile for reads and writes. Human key/value output
remains separate work, not represented as completed by this machine checkpoint.
Nu owns machine config, validates empty data and command shape, returns {} and
preserves parent state with terminal native failures.

Black3 public: root set through Nu with spaces/unknown preservation, root get CLI,
explicit project forbidden before config read. Parent adds fresh Nu set/get/auto
and invalid peer; white adds two complementary controls. No live configuration.

Accepted: product fa81990. Black-box 3/3 and white-box 2/2 independent controls passed. Native Core109 SHA256 3ACE48CBAC14321167D56C974DCE386744BBF3B71D12F807289080EED385AAD6; CLI116 4B2D9F7D58C7A2165508DF895DCB70D215A0278C7BF164763A0F0276695DDF4E; Nu 63FBC00AEE9A43296C8532AE6E94A280324BAD468D14942AB8AE7D980BB0A897. Warning-free strict Release/AOT, default native contracts, protocol15/version and fresh Nu operations/peer guards passed. Reports: assessment/review-blackbox/public-config83-final-report.md and assessment/review-whitebox/capability-83-public-configuration.md in the parent workspace. Human configuration presentation is still pending.
