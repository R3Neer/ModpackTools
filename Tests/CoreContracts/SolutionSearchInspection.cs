using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Graph;
using ModpackTools.Core.Providers;
using ModpackTools.Core.Resolution;
using ModpackTools.Core.Versions;

internal static class SolutionSearchInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            if (arguments.Length < 3 || (arguments.Length - 3) % 2 != 0) throw new ArgumentException("Use --inspect-solution-search <case-json-array> <case-id> [--option value].");
            var limits = new ResolutionLimits(); using var cancellation = new CancellationTokenSource();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 3; index < arguments.Length; index += 2)
            {
                if (!seen.Add(arguments[index])) throw new ArgumentException("Duplicate solution inspection option.");
                var value = long.Parse(arguments[index + 1], CultureInfo.InvariantCulture);
                switch (arguments[index])
                {
                    case "--max-states": limits = limits with { MaximumStates = checked((int)value) }; break;
                    case "--max-work": limits = limits with { MaximumWork = value }; break;
                    case "--max-identity-bytes": limits = limits with { MaximumIdentityBytes = value }; break;
                    case "--max-retained-characters": limits = limits with { MaximumRetainedCharacters = value }; break;
                    case "--max-domains": limits = limits with { MaximumDomains = checked((int)value) }; break;
                    case "--max-candidate-versions": limits = limits with { MaximumCandidateVersions = checked((int)value) }; break;
                    case "--max-candidate-nodes": limits = limits with { MaximumCandidateNodes = checked((int)value) }; break;
                    case "--cancel": if (value != 0) cancellation.Cancel(); break;
                    default: throw new ArgumentException("Unknown solution inspection option.");
                }
            }
            using var file = File.OpenRead(arguments[1]);
            if (file.Length > 4 * 1024 * 1024) throw new ArgumentException("Solution fixture exceeds4MiB.");
            using var document = JsonDocument.Parse(file, new() { MaxDepth = 256 });
            var root = document.RootElement.EnumerateArray().Single(value => Text(value, "id") == arguments[2]);
            var initial = Rows(root, "initial").Select(Node).ToImmutableArray();
            var before = root.TryGetProperty("before", out _) ? Rows(root, "before").Select(Node).ToImmutableArray() : initial;
            var environment = new GraphEnvironment("1.21.1", "fabric", "0.16");
            if (root.TryGetProperty("environment", out var env)) environment = new(Text(env, "MinecraftVersion"), Text(env, "Loader"), Text(env, "LoaderVersion"), Flag(env, "JavaDeclared"), Text(env, "JavaVersion"));
            var previous = GraphReportBuilder.Build(environment, [.. before.Select(value => value.Graph)]);
            var source = new Candidates(root);
            var result = DependencyResolver.SolveAsync(new(environment, before, initial, [.. Rows(root, "roots").Select(value => value.GetString()!)],
                Flag(root, "baseline") ? [.. previous.Errors.Select(value => value.Key)] : [], Flag(root, "strict"), Flag(root, "allow_downgrade"), PreviousReport: previous), source, limits, cancellation.Token).GetAwaiter().GetResult();
            using var bytes = new OutputBuffer();
            using (var writer = new Utf8JsonWriter(bytes))
            {
                writer.WriteStartObject(); writer.WriteBoolean("found", result.Solution is not null); writer.WriteNumber("visited", result.VisitedStates);
                writer.WriteStartArray("nodes");
                if (result.Solution is { } solution) foreach (var node in solution.Nodes)
                { writer.WriteStartObject(); writer.WriteString("id", node.Graph.Id); writer.WriteString("version", node.Graph.VersionId); writer.WriteEndObject(); }
                writer.WriteEndArray(); writer.WritePropertyName("cost");
                if (result.Solution is { } best)
                { writer.WriteStartArray(); writer.WriteNumberValue(best.Cost.Changed); writer.WriteNumberValue(best.Cost.ExplicitChanged); writer.WriteNumberValue(best.Cost.Added); writer.WriteNumberValue(best.Cost.NegativeFreshness); writer.WriteEndArray(); }
                else writer.WriteNullValue();
                writer.WritePropertyName("report"); if (result.Solution is { } found) GraphInspection.WriteReport(writer, found.Report); else writer.WriteNullValue();
                writer.WritePropertyName("last_report"); if (result.LastReport is { } last) GraphInspection.WriteReport(writer, last); else writer.WriteNullValue();
                writer.WriteStartArray("trace"); foreach (var call in source.Trace) writer.WriteStringValue(call); writer.WriteEndArray(); writer.WriteEndObject();
            }
            Console.OpenStandardOutput().Write(bytes.GetBuffer(), 0, checked((int)bytes.Length)); return 0;
        }
        catch (DomainException exception)
        { ManagedInspection.WriteFailure(exception.Id, exception.Message, exception.Category, exception.Target, exception.Hint, exception.Details); return 1; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or FormatException or OverflowException or InvalidOperationException or JsonException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", exception.Message, "InvalidData", null, "check solution inspection arguments", null); return 2; }
    }

    private static ResolutionNode Node(JsonElement value)
    {
        var item = value.GetProperty("Item");
        var mods = Rows(value, "Mods").Select(mod => new ArtifactMod(new(Text(mod, "Id"), Text(mod, "Version"), Text(mod, "Side", "both"),
            mod.TryGetProperty("Provides", out var aliases) ? [.. aliases.EnumerateObject().Select(alias => new ManifestAlias(ManifestAliasKey.FromString(alias.Name), alias.Value.GetString()!))] : [],
            [.. Rows(mod, "Requirements").Select(requirement => Requirement(requirement, 1))]), Flag(mod, "Nested"), [])).ToImmutableArray();
        var graph = new GraphNode(Text(value, "Id"), Text(value, "VersionId"), Text(item, "Name"), Text(item, "Kind", "mod"), Text(item, "Side", "both"),
            Flag(value, "Enabled", true), mods, [.. Rows(value, "Requirements").Select(requirement => Requirement(requirement, 1))], [.. Rows(value, "Warnings").Select(row => row.GetString()!)]);
        var publication = Text(value, "Published");
        var maven = value.TryGetProperty("RawVersion", out var raw) && raw.ValueKind == JsonValueKind.Object && Rows(raw, "loaders").Any(loader => loader.GetString() is "forge" or "neoforge");
        return new(graph, Flag(value, "Pinned"), Text(item, "Source").Equals("local", StringComparison.InvariantCultureIgnoreCase),
            Text(value, "Intent").Equals("explicit", StringComparison.InvariantCultureIgnoreCase), publication.Length == 0 ? null : DateTime.Parse(publication, CultureInfo.InvariantCulture), maven);
    }
    private static ManifestRequirement Requirement(JsonElement value, int depth)
    {
        if (depth > 64) throw new ArgumentException("Fixture requirement exceeds depth64.");
        return new(Text(value, "Target"), value.TryGetProperty("Range", out var range) ? VersionConstraint.FromJsonLiteral(range.GetRawText(), true) : VersionConstraint.Text("*"),
            Text(value, "Kind", "required"), Text(value, "Side", "both"), Text(value, "Scope", "mod"), Flag(value, "Maven") ? VersionScheme.Maven : VersionScheme.General,
            Text(value, "Source", "manifest"), Text(value, "SuggestedVersionId"), [.. Rows(value, "Any").Select(child => Requirement(child, depth + 1))],
            [.. Rows(value, "All").Select(child => Requirement(child, depth + 1))], value.TryGetProperty("Unless", out var unless) && unless.ValueKind != JsonValueKind.Null ? Requirement(unless, depth + 1) : null);
    }
    private static string Text(JsonElement value, string name, string fallback = "") => value.TryGetProperty(name, out var field) && field.ValueKind != JsonValueKind.Null ? field.GetString()! : fallback;
    private static bool Flag(JsonElement value, string name, bool fallback = false) => value.TryGetProperty(name, out var field) ? field.GetBoolean() : fallback;
    private static IEnumerable<JsonElement> Rows(JsonElement value, string name) => value.TryGetProperty(name, out var field) && field.ValueKind != JsonValueKind.Null ? field.EnumerateArray() : [];
    private sealed class Candidates : IResolutionCandidates
    {
        private readonly Dictionary<string, ImmutableArray<ResolutionNode>> nodes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> known = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Trace { get; } = [];
        public Candidates(JsonElement root)
        {
            foreach (var group in root.GetProperty("candidates").EnumerateObject()) nodes.Add(group.Name, [.. group.Value.EnumerateArray().Select(Node)]);
            if (root.TryGetProperty("known", out var flags)) foreach (var pair in flags.EnumerateObject()) known.Add(pair.Name, pair.Value.GetBoolean());
        }
        public ValueTask<ResolutionDomain> GetCandidatesAsync(string id, CancellationToken cancellation)
        {
            Trace.Add("domain:" + id); var values = nodes.GetValueOrDefault(id, []);
            return ValueTask.FromResult(new ResolutionDomain(!known.GetValueOrDefault(id, true) ? CandidateAvailability.Unknown : values.IsEmpty ? CandidateAvailability.KnownEmpty : CandidateAvailability.KnownAvailable,
                [.. values.Select(node => node.Graph.VersionId)]));
        }
        public ValueTask<ResolutionNode> GetNodeAsync(string id, string versionId, CancellationToken cancellation)
        { Trace.Add("candidate:" + id + "/" + versionId); return ValueTask.FromResult(nodes[id].First(node => node.Graph.VersionId == versionId)); }
    }
    private sealed class OutputBuffer : MemoryStream
    {
        private void Guard(int count)
        { if (count > 64 * 1024 * 1024 - Length) throw new DomainException("Compatibility.InspectionOutputLimit", "Solution inspection output exceeds64MiB", "inspect fewer solution facts", "LimitsExceeded", "MaximumOutputBytes"); }
        public override void Write(byte[] buffer, int offset, int count) { Guard(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Guard(buffer.Length); base.Write(buffer); }
    }
}
