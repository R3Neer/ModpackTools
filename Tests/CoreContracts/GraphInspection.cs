using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Graph;
using ModpackTools.Core.Versions;

internal static class GraphInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            if (arguments.Length < 2 || (arguments.Length - 2) % 2 != 0) throw new ArgumentException("Use --inspect-graph-report <case-json-file> [--option value].");
            var limits = new GraphReportLimits(); var context = new RequirementContextLimits(); var evaluation = new RequirementEvaluationLimits();
            var seen = new HashSet<string>(StringComparer.Ordinal); var insensitive = true;
            var policy = arguments[0] == "--inspect-graph-policy";
            var strict = false; var build = false; var policyLimits = new GraphPolicyLimits();
            for (var index = 2; index < arguments.Length; index += 2)
            {
                if (!seen.Add(arguments[index])) throw new ArgumentException("Duplicate graph inspection option.");
                var value = arguments[index + 1]; int Number() => int.Parse(value, CultureInfo.InvariantCulture);
                switch (arguments[index])
                {
                    case "--strict" when policy: strict = bool.Parse(value); break;
                    case "--build" when policy: build = bool.Parse(value); break;
                    case "--max-policy-details-chars" when policy: policyLimits = new(Number()); break;
                    case "--culture": CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(value); break;
                    case "--member-case-insensitive": insensitive = bool.Parse(value); break;
                    case "--max-graph-nodes": limits = limits with { MaximumNodes = Number() }; break;
                    case "--max-graph-mods": limits = limits with { MaximumMods = Number() }; break;
                    case "--max-graph-aliases": limits = limits with { MaximumAliases = Number() }; break;
                    case "--max-graph-requirements": limits = limits with { MaximumRequirements = Number() }; break;
                    case "--max-graph-warnings": limits = limits with { MaximumWarnings = Number() }; break;
                    case "--max-graph-issues": limits = limits with { MaximumIssues = Number() }; break;
                    case "--max-graph-facts": limits = limits with { MaximumFacts = Number() }; break;
                    case "--max-graph-fact-versions": limits = limits with { MaximumFactVersions = Number() }; break;
                    case "--max-graph-input-chars": limits = limits with { MaximumTextCharacters = Number() }; break;
                    case "--max-graph-requirement-depth": limits = limits with { MaximumRequirementDepth = Number() }; break;
                    case "--max-graph-evaluated-nodes": limits = limits with { MaximumEvaluatedNodes = Number() }; break;
                    case "--max-graph-evaluated-versions": limits = limits with { MaximumEvaluatedVersions = Number() }; break;
                    case "--max-graph-identity-chars": limits = limits with { MaximumIdentityCharacters = Number() }; break;
                    case "--max-graph-message-chars": limits = limits with { MaximumMessageCharacters = Number() }; break;
                    case "--max-context-projects": context = context with { MaximumProjects = Number() }; break;
                    case "--max-context-mod-keys": context = context with { MaximumModKeys = Number() }; break;
                    case "--max-context-versions": context = context with { MaximumVersions = Number() }; break;
                    case "--max-context-chars": context = context with { MaximumTextCharacters = Number() }; break;
                    case "--max-context-rows": context = context with { MaximumRows = Number() }; break;
                    case "--max-requirement-nodes": evaluation = evaluation with { MaximumNodes = Number() }; break;
                    case "--max-version-checks": evaluation = evaluation with { MaximumVersionChecks = Number() }; break;
                    case "--max-requirement-depth": evaluation = evaluation with { MaximumDepth = Number() }; break;
                    default: throw new ArgumentException("Unsupported graph inspection option.");
                }
            }
            using var input = new FileStream(arguments[1], FileMode.Open, FileAccess.Read, FileShare.Read);
            using var bytes = new MemoryStream(); var buffer = new byte[4096]; int count;
            while ((count = input.Read(buffer)) != 0)
            { if (count > 4 * 1024 * 1024 - bytes.Length) throw new ArgumentException("Graph inspection input exceeds4MiB."); bytes.Write(buffer, 0, count); }
            using var document = JsonDocument.Parse(bytes.GetBuffer().AsMemory(0, (int)bytes.Length), new() { MaxDepth = 256 });
            var root = document.RootElement;
            if (!seen.Contains("--culture") && root.TryGetProperty("Culture", out var culture)) CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture.GetString()!);
            var report = Read(root, limits with { Context = context, Evaluation = evaluation }, insensitive);
            if (policy)
            {
                var baseline = root.TryGetProperty("Baseline", out var previous) && previous.ValueKind != JsonValueKind.Null
                    ? Read(previous, limits with { Context = context, Evaluation = evaluation }, insensitive) : null;
                GraphPolicy.Assert(report, baseline, strict, build, policyLimits);
                Console.Write("{\"Allowed\":true}"); return 0;
            }
            using var output = new OutputBuffer();
            using (var writer = new Utf8JsonWriter(output))
            {
                WriteReport(writer, report);
            }
            Console.OpenStandardOutput().Write(output.GetBuffer(), 0, (int)output.Length); return 0;

        }
        catch (DomainException exception)
        { ManagedInspection.WriteFailure(exception.Id, exception.Message, exception.Category, exception.Target, exception.Hint, exception.Details); return 1; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or FormatException or OverflowException or InvalidOperationException or JsonException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", exception.Message, "InvalidData", null, "check graph inspection arguments and input", null); return 2; }
    }
    internal static void WriteReport(Utf8JsonWriter writer, GraphReport report)
    {
        writer.WriteStartObject(); writer.WriteNumber("ValidatorRevision", GraphReport.ValidatorRevision); writer.WriteNumber("IssueKeySchema", GraphReport.IssueKeySchema);
        Issues("Issues", report.Issues); Issues("Errors", report.Errors); Issues("Unknown", report.Unknown); Issues("Warnings", report.Warnings);
        writer.WriteBoolean("Complete", report.Complete); writer.WriteEndObject();
        void Issues(string name, ImmutableArray<GraphIssue> values)
        {
            writer.WriteStartArray(name);
            foreach (var issue in values)
            {
                writer.WriteStartObject(); writer.WriteString("Key", issue.Key); writer.WriteString("Code", issue.Code); writer.WriteString("Owner", issue.Owner); writer.WriteString("OwnerName", issue.OwnerName);
                writer.WritePropertyName("Requirement");
                if (issue.Requirement is { } requirement) WriteRequirement(writer, requirement);
                else if (issue.WarningRequirement is { } warning) writer.WriteStringValue(warning); else writer.WriteNullValue();
                writer.WriteString("Side", issue.Side); writer.WriteString("Severity", issue.Severity); writer.WriteString("Message", issue.Message); writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
    }

    internal static GraphReport Read(JsonElement root, GraphReportLimits limits, bool insensitive = true)
    {
        var env = root.GetProperty("Environment");
        var environment = new GraphEnvironment(Text(env, "MinecraftVersion"), Text(env, "Loader"), Text(env, "LoaderVersion"),
            Boolean(env, "JavaDeclared", false), Text(env, "JavaVersion"));
        var nodes = ImmutableArray.CreateBuilder<GraphNode>(); var requirementCount = 0;
        foreach (var node in Array(root, "Nodes"))
        {
            if (nodes.Count >= 100000) throw new ArgumentException("Too many graph inspection nodes.");
            var mods = ImmutableArray.CreateBuilder<ArtifactMod>(); var warnings = ImmutableArray.CreateBuilder<string>();
            foreach (var warning in Array(node, "Warnings")) warnings.Add(warning.GetString() ?? throw new ArgumentException("Warning text cannot be null."));
            foreach (var mod in Array(node, "Mods"))
            {
                var aliases = ImmutableArray.CreateBuilder<ManifestAlias>();
                if (mod.TryGetProperty("Provides", out var provides)) foreach (var alias in provides.EnumerateObject())
                    aliases.Add(new(ManifestAliasKey.FromString(alias.Name), alias.Value.GetString() ?? throw new ArgumentException("Alias version cannot be null.")));
                mods.Add(new(new(Text(mod, "Id"), Text(mod, "Version"), Text(mod, "Side", "both"), aliases.ToImmutable(), Requirements(mod)), Boolean(mod, "Nested", false), []));
            }
            foreach (var manifest in Array(node, "Roots"))
            {
                var loader = Text(manifest, "Loader", environment.Loader);
                var parsed = manifest.TryGetProperty("ModsToml", out _) || manifest.TryGetProperty("NeoForgeToml", out _)
                    ? ForgeRootManifestParser.Parse(new(OptionalText(manifest, "ModsToml"), OptionalText(manifest, "NeoForgeToml"), OptionalText(manifest, "Manifest"), OptionalText(manifest, "JarJar")), loader)
                    : RootManifestParser.Parse(OptionalText(manifest, "Fabric"), OptionalText(manifest, "Quilt"), loader);
                foreach (var mod in parsed.Mods) mods.Add(new(mod, Boolean(manifest, "Nested", false), []));
                foreach (var warning in parsed.Diagnostics) warnings.Add(warning.Message);
            }
            nodes.Add(new(Text(node, "Id"), Text(node, "VersionId"), Text(node, "Name"), Text(node, "Kind", "mod"), Text(node, "Side", "both"),
                Boolean(node, "Enabled", true), mods.ToImmutable(), Requirements(node), warnings.ToImmutable()));
        }
        var overrides = root.TryGetProperty("Overrides", out var presence) ? new RuntimeOverrides(Boolean(presence, "FabricDependencies", false), Boolean(presence, "QuiltOverrides", false)) : new();
        return GraphReportBuilder.Build(environment, nodes.ToImmutable(), overrides, limits);
        ImmutableArray<ManifestRequirement> Requirements(JsonElement value) => [.. Array(value, "Requirements").Select(child => Requirement(child, 1))];
        ManifestRequirement Requirement(JsonElement value, int depth)
        {
            if (++requirementCount > 100000 || depth > 64) throw new ArgumentException("Graph inspection requirement declaration exceeds its input bound.");
            var constraint = value.TryGetProperty("Range", out var range) ? VersionConstraint.FromJsonLiteral(range.GetRawText(), insensitive) : VersionConstraint.Text("*");
            return new(Text(value, "Target"), constraint, Text(value, "Kind", "required"), Text(value, "Side", "both"), Text(value, "Scope", "mod"),
                Boolean(value, "Maven", false) ? VersionScheme.Maven : VersionScheme.General, Text(value, "Source", "manifest"), Text(value, "SuggestedVersionId"),
                [.. Array(value, "Any").Select(child => Requirement(child, depth + 1))], [.. Array(value, "All").Select(child => Requirement(child, depth + 1))],
                value.TryGetProperty("Unless", out var unless) && unless.ValueKind != JsonValueKind.Null ? Requirement(unless, depth + 1) : null);
        }
    }
    private static string Text(JsonElement value, string name, string fallback = "") => value.TryGetProperty(name, out var field)
        ? field.ValueKind == JsonValueKind.String ? field.GetString()! : throw new ArgumentException("Graph text fields must be non-null strings.") : fallback;
    private static string? OptionalText(JsonElement value, string name) => value.TryGetProperty(name, out var field) ? field.GetString() : null;
    private static bool Boolean(JsonElement value, string name, bool fallback) => value.TryGetProperty(name, out var field) ? field.GetBoolean() : fallback;
    private static IEnumerable<JsonElement> Array(JsonElement value, string name) => value.TryGetProperty(name, out var field) && field.ValueKind != JsonValueKind.Null ? field.EnumerateArray() : [];
    internal static void WriteRequirement(Utf8JsonWriter writer, ManifestRequirement requirement)
    {
        writer.WriteStartObject(); writer.WriteString("Target", requirement.Target); writer.WritePropertyName("Range"); writer.WriteRawValue(requirement.Constraint.PublicJson);
        writer.WriteString("Kind", requirement.Kind); writer.WriteString("Side", requirement.Side); writer.WriteString("Scope", requirement.Scope); writer.WriteBoolean("Maven", requirement.Scheme == VersionScheme.Maven);
        writer.WriteString("Source", requirement.Source); writer.WriteString("SuggestedVersionId", requirement.SuggestedVersionId);
        writer.WriteStartArray("Any"); if (!requirement.Any.IsDefault) foreach (var child in requirement.Any) WriteRequirement(writer, child); writer.WriteEndArray();
        writer.WriteStartArray("All"); if (!requirement.All.IsDefault) foreach (var child in requirement.All) WriteRequirement(writer, child); writer.WriteEndArray();
        writer.WritePropertyName("Unless"); if (requirement.Unless is null) writer.WriteNullValue(); else WriteRequirement(writer, requirement.Unless); writer.WriteEndObject();
    }
    private sealed class OutputBuffer : MemoryStream
    {
        private void Guard(int count)
        { if (count > 32 * 1024 * 1024 - Length) throw new DomainException("Compatibility.InspectionOutputLimit", "Graph inspection output exceeds32MiB", "inspect fewer graph issues", target: "MaximumOutputBytes"); }
        public override void Write(byte[] buffer, int offset, int count) { Guard(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Guard(buffer.Length); base.Write(buffer); }
    }
}
