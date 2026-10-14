using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Data;
using ModpackTools.Core.Graph;
using ModpackTools.Core.Versions;

internal static class RequirementInspection
{
    public static int Run(string[] arguments)
    {
        try
        {
            if (arguments.Length < 2 || (arguments.Length - 2) % 2 != 0) throw new ArgumentException("Use --inspect-requirement <case-json-file> [--option value].");
            var evaluation = new RequirementEvaluationLimits(); var contextLimits = new RequirementContextLimits();
            var ranges = new VersionRangeLimits(); var comparison = new VersionComparisonLimits();
            var comparer = StringComparer.OrdinalIgnoreCase;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 2; index < arguments.Length; index += 2)
            {
                if (!seen.Add(arguments[index])) throw new ArgumentException("Duplicate requirement inspection option.");
                var value = arguments[index + 1]; int Number() => int.Parse(value, CultureInfo.InvariantCulture);
                switch (arguments[index])
                {
                    case "--culture": CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(value); break;
                    case "--member-case-insensitive": comparer = bool.Parse(value) ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal; break;
                    case "--max-requirement-nodes": evaluation = evaluation with { MaximumNodes = Number() }; break;
                    case "--max-version-checks": evaluation = evaluation with { MaximumVersionChecks = Number() }; break;
                    case "--max-requirement-depth": evaluation = evaluation with { MaximumDepth = Number() }; break;
                    case "--max-context-projects": contextLimits = contextLimits with { MaximumProjects = Number() }; break;
                    case "--max-context-mod-keys": contextLimits = contextLimits with { MaximumModKeys = Number() }; break;
                    case "--max-context-versions": contextLimits = contextLimits with { MaximumVersions = Number() }; break;
                    case "--max-context-chars": contextLimits = contextLimits with { MaximumTextCharacters = Number() }; break;
                    case "--max-context-rows": contextLimits = contextLimits with { MaximumRows = Number() }; break;
                    case "--max-range-chars": ranges = ranges with { MaximumTextCharacters = Number() }; break;
                    case "--max-range-nodes": ranges = ranges with { MaximumNodes = Number() }; break;
                    case "--max-range-comparisons": ranges = ranges with { MaximumComparisons = Number() }; break;
                    case "--max-range-depth": ranges = ranges with { MaximumDepth = Number() }; break;
                    case "--max-version-chars": comparison = comparison with { MaximumTextCharacters = Number() }; break;
                    case "--max-version-digits": comparison = comparison with { MaximumNumericDigits = Number() }; break;
                    case "--max-version-items": comparison = comparison with { MaximumItems = Number() }; break;
                    case "--max-version-depth": comparison = comparison with { MaximumDepth = Number() }; break;
                    default: throw new ArgumentException("Unsupported requirement inspection option.");
                }
            }
            using var input = new FileStream(arguments[1], FileMode.Open, FileAccess.Read, FileShare.Read);
            using var bytes = new MemoryStream();
            var buffer = new byte[4096]; int count;
            while ((count = input.Read(buffer)) != 0)
            { if (count > 4 * 1024 * 1024 - bytes.Length) throw new ArgumentException("Requirement inspection input exceeds4MiB."); bytes.Write(buffer, 0, count); }
            using var document = JsonDocument.Parse(bytes.GetBuffer().AsMemory(0, checked((int)bytes.Length)), new() { MaxDepth = 256 });
            var root = document.RootElement;
            if (!seen.Contains("--culture") && root.TryGetProperty("culture", out var culture)) CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture.GetString()!);
            var projects = ImmutableArray.CreateBuilder<RequirementProject>(); var mods = ImmutableArray.CreateBuilder<RequirementModVersions>();
            if (root.TryGetProperty("projects", out var projectMap))
                foreach (var entry in projectMap.EnumerateObject())
                {
                    if (entry.Value.ValueKind != JsonValueKind.String) throw new ArgumentException("Project version IDs must be non-null strings.");
                    projects.Add(new(entry.Name, entry.Value.GetString()!));
                }
            if (root.TryGetProperty("mods", out var modMap))
                foreach (var entry in modMap.EnumerateObject())
                {
                    var values = ImmutableArray.CreateBuilder<string?>();
                    foreach (var version in entry.Value.EnumerateArray())
                    {
                        if (version.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) throw new ArgumentException("Mod versions must be strings or null.");
                        values.Add(version.GetString());
                    }
                    mods.Add(new(entry.Name, values.ToImmutable()));
                }
            var nodes = 0;
            ManifestRequirement Requirement(JsonElement value, int depth)
            {
                if (++nodes > 100000 || depth > 64) throw new ArgumentException("Requirement inspection declaration exceeds its input bound.");
                string Text(string name, string fallback = "") => value.TryGetProperty(name, out var field) ? field.GetString() ?? "" : fallback;
                ImmutableArray<ManifestRequirement> Children(string name)
                {
                    if (!value.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null) return [];
                    var children = ImmutableArray.CreateBuilder<ManifestRequirement>();
                    foreach (var child in field.EnumerateArray()) children.Add(Requirement(child, depth + 1));
                    return children.ToImmutable();
                }
                var range = value.TryGetProperty("Range", out var raw) ? VersionRangeDeclaration.Read(raw, memberComparer: comparer) : VersionRange.Text("*");
                var unless = value.TryGetProperty("Unless", out var condition) && condition.ValueKind != JsonValueKind.Null ? Requirement(condition, depth + 1) : null;
                return new(Text("Target"), range, "", Text("Kind", "required"), Text("Side", "both"), Text("Scope", "mod"),
                    value.TryGetProperty("Maven", out var maven) && maven.GetBoolean() ? VersionScheme.Maven : VersionScheme.General,
                    Text("Source", "manifest"), Text("SuggestedVersionId"), Children("Any"), Children("All"), unless);
            }
            var context = RequirementContext.Create(projects.ToImmutable(), mods.ToImmutable(), contextLimits);
            var result = RequirementEvaluator.Evaluate(Requirement(root.GetProperty("requirement"), 1), context, evaluation with { Ranges = ranges with { Comparison = comparison } });
            Console.Write(result is null ? "null" : result.Value ? "true" : "false"); return 0;
        }
        catch (DomainException exception)
        { ManagedInspection.WriteFailure(exception.Id, exception.Message, exception.Category, exception.Target, exception.Hint, exception.Details); return 1; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or FormatException or OverflowException or InvalidOperationException or JsonException)
        { ManagedInspection.WriteFailure("Core.InputReadFailed", exception.Message, "InvalidData", null, "check requirement inspection input and options", null); return 2; }
    }
}
