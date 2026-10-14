# Capability55: graph policy

Architecture reviewed before implementation. GraphPolicy.Assert(report,
baseline=null, strict=false, build=false, limits=null) is pure: no IO/network/cache
or report mutation. Both inputs are closed current GraphReport objects, validator4
and key schema2. Legacy cache objects cannot enter. A later persisted baseline
reader must recalculate or explicitly validate revision and source identity.

Baseline suppresses matching error keys only when neither strict nor build.
OrdinalIgnoreCase over closed hexadecimal keys matches the original key domain.
Build blocks all errors, allows unknown unless strict. Strict blocks all errors
and unknown. Warnings never block. Empty baseline suppresses nothing.
Details preserve selected error order followed by unknown only for strict,
separated by semicolon and space. Report is unchanged.

Blocked: Compatibility.GraphBlocked / InvalidOperation / null target,
message 'The resulting pack does not satisfy dependency validation', hint
'resolve the listed requirements, unpin a blocking version, or inspect incomplete metadata'.

GraphPolicyLimits.MaximumDetailCharacters defaults to1Mi UTF16characters,
positive validation upfront even for an allowed report. Sum selected messages
and separators with long before allocation. Exact passes; over is
Compatibility.PolicyDetailLimit/LimitsExceeded, target MaximumDetailCharacters.
Invalid quota is Compatibility.InvalidGraphPolicy/InvalidArgument. This explicit
resource refinement replaces unbounded detail allocation, never drops diagnostics.

Inspection: --inspect-graph-policy case.json with Graph54 shape plus optional
Baseline object of same shape. Flags --strict true|false, --build true|false,
--max-policy-details-chars integer. Same4Mi input and independent per-report graph
quotas. Allowed response {"Allowed":true}/exit0; domainfailure1, badtransport2.
Existing graph-report inspection unchanged. Acceptance covers complete flag/baseline
matrix, no mutation, exact/over limits, exact error and unknown detail order.
This is not health composition, provider validation or public command activation.

Closed acceptance: host76 SHA256
6F77B687F282E0CF6B1325550C4CB582EC1BD78E0CD08B6E890728371EC9F814,
CLI83 SHA256 E2C1A6706502A8467A093CC45FDB9F797CB98857C337923148CE3023B2FF0180.
Three-project locked restore/format verification/strict Release and both Native
AOT publications passed without warnings. Parent default Native,29 requirement
comparisons and38 current/context cases passed. Blind109 includes36 policy,
56 exact previous-host graph regressions,14 quota and3 grammar controls.
White39 includes24 original policy controls and15 graph regressions, plus
external key calculation/default suite. Original38 policy captures remain,
with two reverse-report characterizations outside closed inspector input.
No baseline was rewritten and no live cache/install/project was modified.
