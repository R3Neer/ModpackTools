using System.Collections.Immutable;
using System.Text.RegularExpressions;
using ModpackTools.Core.Data;
using ModpackTools.Core.Versions;
using Newtonsoft.Json;

namespace ModpackTools.Core.Artifacts;

public sealed record ForgeManifestTexts(string? ModsToml = null, string? NeoForgeToml = null, string? Manifest = null, string? JarJar = null);
public sealed record ForgeManifestLimits
{
    public RootManifestLimits Declarations { get; init; } = new();
    public int MaximumMods { get; init; } = 100000;
    public int MaximumDiagnostics { get; init; } = 100000;
    public int MaximumTomlDepth { get; init; } = 64;
    public int MaximumSyntaxNodes { get; init; } = 1000000;
}

public static partial class ForgeRootManifestParser
{
    public static RootManifestDeclarations Parse(ForgeManifestTexts texts, string loader, ForgeManifestLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(texts); ArgumentNullException.ThrowIfNull(loader);
        return ParseEntries(entry => entry switch
        {
            LoaderManifestEntry.Forge => texts.ModsToml,
            LoaderManifestEntry.NeoForge => texts.NeoForgeToml,
            LoaderManifestEntry.Manifest => texts.Manifest,
            LoaderManifestEntry.JarJar => texts.JarJar,
            _ => throw new InvalidOperationException("Unexpected Forge text entry.")
        }, loader, limits ?? new());
    }

    internal static void ValidatePolicy(ForgeManifestLimits limits)
    {
        limits ??= new();
        RootManifestParser.ValidatePolicy(limits.Declarations);
        var invalid = limits.MaximumMods <= 0 ? nameof(limits.MaximumMods)
            : limits.MaximumDiagnostics <= 0 ? nameof(limits.MaximumDiagnostics)
            : limits.MaximumTomlDepth is < 1 or > 64 ? nameof(limits.MaximumTomlDepth)
            : limits.MaximumSyntaxNodes <= 0 ? nameof(limits.MaximumSyntaxNodes) : null;
        if (invalid is not null) throw new DomainException("Metadata.InvalidDeclarationPolicy", "Forge declaration limits are invalid",
            "use positive quotas and TOML depths between 1 and 64", target: invalid);
    }

    // One owned context controls decode/read stages. The public text boundary's
    // entry source is pure; only the internal archive facade supplies IO here.
    internal static RootManifestDeclarations ParseEntries(Func<LoaderManifestEntry, string?> read, string loader, ForgeManifestLimits limits, DeclarationAllowance? allowance = null)
    {
        ValidatePolicy(limits);
        var scope = new Scope(limits, allowance);
        var traverse = true;
        try
        {
            var neo = string.Equals(loader, "neoforge", StringComparison.InvariantCultureIgnoreCase);
            if (neo || string.Equals(loader, "forge", StringComparison.InvariantCultureIgnoreCase))
            {
                var neoText = neo ? read(LoaderManifestEntry.NeoForge) : null;
                if (neo) scope.Input(neoText, "META-INF/neoforge.mods.toml");
                var selected = neo && !string.IsNullOrEmpty(neoText) ? neoText : read(LoaderManifestEntry.Forge);
                var name = neo && !string.IsNullOrEmpty(neoText) ? "META-INF/neoforge.mods.toml" : "META-INF/mods.toml";
                scope.Input(selected, name);
                if (!string.IsNullOrEmpty(selected)) scope.Mods(selected, name, read);
                scope.JarJar(read(LoaderManifestEntry.JarJar));
            }
            if (scope.ModsResult.Count == 0) scope.Warn("NoSupportedManifest", "No supported " + loader + " mod manifest found");
        }
        catch (RootArchiveEntryException exception)
        {
            traverse = false;
            scope.Diagnostics.Add(exception.Diagnostic);
        }
        catch (ManifestLimitException exception)
        {
            traverse = false;
            // Failure diagnostics are a reserved final slot outside the warning quota.
            scope.Diagnostics.Add(new("DeclarationLimit", "Manifest could not be fully read: declaration resource limit exceeded",
                scope.Manifest, scope.Operation + ": " + exception.Message, "inspect the declaration or use explicit quotas within the depth bounds"));
        }
        catch (Exception exception) when (exception is JsonReaderException or FormatException or InvalidOperationException
            or ArgumentException or OverflowException or CacheJsonLimitException)
        {
            traverse = false;
            var limited = exception is CacheJsonLimitException || exception is JsonReaderException
                && exception.Message.StartsWith("The reader's MaxDepth", StringComparison.Ordinal);
            scope.Diagnostics.Add(new(limited ? "DeclarationLimit" : "ManifestIncomplete", "Manifest could not be fully read: " + scope.Operation,
                scope.Manifest, exception.Message, "check the selected manifest field and declaration types"));
        }
        var status = !traverse || scope.Diagnostics.Any(item => item.Code != "NoSupportedManifest") ? RootManifestStatus.Incomplete
            : scope.ModsResult.Count == 0 ? RootManifestStatus.Missing : RootManifestStatus.Complete;
        return new([.. scope.ModsResult], [.. scope.Diagnostics], scope.Paths, status, traverse);
    }
    private sealed class Scope(ForgeManifestLimits limits, DeclarationAllowance? allowance)
    {
        public List<DeclaredMod> ModsResult { get; } = [];
        public List<ManifestDiagnostic> Diagnostics { get; } = [];
        public ImmutableArray<string> Paths { get; private set; } = [];
        public string Manifest { get; private set; } = "";
        public string Operation { get; private set; } = "read text";
        private int requirements;
        private readonly Dictionary<TomlDeclarationValue, string> textValues = new(ReferenceEqualityComparer.Instance);
        private readonly ConstraintScope constraints = new(limits.Declarations);
        public void Input(string? text, string manifest)
        { Manifest = manifest; Operation = "read text"; if (text?.Length > limits.Declarations.MaximumTextCharacters) throw new ManifestLimitException(nameof(limits.Declarations.MaximumTextCharacters)); }
        public void Warn(string code, string message)
        {
            if (allowance?.Diagnostic(code, Diagnostics.Count) == false) return;
            if (Diagnostics.Count >= limits.MaximumDiagnostics) throw new ManifestLimitException(nameof(limits.MaximumDiagnostics));
            Diagnostics.Add(new(code, message, Manifest, null, null));
        }
        private TomlDeclarationValue Field(TomlDeclarationValue value, string key)
        { Operation = "read field '" + key + "'"; return TomlDeclarationProjection.Index(value, key); }
        private bool Has(TomlDeclarationValue value, string key)
        { Operation = "test field '" + key + "'"; return TomlDeclarationProjection.Contains(value, key); }
        private string Text(TomlDeclarationValue value) => Projection(value, textValues, false);
        private string Projection(TomlDeclarationValue value, Dictionary<TomlDeclarationValue, string> cache, bool display)
        {
            if (value is not (TomlFields or TomlSequence))
                return display ? TomlDeclarationProjection.RangeDisplay(value) : TomlDeclarationProjection.String(value);
            if (cache.TryGetValue(value, out var result)) return result;
            result = display ? TomlDeclarationProjection.RangeDisplay(value) : TomlDeclarationProjection.String(value);
            cache.Add(value, result);
            return result;
        }
        private ManifestRequirement Requirement(string target, TomlDeclarationValue range, string kind = "required", string side = "both")
        {
            if (++requirements > limits.Declarations.MaximumRequirements) throw new ManifestLimitException(nameof(limits.Declarations.MaximumRequirements));
            return new(target, constraints.Toml(range), kind, side, "mod", VersionScheme.Maven, "manifest", "", [], [], null);
        }
        public void Mods(string text, string name, Func<LoaderManifestEntry, string?> read)
        {
            Manifest = name; Operation = "decode TOML";
            var data = TomlDeclarationReader.Read(text, limits);
            var manifest = read(LoaderManifestEntry.Manifest);
            Input(manifest, "META-INF/MANIFEST.MF");
            var jarVersion = JarVersion().Match(manifest ?? "").Groups[1].Value;
            Manifest = name;
            foreach (var mod in TomlDeclarationProjection.Elements(Field(data, "mods")))
            {
                allowance?.Mod(ModsResult.Count);
                if (ModsResult.Count >= limits.MaximumMods) throw new ManifestLimitException(nameof(limits.MaximumMods));
                var declared = ImmutableArray.CreateBuilder<ManifestRequirement>();
                var dependencies = Field(data, "dependencies");
                if (TomlDeclarationProjection.Truthy(dependencies) && Has(dependencies, Text(Field(mod, "modId"))))
                {
                    foreach (var dependency in TomlDeclarationProjection.Elements(Field(dependencies, Text(Field(mod, "modId")))))
                    {
                        var kind = Has(dependency, "type") ? Text(Field(dependency, "type"))
                            : TomlDeclarationProjection.EqualFalse(Field(dependency, "mandatory")) ? "optional" : "required";
                        declared.Add(Requirement(Text(Field(dependency, "modId")), Field(dependency, "versionRange"), kind, Text(Field(dependency, "side")).ToLowerInvariant()));
                        if (Has(dependency, "ordering") && TomlDeclarationProjection.NotNone(Field(dependency, "ordering")))
                            Warn("LoadOrdering", Text(Field(mod, "modId")) + ": load ordering is not verified");
                    }
                }
                var loaderVersion = Field(data, "loaderVersion");
                if (TomlDeclarationProjection.Truthy(loaderVersion)) declared.Add(Requirement(Text(Field(data, "modLoader")), loaderVersion));
                var selected = Has(mod, "features") ? Field(mod, "features")
                    : Has(data, "features") && Has(Field(data, "features"), Text(Field(mod, "modId")))
                        ? Field(Field(data, "features"), Text(Field(mod, "modId"))) : TomlMissing.Value;
                var features = TomlDeclarationProjection.Pipeline(selected);
                if (TomlDeclarationProjection.Truthy(features))
                {
                    Operation = "read selected features Keys";
                    foreach (var feature in TomlDeclarationProjection.FeatureKeys(features))
                    {
                        if (string.Equals(feature, "javaVersion", StringComparison.InvariantCultureIgnoreCase)
                            || string.Equals(feature, "java_version", StringComparison.InvariantCultureIgnoreCase))
                            declared.Add(Requirement("java", Field(features, feature)));
                        else Warn("RuntimeFeature", Text(Field(mod, "modId")) + ": runtime feature '" + feature + "' is not verified");
                    }
                }
                var version = Text(Field(mod, "version")).Replace("${file.jarVersion}", jarVersion, StringComparison.Ordinal);
                if (version.Contains("${", StringComparison.Ordinal) || version.Length == 0)
                    Warn("VersionSubstitution", Text(Field(mod, "modId")) + ": version substitution could not be evaluated");
                ModsResult.Add(new(Text(Field(mod, "modId")), version, "both", [], declared.ToImmutable()));
            }
        }
        public void JarJar(string? text)
        {
            Input(text, "META-INF/jarjar/metadata.json");
            if (string.IsNullOrEmpty(text)) return;
            Operation = "decode Jar-in-Jar JSON";
            var value = OrderedManifestReader.Read(text, limits.Declarations);
            Operation = "read Jar-in-Jar paths";
            var paths = ImmutableArray.CreateBuilder<string>();
            foreach (var jar in OrderedManifestProjection.Elements(OrderedManifestProjection.Dot(value, "jars")))
            {
                // Only truthy source paths reach the child lookup; casts happen later.
                foreach (var path in OrderedManifestProjection.Elements(OrderedManifestProjection.Dot(jar, "path")))
                {
                    if (!OrderedManifestProjection.Truthy(path)) continue;
                    if (paths.Count >= limits.Declarations.MaximumNestedPaths) throw new ManifestLimitException(nameof(limits.Declarations.MaximumNestedPaths));
                    paths.Add(OrderedManifestProjection.String(path));
                }
            }
            Paths = paths.ToImmutable();
            Warn("JarSelection", "Jar-in-Jar library selection ranges require runtime verification");
        }
    }
    [GeneratedRegex(@"^Implementation-Version:\s*([^\r\n]+)", RegexOptions.Multiline | RegexOptions.NonBacktracking)]
    private static partial Regex JarVersion();
}
