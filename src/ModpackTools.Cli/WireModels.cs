using System.Text.Json.Serialization;
using ModpackTools.Core;

namespace ModpackTools.Cli;

internal sealed record VersionData(VersionInfo Version);
internal sealed record VersionEnvelope(int SchemaVersion, bool Ok, string Command, string[] Arguments, VersionData Data);
internal sealed record FailureEnvelope(int SchemaVersion, bool Ok, string Command, string[] Arguments, Failure Error);
internal sealed record Failure(string Id, string Message, string Category, string? Target);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(VersionEnvelope))]
[JsonSerializable(typeof(FailureEnvelope))]
internal partial class WireJsonContext : JsonSerializerContext;
