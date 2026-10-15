using System.Collections.Immutable;
using System.Text.Json;
using ModpackTools.Core;
using ModpackTools.Core.Artifacts;
using ModpackTools.Core.Data;
using ModpackTools.Core.Graph;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Versions;

namespace ModpackTools.Cli;

internal static class ContentListCommand
{
    private const string Usage = "modpack content list [filters]";
    private static readonly string[] ValueOptions = ["type", "category", "side", "source", "state", "match"];

    public static async Task RunAsync(string command, string[] arguments, string? projectId, string? activeProject,
        string configurationDirectory, CancellationToken cancellation)
    {
        var values = Parse(arguments.Skip(1).ToArray());
        var effectiveId = ProjectSelection.EffectiveId(projectId, activeProject);
        var project = ProjectSelection.Find(ProjectReader.Discover(ConfigurationReader.ReadRoot(configurationDirectory)), effectiveId);
        ProjectStructure.Assert(project);
        cancellation.ThrowIfCancellationRequested();
        // Keep the raw view's lexical fields and error order. Health consumes semantic
        // install facts only after selection and publication of numbered references.
        var inventory = InventoryReader.Read(project);
        var now = DateTimeOffset.UtcNow;
        var category = values.GetValueOrDefault("category");
        if (category is not null) category = CategorySelector.ResolveFromProject(project.Root, project.Id, configurationDirectory, category, now, true);
        var view = InventoryReferences.Assign(InventorySelector.Select(inventory, new(
            values.GetValueOrDefault("type", "all"), category, values.GetValueOrDefault("side"),
            values.GetValueOrDefault("source"), values.GetValueOrDefault("state", "all"), values.GetValueOrDefault("match"))));
        cancellation.ThrowIfCancellationRequested();
        InventoryReferenceCache.Write(configurationDirectory, view, now);
        var health = await ProjectHealth.ReadAsync(project, configurationDirectory, now, values.ContainsKey("verify"), cancellation: cancellation).ConfigureAwait(false);
        using var output = new NativeOutputBuffer("Content list", "use narrower content filters");
        using (var writer = new Utf8JsonWriter(output, new() { MaxDepth = 256 }))
        {
            writer.WriteStartObject(); writer.WriteNumber("schema_version", 1); writer.WriteBoolean("ok", true);
            writer.WriteString("command", command); Strings(writer, "arguments", arguments);
            writer.WriteStartObject("data"); writer.WritePropertyName("project");
            JsonSerializer.Serialize(writer, PublicProject.From(project), WireJsonContext.Default.PublicProject);
            writer.WriteStartObject("summary"); writer.WriteNumber("mods", inventory.Mods.Length);
            writer.WriteNumber("resources_enabled", inventory.ResourceView.Active.Length);
            writer.WriteNumber("resources_disabled", inventory.ResourceView.Inactive.Length); writer.WriteNumber("shaders", inventory.Shaders.Length); writer.WriteEndObject();
            writer.WriteStartObject("health"); Issues(writer, "errors", health.Report.Errors);
            Issues(writer, "warnings", health.Report.Warnings); Issues(writer, "unknown", health.Report.Unknown); writer.WriteEndObject();
            writer.WriteStartObject("inventory"); Strings(writer, "filters", view.Selection.Filters);
            Strings(writer, "included_types", view.Selection.IncludedTypes.Select(Kind));
            writer.WriteNumber("total_matches", view.Selection.TotalMatches); writer.WriteStartArray("items");
            foreach (var item in view.Mods) Physical(writer, item, "");
            foreach (var item in view.ActiveResources)
            {
                var row = item.Item;
                Item(writer, item.Number, row.Id, row.Name, "resourcepack", null, row.Source.ToString().ToLowerInvariant(), row.Filename,
                    null, false, "active", row.Priority, null);
            }
            foreach (var item in view.InactiveResources) Physical(writer, item, "inactive");
            foreach (var item in view.Shaders) Physical(writer, item, "");
            writer.WriteEndArray(); writer.WriteEndObject();
            // The released machine contract also captures the resource renderer's
            // selected rows, including its conditional empty-section behavior.
            if (view.Selection.TotalMatches != 0 && view.Selection.IncludedTypes.Contains(ContentKind.ResourcePack) &&
                (view.Selection.Filters.IsEmpty || view.ActiveResources.Length + view.InactiveResources.Length != 0))
            {
                writer.WriteStartArray("resource_packs");
                foreach (var item in view.ActiveResources)
                {
                    var row = item.Item;
                    Item(writer, item.Number, row.Id, row.Name, "resourcepack", null, row.Source.ToString().ToLowerInvariant(), row.Filename,
                        null, false, "active", row.Priority, null);
                }
                foreach (var item in view.InactiveResources) Physical(writer, item, "inactive");
                writer.WriteEndArray();
            }
            writer.WriteEndObject(); writer.WriteEndObject();
        }
        cancellation.ThrowIfCancellationRequested();
        Console.OpenStandardOutput().Write(output.GetBuffer(), 0, checked((int)output.Length));
        Console.WriteLine();
    }

    private static Dictionary<string, string> Parse(string[] arguments)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var positionals = new List<string>();
        for (var index = 0; index < arguments.Length; index++)
        {
            var token = arguments[index];
            if (!token.StartsWith("--", StringComparison.Ordinal)) { positionals.Add(token); continue; }
            var equals = token.IndexOf('=');
            var name = equals < 0 ? token[2..] : token[2..equals];
            var value = equals < 0 ? null : token[(equals + 1)..];
            if (name.Equals("verify", StringComparison.OrdinalIgnoreCase))
            {
                if (value is not null) throw new DomainException("Option.UnexpectedValue", $"Option '--{name}' does not accept a value", $"use --{name} without an attached value", target: name);
                values[name] = ""; continue;
            }
            if (!ValueOptions.Contains(name, StringComparer.OrdinalIgnoreCase)) throw new DomainException("Option.Unknown", $"Option '--{name}' is not recognized for this command", "run the command with --help", target: name);
            if (value is null)
            {
                if (++index == arguments.Length || arguments[index].StartsWith("--", StringComparison.Ordinal))
                    throw new DomainException("Option.MissingValue", $"Option '--{name}' requires a value", $"--{name} <value>", target: name);
                value = arguments[index];
            }
            values[name] = value;
        }
        if (positionals.Count != 0)
        {
            var bare = positionals.FirstOrDefault(value => ValueOptions.Append("verify").Contains(value, StringComparer.OrdinalIgnoreCase));
            if (bare is not null) throw new DomainException("Option.MissingPrefix", $"Option '{bare}' must start with '--'", $"--{bare}", target: bare);
            throw new DomainException("Command.InvalidArguments", "The command arguments do not match the expected syntax", Usage,
                targetData: positionals.Count == 1 ? new TextTarget(positionals[0]) : new TextListTarget(positionals.ToArray()));
        }
        return values;
    }

    private static string Kind(ContentKind kind) => kind switch
    { ContentKind.Mod => "mod", ContentKind.ResourcePack => "resourcepack", ContentKind.ShaderPack => "shaderpack", _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
    private static void Strings(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    { writer.WriteStartArray(name); foreach (var value in values) writer.WriteStringValue(value); writer.WriteEndArray(); }
    private static void Physical(Utf8JsonWriter writer, NumberedPhysical value, string? state)
    {
        var row = value.Item;
        Item(writer, value.Number, row.Id, row.Name, Kind(row.Kind), row.Side, row.Source.ToString().ToLowerInvariant(), row.Filename,
            row.Category, row.Pinned ?? false, state, null, row.VersionId);
    }
    private static void Item(Utf8JsonWriter writer, int? number, string id, string name, string kind, string? side, string source,
        string? filename, string? category, bool pinned, string? state, int? priority, string? version)
    {
        writer.WriteStartObject(); if (number is { } n) writer.WriteNumber("number", n); else writer.WriteNull("number");
        writer.WriteString("id", id); writer.WriteString("name", name.Length == 0 ? null : name); writer.WriteString("type", kind);
        writer.WriteString("side", side); writer.WriteString("source", source); writer.WriteString("filename", filename);
        writer.WriteString("category", category); writer.WriteBoolean("pinned", pinned); writer.WriteString("state", state);
        if (priority is { } p) writer.WriteNumber("priority", p); else writer.WriteNull("priority");
        writer.WriteString("version_id", version); writer.WriteNull("project_id"); writer.WriteNull("slug"); writer.WriteEndObject();
    }
    internal static void Issues(Utf8JsonWriter writer, string name, ImmutableArray<GraphIssue> issues)
    {
        writer.WriteStartArray(name);
        foreach (var issue in issues)
        {
            writer.WriteStartObject(); writer.WriteString("Key", issue.Key); writer.WriteString("Code", issue.Code);
            writer.WriteString("Owner", issue.Owner); writer.WriteString("OwnerName", issue.OwnerName);
            writer.WritePropertyName("Requirement");
            if (issue.Requirement is { } requirement) Requirement(writer, requirement);
            else if (issue.WarningRequirement is { } warning) writer.WriteStringValue(warning); else writer.WriteNullValue();
            writer.WriteString("Side", issue.Side); writer.WriteString("Severity", issue.Severity); writer.WriteString("Message", issue.Message); writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
    private static void Requirement(Utf8JsonWriter writer, ManifestRequirement value)
    {
        writer.WriteStartObject(); writer.WriteString("Target", value.Target); writer.WritePropertyName("Range"); writer.WriteRawValue(value.Constraint.PublicJson);
        writer.WriteString("Kind", value.Kind); writer.WriteString("Side", value.Side); writer.WriteString("Scope", value.Scope);
        writer.WriteBoolean("Maven", value.Scheme == VersionScheme.Maven); writer.WriteString("Source", value.Source); writer.WriteString("SuggestedVersionId", value.SuggestedVersionId);
        writer.WriteStartArray("Any"); if (!value.Any.IsDefault) foreach (var child in value.Any) Requirement(writer, child); writer.WriteEndArray();
        writer.WriteStartArray("All"); if (!value.All.IsDefault) foreach (var child in value.All) Requirement(writer, child); writer.WriteEndArray();
        writer.WritePropertyName("Unless"); if (value.Unless is { } unless) Requirement(writer, unless); else writer.WriteNullValue(); writer.WriteEndObject();
    }
}
