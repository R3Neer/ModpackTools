using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Versions;

internal static class RootManifestInspection
{
    public static int Run(string[] arguments, bool forge = false, bool archive = false, bool traversal = false)
    {
        try
        {
            var offset = archive ? 3 : forge ? 6 : 4;
            if (arguments.Length < offset || (arguments.Length - offset) % 2 != 0)
                throw new ArgumentException(archive ? "Use --inspect-loader-archive-root <archive-file> <loader> [--option value]." : forge
                    ? "Use --inspect-forge-manifest <mods-file-or-null> <neo-file-or-null> <manifest-file-or-null> <jarjar-file-or-null> <loader> [--option value]."
                    : "Use --inspect-root-manifest <fabric-file-or-null> <quilt-file-or-null> <loader> [--option value].");
            var limits = new RootManifestLimits();
            var forgeLimits = new ForgeManifestLimits();
            var archiveLimits = new RootArchiveLimits();
            var traversalLimits = new ArtifactTraversalLimits();
            var probes = ImmutableArray<string?>.Empty;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = offset; index < arguments.Length; index += 2)
            {
                if (!seen.Add(arguments[index])) throw new ArgumentException("Duplicate root-manifest inspection option.");
                var value = arguments[index + 1];
                int Number() => int.Parse(value, CultureInfo.InvariantCulture);
                long Bytes() => long.Parse(value, CultureInfo.InvariantCulture);
                switch (arguments[index])
                {
                    case "--max-nested-depth" when traversal: traversalLimits = traversalLimits with { MaximumDepth = Number() }; break;
                    case "--max-child-bytes" when traversal: traversalLimits = traversalLimits with { MaximumChildBytes = Bytes() }; break;
                    case "--max-copied-bytes" when traversal: traversalLimits = traversalLimits with { MaximumCopiedBytes = Bytes() }; break;
                    case "--max-nested-attempts" when traversal: traversalLimits = traversalLimits with { MaximumNestedAttempts = Number() }; break;
                    case "--max-total-metadata-bytes" when traversal: traversalLimits = traversalLimits with { MaximumMetadataBytes = Bytes() }; break;
                    case "--max-total-central-bytes" when traversal: traversalLimits = traversalLimits with { MaximumCentralBytes = Bytes() }; break;
                    case "--max-total-entries" when traversal: traversalLimits = traversalLimits with { MaximumEntries = Number() }; break;
                    case "--max-total-mods" when traversal: traversalLimits = traversalLimits with { MaximumMods = Number() }; break;
                    case "--max-total-diagnostics" when traversal: traversalLimits = traversalLimits with { MaximumDiagnostics = Number() }; break;
                    case "--culture": CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(value); break;
                    case "--range-probes": probes = Probes(value); break;
                    case "--max-manifest-chars": limits = limits with { MaximumTextCharacters = Number() }; break;
                    case "--max-manifest-tokens": limits = limits with { MaximumTokenCharacters = Number() }; break;
                    case "--max-manifest-nodes": limits = limits with { MaximumNodes = Number() }; break;
                    case "--max-manifest-depth": limits = limits with { MaximumJsonDepth = Number() }; break;
                    case "--max-requirements": limits = limits with { MaximumRequirements = Number() }; break;
                    case "--max-requirement-depth": limits = limits with { MaximumRequirementDepth = Number() }; break;
                    case "--max-aliases": limits = limits with { MaximumAliases = Number() }; break;
                    case "--max-nested-paths": limits = limits with { MaximumNestedPaths = Number() }; break;
                    case "--max-mods" when forge || archive: forgeLimits = forgeLimits with { MaximumMods = Number() }; break;
                    case "--max-diagnostics" when forge || archive: forgeLimits = forgeLimits with { MaximumDiagnostics = Number() }; break;
                    case "--max-toml-depth" when forge || archive: forgeLimits = forgeLimits with { MaximumTomlDepth = Number() }; break;
                    case "--max-syntax-nodes" when forge || archive: forgeLimits = forgeLimits with { MaximumSyntaxNodes = Number() }; break;
                    case "--max-metadata-bytes" when archive: archiveLimits = archiveLimits with { Archive = archiveLimits.Archive with { MaximumMetadataBytes = Bytes() } }; break;
                    case "--max-central-bytes" when archive: archiveLimits = archiveLimits with { Archive = archiveLimits.Archive with { MaximumCentralDirectoryBytes = Bytes() } }; break;
                    case "--max-archive-entries" when archive: archiveLimits = archiveLimits with { Archive = archiveLimits.Archive with { MaximumEntries = Number() } }; break;
                    case "--max-aggregate-metadata-bytes" when archive: archiveLimits = archiveLimits with { MaximumAggregateMetadataBytes = Bytes() }; break;
                    default: throw new ArgumentException("Unsupported root-manifest inspection option.");
                }
            }
            RootManifestDeclarations result;
            ArchiveReadStatus? archiveStatus = null;
            ArtifactArchiveDeclarations? artifact = null;
            if (traversal)
            {
                artifact = ArtifactArchiveReader.Read(arguments[1], arguments[2], traversalLimits with
                { Root = archiveLimits with { Declarations = forgeLimits with { Declarations = limits } } });
                result = artifact.Root?.Root ?? new([], [], [], RootManifestStatus.Incomplete, false);
                archiveStatus = artifact.Root?.Status;
            }
            else if (archive)
            {
                var read = RootArchiveReader.Read(arguments[1], arguments[2], archiveLimits with { Declarations = forgeLimits with { Declarations = limits } });
                result = read.Root; archiveStatus = read.Status;
            }
            else if (forge)
            {
                var loader = arguments[5];
                var neo = string.Equals(loader, "neoforge", StringComparison.InvariantCultureIgnoreCase);
                var supported = neo || string.Equals(loader, "forge", StringComparison.InvariantCultureIgnoreCase);
                var neoText = neo ? Read(arguments[2]) : null;
                var modsText = supported && string.IsNullOrEmpty(neoText) ? Read(arguments[1]) : null;
                var selected = !string.IsNullOrEmpty(neoText) ? neoText : modsText;
                result = ForgeRootManifestParser.Parse(new(modsText, neoText,
                    supported && !string.IsNullOrEmpty(selected) ? Read(arguments[3]) : null,
                    supported ? Read(arguments[4]) : null), loader, forgeLimits with { Declarations = limits });
            }
            else
            {
                var isQuilt = string.Equals(arguments[3], "quilt", StringComparison.InvariantCultureIgnoreCase);
                result = RootManifestParser.Parse(Read(arguments[1]), isQuilt ? Read(arguments[2]) : null, arguments[3], limits);
            }
            using var bytes = new InspectionBuffer();
            using (var writer = new Utf8JsonWriter(bytes, new() { MaxDepth = 512 }))
            {
                writer.WriteStartObject();
                if (artifact is not null) writer.WriteString("traversal_status", artifact.Status.ToString());
                if (archiveStatus is not null) writer.WriteString("archive_status", archiveStatus.ToString());
                writer.WriteString("status", result.Status.ToString());
                writer.WriteBoolean("can_traverse_nested", result.CanTraverseNested);
                writer.WriteStartArray("mods");
                foreach (var occurrence in artifact?.Mods ?? [.. result.Mods.Select(mod => new ArtifactMod(mod, false, []))])
                {
                    var mod = occurrence.Declaration;
                    writer.WriteStartObject();
                    writer.WriteString("Id", mod.Id); writer.WriteString("Version", mod.Version); writer.WriteString("Side", mod.Side); writer.WriteBoolean("Nested", occurrence.Nested);
                    if (artifact is not null)
                    { writer.WriteStartArray("EntryChain"); foreach (var entry in occurrence.EntryChain) writer.WriteStringValue(entry); writer.WriteEndArray(); }
                    writer.WriteStartArray("Aliases");
                    foreach (var alias in mod.Provides) Alias(writer, alias);
                    writer.WriteEndArray();
                    writer.WriteStartArray("Requirements");
                    foreach (var requirement in mod.Requirements) Requirement(writer, requirement, probes);
                    writer.WriteEndArray(); writer.WriteEndObject();
                }
                writer.WriteEndArray();
                var diagnostics = artifact?.Diagnostics ?? [.. result.Diagnostics.Select(diagnostic => new ArtifactDiagnostic(diagnostic, []))];
                writer.WriteStartArray("warnings"); foreach (var item in diagnostics) writer.WriteStringValue(item.Diagnostic.Message); writer.WriteEndArray();
                writer.WriteStartArray("diagnostics");
                foreach (var item in diagnostics)
                {
                    var diagnostic = item.Diagnostic;
                    writer.WriteStartObject(); writer.WriteString("code", diagnostic.Code); writer.WriteString("message", diagnostic.Message);
                    writer.WriteString("manifest", diagnostic.Manifest); writer.WriteString("details", diagnostic.Details); writer.WriteString("hint", diagnostic.Hint);
                    if (artifact is not null)
                    { writer.WriteStartArray("entry_chain"); foreach (var entry in item.EntryChain) writer.WriteStringValue(entry); writer.WriteEndArray(); }
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteStartArray("nested_paths"); foreach (var path in result.NestedPaths) writer.WriteStringValue(path); writer.WriteEndArray();
                writer.WriteEndObject();
            }
            Console.OpenStandardOutput().Write(bytes.GetBuffer(), 0, checked((int)bytes.Length));
            return 0;
        }
        catch (DomainException exception)
        {
            ManagedInspection.WriteFailure(exception.Id, exception.Message, exception.Category, exception.Target, exception.Hint, exception.Details);
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or FormatException
            or OverflowException or InvalidOperationException or JsonException)
        {
            ManagedInspection.WriteFailure("Core.InputReadFailed", exception.Message, "InvalidData", null, "check root-manifest inspection arguments and files", null);
            return 2;
        }
    }

    // Repeated declaration text/probe projections cannot expand a diagnostic
    // response indefinitely. Buffer first so failures never emit partial JSON.
    private sealed class InspectionBuffer : MemoryStream
    {
        private const int MaximumOutputBytes = 32 * 1024 * 1024;
        private void Guard(int count)
        {
            if (Length + count > MaximumOutputBytes)
                throw new DomainException("Metadata.InspectionOutputLimit", "Root inspection response exceeds its byte limit",
                    "inspect fewer declarations or range probes", target: nameof(MaximumOutputBytes));
        }
        public override void Write(byte[] buffer, int offset, int count) { Guard(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Guard(buffer.Length); base.Write(buffer); }
    }
    private static string? Read(string path)
    {
        if (path == "null") return null;
        using var file = File.OpenRead(path);
        using var bytes = new MemoryStream();
        var buffer = new byte[65536];
        int count;
        while ((count = file.Read(buffer)) != 0)
        {
            if (count > 8 * 1024 * 1024 - bytes.Length) throw new ArgumentException("Diagnostic manifest file exceeds its byte quota.");
            bytes.Write(buffer, 0, count);
        }
        bytes.Position = 0;
        using var reader = new StreamReader(bytes, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
    private static ImmutableArray<string?> Probes(string json)
    {
        if (json.Length > 1024 * 1024) throw new ArgumentException("Range probes exceed their input quota.");
        using var document = JsonDocument.Parse(json, new() { MaxDepth = 16 });
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() > 64) throw new ArgumentException("Range probes must be a bounded array.");
        return [.. document.RootElement.EnumerateArray().Select(value => value.ValueKind is JsonValueKind.String or JsonValueKind.Null
            ? value.GetString() : throw new ArgumentException("A range probe must be a string or null."))];
    }
    private static void Alias(Utf8JsonWriter writer, ManifestAlias alias)
    {
        var key = alias.Key;
        writer.WriteStartObject(); writer.WriteString("key_kind", key.Kind.ToString());
        writer.WriteString("key_type", key.Kind switch
        {
            ManifestAliasKeyKind.String => "System.String",
            ManifestAliasKeyKind.Int64 => "System.Int64",
            ManifestAliasKeyKind.Double => "System.Double",
            ManifestAliasKeyKind.BigInteger => "System.Numerics.BigInteger",
            ManifestAliasKeyKind.Boolean => "System.Boolean",
            ManifestAliasKeyKind.Date => "System.DateTime",
            ManifestAliasKeyKind.Array => "System.Object[]",
            _ => "System.Management.Automation.OrderedHashtable"
        });
        writer.WritePropertyName("key");
        switch (key.Kind)
        {
            case ManifestAliasKeyKind.String: writer.WriteStringValue(key.StringValue); break;
            case ManifestAliasKeyKind.Int64: writer.WriteNumberValue(key.IntegerValue!.Value); break;
            case ManifestAliasKeyKind.BigInteger: writer.WriteRawValue(key.BigIntegerValue!.Value.ToString(CultureInfo.InvariantCulture)); break;
            case ManifestAliasKeyKind.Double:
                var number = key.FloatingValue!.Value;
                if (double.IsFinite(number)) writer.WriteNumberValue(number); else writer.WriteStringValue(number.ToString(CultureInfo.InvariantCulture)); break;
            case ManifestAliasKeyKind.Boolean: writer.WriteBooleanValue(key.BooleanValue!.Value); break;
            case ManifestAliasKeyKind.Date: writer.WriteStringValue(key.DateValue!.Value); break;
            default: writer.WriteRawValue(key.OpaqueJson!); break;
        }
        writer.WriteString("value", alias.Version); writer.WriteEndObject();
    }
    private static void Requirement(Utf8JsonWriter writer, ManifestRequirement requirement, ImmutableArray<string?> probes)
    {
        writer.WriteStartObject(); writer.WriteString("Target", requirement.Target); writer.WriteString("RangeDisplay", requirement.RangeDisplay);
        writer.WriteString("Kind", requirement.Kind); writer.WriteString("Side", requirement.Side); writer.WriteString("Scope", requirement.Scope);
        writer.WriteBoolean("Maven", requirement.Scheme == VersionScheme.Maven); writer.WriteString("Source", requirement.Source);
        writer.WriteString("SuggestedVersionId", requirement.SuggestedVersionId);
        writer.WriteStartArray("RangeChecks");
        foreach (var version in probes)
        {
            writer.WriteStartObject(); writer.WriteString("Version", version);
            try
            {
                var result = VersionRanges.Evaluate(version, requirement.Range, requirement.Scheme);
                writer.WritePropertyName("Result"); if (result is { } matches) writer.WriteBooleanValue(matches); else writer.WriteNullValue();
                writer.WriteNull("Error");
            }
            catch (DomainException exception) { writer.WriteNull("Result"); writer.WriteString("Error", exception.Id); }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteStartArray("Any"); foreach (var child in requirement.Any) Requirement(writer, child, probes); writer.WriteEndArray();
        writer.WriteStartArray("All"); foreach (var child in requirement.All) Requirement(writer, child, probes); writer.WriteEndArray();
        writer.WritePropertyName("Unless"); if (requirement.Unless is { } unless) Requirement(writer, unless, probes); else writer.WriteNullValue();
        writer.WriteEndObject();
    }
}
