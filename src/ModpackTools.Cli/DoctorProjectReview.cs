using System.Security.Cryptography;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Data;
using ModpackTools.Core.Diagnostics;
using ModpackTools.Core.Processes;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Cli;

internal sealed record DoctorReviewFacts(string Contract, ConfigurationObservation Configuration, PackwizObservation Tool,
    bool Strict, bool AllowDowngrade, TransactionExpectation Project, ContentPlanPresentation Plan);
internal sealed record DoctorProjectReviewPresentation(string Kind, string ReviewToken,
    ContentChangePresentation[] Items, DoctorItem[] HealthItems)
{
    public static DoctorProjectReviewPresentation From(DoctorRepairResult repair, ConfigurationObservation configuration,
        PackwizObservation tool, bool strict, bool allowDowngrade)
    {
        var project = repair.Project ?? throw new InvalidOperationException("Project review requires a prepared project.");
        var plan = ContentPlanPresentation.From(project.Result);
        var facts = new DoctorReviewFacts("ModpackTools/doctor-project-review/v1", configuration, tool, strict, allowDowngrade, project.Expectation, plan);
        using var algorithm = SHA256.Create();
        using var hash = new CryptoStream(Stream.Null, algorithm, CryptoStreamMode.Write, leaveOpen: true);
        using (var writer = new Utf8JsonWriter(hash)) JsonSerializer.Serialize(writer, facts, WireJsonContext.Default.DoctorReviewFacts);
        hash.FlushFinalBlock();
        return new("doctor-repair", Convert.ToHexString(algorithm.Hash!), plan.Items, plan.HealthItems);
    }
    public void AssertReviewed(string? reviewed)
    {
        if (reviewed is not null && !StringComparer.OrdinalIgnoreCase.Equals(ReviewToken, reviewed))
            throw new DomainException("Transaction.ReviewChanged", "Project repair changed since it was shown; no reviewed project changes were applied",
                "run doctor --fix again and review the new project repair", "ResourceBusy");
    }
}
