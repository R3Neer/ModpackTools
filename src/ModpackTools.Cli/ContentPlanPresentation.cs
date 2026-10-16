using ModpackTools.Core.Diagnostics;
using ModpackTools.Core.Resolution;

namespace ModpackTools.Cli;

internal sealed record ContentChangePresentation(string Id, string Name, string Previous, string Next, string Reason);
internal sealed record ContentPlanPresentation(string Kind, ContentChangePresentation[] Items, DoctorItem[] HealthItems)
{
    public static ContentPlanPresentation From(ContentPlan plan) => new("content-plan",
        [.. plan.Changes.Select(change => new ContentChangePresentation(change.Id, change.After.Item.Name,
            change.Before is null ? "not installed" : string.IsNullOrEmpty(change.Before.Graph.VersionId) ? "installed" : change.Before.Graph.VersionId,
            change.After.Graph.VersionId, change.Reason))],
        [.. HealthDisplay.Read(plan.Report, false, new(), compact: true)]);
}
internal sealed record ContentPlanEnvelope(int SchemaVersion, bool Ok, string Command, string[] Arguments,
    TransactionData Data, ContentPlanPresentation Presentation);
