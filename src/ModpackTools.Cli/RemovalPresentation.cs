using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ModpackTools.Core;
using ModpackTools.Core.Diagnostics;
using ModpackTools.Core.Resolution;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Cli;

internal sealed record RemovalDisplayRow(string Id, string Name, string? VersionId, string Reason);
internal sealed record RemovalPresentation(string Kind, string ReviewToken, string[] Requested, RemovalDisplayRow[] Items, DoctorItem[] HealthItems)
{
    public static RemovalPresentation From(TransactionPreview<RemovalPlan> preview)
    {
        var items = preview.Result.Changes.Select(change => new RemovalDisplayRow(change.Id, change.Before.Item.Name, change.Before.Graph.VersionId, change.Reason)).ToArray();
        var requested = preview.Result.Requested.ToArray();
        var health = HealthDisplay.Read(preview.Result.Report, false, new(), compact: true).ToArray();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        // Length-prefixed UTF-8 fields distinguish null, empty and boundaries.
        // Hash both the protected source and all facts shown for approval.
        Add("ModpackTools/removal-review/v2");
        Add(preview.Expectation.Project.CanonicalRoot); Add(preview.Expectation.Project.FileIdentity);
        Add(preview.Expectation.BeforeFingerprint);
        Count(preview.Expectation.Changes.Length);
        foreach (var change in preview.Expectation.Changes) { Add(change.Path); Add(change.Before); Add(change.After); }
        Count(requested.Length); foreach (var id in requested) Add(id);
        Count(items.Length); foreach (var item in items) { Add(item.Id); Add(item.Name); Add(item.VersionId); Add(item.Reason); }
        Count(health.Length); foreach (var item in health) { Add(item.Status); Add(item.Text); }
        return new("removal", Convert.ToHexString(hash.GetHashAndReset()), requested, items, health);

        void Count(int value)
        {
            Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(bytes, value); hash.AppendData(bytes);
        }
        void Add(string? value)
        {
            if (value is null) { Count(-1); return; }
            Count(Encoding.UTF8.GetByteCount(value));
            // Avoid materializing another large encoded batch just to hash it.
            Span<byte> bytes = stackalloc byte[4096];
            var encoder = Encoding.UTF8.GetEncoder(); var remaining = value.AsSpan();
            do
            {
                encoder.Convert(remaining, bytes, true, out var used, out var written, out var complete);
                hash.AppendData(bytes[..written]); remaining = remaining[used..];
                if (complete) break;
            } while (true);
        }
    }

    public void AssertReviewed(string? reviewed)
    {
        if (reviewed is not null && !StringComparer.OrdinalIgnoreCase.Equals(ReviewToken, reviewed))
            throw new DomainException("Transaction.ReviewChanged", "The removal differs from the reviewed plan", "preview the removal again before confirming", "InvalidOperation");
    }
}
