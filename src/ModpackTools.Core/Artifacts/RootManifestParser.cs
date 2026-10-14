using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using ModpackTools.Core.Data;
using ModpackTools.Core.Versions;
using Newtonsoft.Json;

namespace ModpackTools.Core.Artifacts;

public static class RootManifestParser
{
    public static RootManifestDeclarations Parse(string? fabric, string? quilt, string loader, RootManifestLimits? limits = null)
        => ParseOwned(fabric, quilt, loader, limits ?? new(), null);

    internal static RootManifestDeclarations ParseOwned(string? fabric, string? quilt, string loader, RootManifestLimits limits, DeclarationAllowance? allowance)
    {
        ArgumentNullException.ThrowIfNull(loader);
        limits ??= new();
        ValidatePolicy(limits);
        var scope = new Scope(limits, allowance);
        var canTraverse = true;
        try
        {
            var isQuilt = string.Equals(loader, "quilt", StringComparison.InvariantCultureIgnoreCase);
            scope.Input(fabric, "fabric.mod.json");
            if (isQuilt) scope.Input(quilt, "quilt.mod.json");
            if (isQuilt && !string.IsNullOrEmpty(quilt)) scope.Quilt(quilt);
            else if ((isQuilt || string.Equals(loader, "fabric", StringComparison.InvariantCultureIgnoreCase)) && !string.IsNullOrEmpty(fabric)) scope.Fabric(fabric);
            if (scope.Mods.Count == 0) scope.Warn(new("NoSupportedManifest", "No supported " + loader + " mod manifest found", "", null, null));
        }
        catch (RootArchiveEntryException exception)
        { canTraverse = false; scope.Diagnostics.Add(exception.Diagnostic); }
        catch (ManifestLimitException exception)
        {
            canTraverse = false;
            scope.Diagnostics.Add(new("DeclarationLimit", "Manifest could not be fully read: declaration resource limit exceeded",
                scope.Manifest, scope.Operation + ": " + exception.Message, "inspect the declaration or use explicit quotas within the depth bounds"));
        }
        catch (Exception exception) when (exception is JsonReaderException or FormatException or InvalidOperationException or ArgumentException
            or OverflowException or CacheJsonLimitException or System.Text.Json.JsonException)
        {
            canTraverse = false;
            var limited = exception is CacheJsonLimitException || exception is JsonReaderException
                && exception.Message.StartsWith("The reader's MaxDepth", StringComparison.Ordinal);
            scope.Diagnostics.Add(new(limited ? "DeclarationLimit" : "ManifestIncomplete", "Manifest could not be fully read: " + scope.Operation,
                scope.Manifest, exception.Message, "check the selected manifest field and declaration types"));
        }
        var status = !canTraverse || scope.Diagnostics.Any(item => item.Code != "NoSupportedManifest") ? RootManifestStatus.Incomplete
            : scope.Mods.Count == 0 ? RootManifestStatus.Missing : RootManifestStatus.Complete;
        return new([.. scope.Mods], [.. scope.Diagnostics], scope.NestedPaths, status, canTraverse);
    }

    internal static void ValidatePolicy(RootManifestLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var invalid = limits.MaximumTextCharacters <= 0 ? nameof(limits.MaximumTextCharacters)
            : limits.MaximumTokenCharacters <= 0 ? nameof(limits.MaximumTokenCharacters)
            : limits.MaximumNodes <= 0 ? nameof(limits.MaximumNodes)
            : limits.MaximumJsonDepth is < 1 or > 64 ? nameof(limits.MaximumJsonDepth)
            : limits.MaximumRequirements <= 0 ? nameof(limits.MaximumRequirements)
            : limits.MaximumRequirementDepth is < 1 or > 64 ? nameof(limits.MaximumRequirementDepth)
            : limits.MaximumAliases <= 0 ? nameof(limits.MaximumAliases)
            : limits.MaximumNestedPaths <= 0 ? nameof(limits.MaximumNestedPaths) : null;
        if (invalid is not null) throw new DomainException("Metadata.InvalidDeclarationPolicy", "Root-manifest limits are invalid",
            "use positive declaration quotas and depths between 1 and 64", target: invalid);
    }

    private sealed class Scope(RootManifestLimits limits, DeclarationAllowance? allowance)
    {
        public List<DeclaredMod> Mods { get; } = [];
        public List<ManifestDiagnostic> Diagnostics { get; } = [];
        public void Warn(ManifestDiagnostic diagnostic)
        { if (allowance?.Diagnostic(diagnostic.Code, Diagnostics.Count) != false) Diagnostics.Add(diagnostic); }
        public ImmutableArray<string> NestedPaths { get; private set; } = [];
        public string Manifest { get; private set; } = "fabric.mod.json";
        public string Operation { get; private set; } = "decode JSON";
        private int requirements;
        private int aliasSlots;
        private readonly Dictionary<OrderedManifestValue, object> opaqueIdentities = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<OrderedManifestValue, string> formattedText = new(ReferenceEqualityComparer.Instance);
        private string Text(OrderedManifestValue value)
        {
            if (value is not (ManifestArray or ManifestInteger or ManifestBigInteger or ManifestFloat or ManifestDate))
                return OrderedManifestProjection.String(value);
            if (formattedText.TryGetValue(value, out var text)) return text;
            text = OrderedManifestProjection.String(value);
            formattedText.Add(value, text);
            return text;
        }
        public void Input(string? text, string manifest)
        {
            Manifest = manifest;
            Operation = "read text";
            if (text?.Length > limits.MaximumTextCharacters) throw new ManifestLimitException(nameof(limits.MaximumTextCharacters));
        }
        private OrderedManifestValue Decode(string text, string manifest)
        { Manifest = manifest; Operation = "decode JSON"; return OrderedManifestReader.Read(text, limits); }
        private OrderedManifestValue Dot(OrderedManifestValue value, string name)
        { Operation = "read property '" + name + "'"; return OrderedManifestProjection.Dot(value, name); }
        private OrderedManifestValue Index(OrderedManifestValue value, string name)
        { Operation = "read field '" + name + "'"; return OrderedManifestProjection.Index(value, name); }
        public void Fabric(string text)
        {
            var json = Decode(text, "fabric.mod.json");
            if (OrderedManifestProjection.SchemaMismatch(Dot(json, "schemaVersion"))) Schema("Fabric");
            allowance?.Mod(Mods.Count);
            var declared = ImmutableArray.CreateBuilder<ManifestRequirement>();
            foreach (var group in new[] { "depends", "breaks", "conflicts", "recommends", "suggests" })
            {
                if (Index(json, group) is not ManifestMap map) continue;
                var kind = group switch { "depends" => "required", "breaks" => "incompatible", "conflicts" => "discouraged", _ => "recommended" };
                // Actual entries cannot be shadowed by a source key named Keys.
                foreach (var entry in map.Entries) declared.Add(Requirement(entry.Key, entry.Value, kind, 1));
            }
            var aliases = new AliasBuilder();
            foreach (var alias in OrderedManifestProjection.Elements(Index(json, "provides")))
                if (OrderedManifestProjection.Truthy(alias))
                {
                    BeginAlias();
                    aliases.Add(AliasKey(alias), Text(Dot(json, "version")));
                }
            var id = Text(Dot(json, "id"));
            var version = Text(Dot(json, "version"));
            var side = Text(Index(json, "environment"));
            Mods.Add(new(id, version, side, aliases.Finish(), declared.ToImmutable()));
            var paths = ImmutableArray.CreateBuilder<OrderedManifestValue>();
            foreach (var jar in OrderedManifestProjection.Elements(Index(json, "jars")))
                if (OrderedManifestProjection.Truthy(jar))
                    foreach (var file in OrderedManifestProjection.Elements(Dot(jar, "file")))
                    {
                        if (!OrderedManifestProjection.Truthy(file)) continue;
                        if (paths.Count >= limits.MaximumNestedPaths) throw new ManifestLimitException(nameof(limits.MaximumNestedPaths));
                        paths.Add(file);
                    }
            SetPaths(paths);
        }
        public void Quilt(string text)
        {
            var json = Decode(text, "quilt.mod.json");
            var quilt = Dot(json, "quilt_loader");
            allowance?.Mod(Mods.Count);
            var declared = ImmutableArray.CreateBuilder<ManifestRequirement>();
            foreach (var group in new[] { "depends", "breaks" })
                foreach (var dependency in OrderedManifestProjection.Elements(Index(quilt, group)))
                    if (dependency is not ManifestNull) declared.Add(QuiltRequirement(dependency, group == "breaks" ? "incompatible" : "required", 1));
            var aliases = new AliasBuilder();
            foreach (var alias in OrderedManifestProjection.Elements(Index(quilt, "provides")))
            {
                if (alias is ManifestString literal)
                {
                    BeginAlias();
                    aliases.Add(ManifestAliasKey.Text(literal.Value), Text(Dot(quilt, "version")));
                }
                else if (OrderedManifestProjection.Truthy(alias))
                {
                    BeginAlias();
                    var id = Text(Dot(alias, "id"));
                    var version = Index(alias, "version");
                    aliases.Add(ManifestAliasKey.Text(id), Text(
                        OrderedManifestProjection.Truthy(version) ? Dot(alias, "version") : Dot(quilt, "version")));
                }
            }
            var side = "both";
            if (json is ManifestMap root && root.TryGet("minecraft", out var minecraft))
            {
                Operation = "read minecraft environment";
                if (minecraft is not ManifestMap game) throw new InvalidOperationException("minecraft must support an environment field lookup.");
                if (game.TryGet("environment", out _)) side = Text(Dot(minecraft, "environment"));
            }
            Mods.Add(new(Text(Dot(quilt, "id")), Text(Dot(quilt, "version")),
                side, aliases.Finish(), declared.ToImmutable()));
            SetPaths(OrderedManifestProjection.Elements(Index(quilt, "jars")));
            if (OrderedManifestProjection.SchemaMismatch(Dot(json, "schema_version"))) Schema("Quilt");
        }
        private void Schema(string loader) => Warn(new("UnknownSchema", "Unknown " + loader + " schema version", Manifest, null, null));
        private void BeginRequirement(int depth)
        {
            Operation = "construct dependency requirement";
            if (depth > limits.MaximumRequirementDepth) throw new ManifestLimitException(nameof(limits.MaximumRequirementDepth));
            if (requirements >= limits.MaximumRequirements) throw new ManifestLimitException(nameof(limits.MaximumRequirements));
            requirements++;
        }
        private ManifestRequirement Requirement(string target, OrderedManifestValue range, string kind, int depth)
        {
            BeginRequirement(depth);
            return BuildRequirement(target, range, kind, [], [], null);
        }
        private static ManifestRequirement BuildRequirement(string target, OrderedManifestValue range, string kind,
            ImmutableArray<ManifestRequirement> any, ImmutableArray<ManifestRequirement> all, ManifestRequirement? unless) => new(
                target, OrderedManifestProjection.Range(range), OrderedManifestProjection.RangeDisplay(range), kind, "both", "mod",
                VersionScheme.General, "manifest", "", any, all, unless);
        private ManifestRequirement QuiltRequirement(OrderedManifestValue value, string kind, int depth)
        {
            BeginRequirement(depth);
            if (value is ManifestString text) return BuildRequirement(text.Value, new ManifestString("*"), kind, [], [], null);
            var target = Text(OrderedManifestProjection.Property(value, "id"));
            var range = OrderedManifestProjection.Property(value, "versions");
            if (OrderedManifestProjection.EqualTrue(OrderedManifestProjection.Property(value, "optional"))) kind = "optional";
            ImmutableArray<ManifestRequirement> Children(string operation)
            {
                var children = OrderedManifestProjection.Property(value, operation);
                return OrderedManifestProjection.Truthy(children) ?
                    [.. OrderedManifestProjection.Elements(children).Select(child => QuiltRequirement(child, "required", depth + 1))] : [];
            }
            var any = Children("any");
            var all = Children("all");
            if (value is ManifestArray array) any = [.. array.Values.Select(child => QuiltRequirement(child, "required", depth + 1))];
            var unlessValue = OrderedManifestProjection.Property(value, "unless");
            var unless = OrderedManifestProjection.Truthy(unlessValue) ? QuiltRequirement(unlessValue, "required", depth + 1) : null;
            return BuildRequirement(target, range, kind, any, all, unless);
        }
        private void BeginAlias()
        {
            Operation = "construct provided alias";
            if (aliasSlots >= limits.MaximumAliases) throw new ManifestLimitException(nameof(limits.MaximumAliases));
            aliasSlots++;
        }
        private ManifestAliasKey AliasKey(OrderedManifestValue value) => value switch
        {
            ManifestString text => ManifestAliasKey.Text(text.Value),
            ManifestInteger integer => ManifestAliasKey.Integer(integer.Value),
            ManifestBigInteger big => ManifestAliasKey.Big(big.Value),
            ManifestFloat number => ManifestAliasKey.Floating(number.Value),
            ManifestBoolean boolean => ManifestAliasKey.Boolean(boolean.Value),
            ManifestDate date => ManifestAliasKey.Date(date.Value),
            _ => OpaqueAlias(value)
        };
        private ManifestAliasKey OpaqueAlias(OrderedManifestValue value)
        {
            if (!opaqueIdentities.TryGetValue(value, out var identity)) opaqueIdentities[value] = identity = new object();
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes)) WriteValue(writer, value);
            return ManifestAliasKey.Opaque(value is ManifestArray, identity, System.Text.Encoding.UTF8.GetString(bytes.GetBuffer(), 0, checked((int)bytes.Length)));
        }
        private void SetPaths(IEnumerable<OrderedManifestValue> values)
        {
            Operation = "collect nested archive declarations";
            var paths = ImmutableArray.CreateBuilder<string>();
            foreach (var value in values)
            {
                if (!OrderedManifestProjection.Truthy(value)) continue;
                if (paths.Count >= limits.MaximumNestedPaths) throw new ManifestLimitException(nameof(limits.MaximumNestedPaths));
                paths.Add(Text(value));
            }
            NestedPaths = paths.ToImmutable();
        }
    }
    private sealed class AliasBuilder
    {
        private readonly List<ManifestAlias> values = [];
        private readonly Dictionary<ManifestAliasKey, int> indexes = [];
        public void Add(ManifestAliasKey key, string version)
        {
            if (indexes.TryGetValue(key, out var index)) values[index] = new(values[index].Key, version);
            else { indexes.Add(key, values.Count); values.Add(new(key, version)); }
        }
        public ImmutableArray<ManifestAlias> Finish() => [.. values];
    }
    private static void WriteValue(Utf8JsonWriter writer, OrderedManifestValue value)
    {
        switch (value)
        {
            case ManifestNull: writer.WriteNullValue(); break;
            case ManifestString text: writer.WriteStringValue(text.Value); break;
            case ManifestBoolean boolean: writer.WriteBooleanValue(boolean.Value); break;
            case ManifestInteger integer: writer.WriteNumberValue(integer.Value); break;
            case ManifestBigInteger big: writer.WriteRawValue(big.Value.ToString(CultureInfo.InvariantCulture)); break;
            case ManifestFloat number when double.IsFinite(number.Value):
                var numeric = number.Value.ToString("R", CultureInfo.InvariantCulture);
                writer.WriteRawValue(numeric.IndexOfAny(['.', 'e', 'E']) < 0 ? numeric + ".0" : numeric); break;
            case ManifestFloat number: writer.WriteStringValue(number.Value.ToString(CultureInfo.InvariantCulture)); break;
            case ManifestDate date: writer.WriteStringValue(CacheDateValue.Capture(date.Value).Key); break;
            case ManifestArray array:
                writer.WriteStartArray(); foreach (var child in array.Values) WriteValue(writer, child); writer.WriteEndArray(); break;
            case ManifestMap map:
                writer.WriteStartObject(); foreach (var field in map.Entries) { writer.WritePropertyName(field.Key); WriteValue(writer, field.Value); }
                writer.WriteEndObject(); break;
            default: throw new InvalidOperationException("Unexpected opaque alias value.");
        }
    }
}
