# Native Nu short alias validation

Discovered while wiring diagnostic doctor99: the core expands -n/-y/-V/-h as
documented before emitting public arguments, but Nu validated the response against
raw short options. This could reject a valid dry-run or misclassify removal preview.

The adapter normalizes exactly these existing aliases at native invocation entry,
then uses that same argument list for request and response validation. Business
grammar remains in the core. Ownership/help/coexistence behavior is unchanged.
This is a separate coherent adapter fix before continuing doctor99, not a partial
commit of its unfinished C# files.

Accepted product checkpoint: `1980f4f`. Syntax and full fresh-process Nu suite pass
against existing published CLI123/Core120, with actual Packwiz, empty PATH and
unchanged source hashes. The positive local set-side dry run matches the original;
the original local-pin error also matches (pinning local content is unsupported).
The black-box controls use a missing Packwiz sentinel to prove neither local
operation launches it. One white-box peer control rejects a response that claims
dry_run=false after -n. Parent's first positive test incorrectly chose local pin;
that domain error was preserved and the positive case corrected to local set-side.

Final Nu SHA-256: F027D2AB70A722BAF9EA7B95DE8E3798127B8BD6221EC1D837CB16E2E6291F23.
CLI123: 917C4517085BFC36D27C790AF1DEC07FE7CB8FC20FAB86F6BCC2631EF87D785A.
Core120 peer: FE9FF41EA5253EACFB95D05D9385581FEB24D07C1E6C65194B65985E4C18ABF6.
Independent evidence is in the external assessment review directories, reports
nu-short-alias-hotfix-final.md and nu-alias-final-20261015-231423 run.
No C# source changes belong to this checkpoint; a new SDK build/AOT publication
is not claimed or required for Nu-only. Doctor99 remains unfinished and unstaged.
