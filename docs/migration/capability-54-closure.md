# Capability 54: side-specific dependency reports

Product checkpoint: 9a25c32. Pure typed graph construction is complete; public
content routing, health composer/cache and baseline policy are not activated.
Validator revision4 and key schema2 must isolate later cache/baseline consumers.

Locked restore, format verification and strict Release builds passed for the
three projects; both Native AOT publishes completed without warnings.
Host artifacts/contracts-side-graph-v75/CoreContracts.exe SHA256:
1EFEA102DEDA5A7BB4A20BC40D85CCF3838D3670EE2167AA1B236E9BC8EADDBF.
CLI artifacts/native-side-graph-v82/modpacktools.exe SHA256:
E311A7BCCC7DEF945026E9CC81109E18F0B80EABC129ECBC92047E4B8F370FED.

Parent Native default and 258 finite regression cases passed. Independent black
box: 103 Native calls, 67 exact externally calculated keys, four collision
relations and 34 full issue audits. White box: source review, 15 Native probes,
external warning-key calculation and default Native passed. No blocking findings.
Evidence remains outside product in workspace assessment/review-blackbox/
side-graph-v75-final-report.md and assessment/review-whitebox/
capability-54-side-graph.md. Preserved original baselines distinguish literal
JSON comparer provenance and the intentional Keys alias correction.

Budgets bound inputs, both-side evaluator consumption, fact versions, repeated
hash text and constructed messages. Limits fail with typed errors and no partial
report. This is not a measurement of the complete application's memory budget
or an end-to-end performance claim.
