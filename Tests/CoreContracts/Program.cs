using ModpackTools.Core.Data;
using ModpackTools.Core.Inventory;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

if (args.FirstOrDefault() == "--inspect-forge-manifest") return RootManifestInspection.Run(args, true);
if (args.FirstOrDefault() == "--inspect-loader-archive-root") return RootManifestInspection.Run(args, archive: true);
if (args.FirstOrDefault() == "--inspect-loader-archive") return RootManifestInspection.Run(args, archive: true, traversal: true);
if (args.FirstOrDefault() == "--inspect-requirement") return RequirementInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-constraint-literal") return ConstraintInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-graph-report") return GraphInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-graph-policy") return GraphInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-provider-version-cache") return ProviderCacheInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-provider-acquisition") return ProviderAcquisitionInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-provider-candidates") return ProviderCandidateInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-artifact") return ArtifactProviderInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-project-health") return ProjectHealthInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-solution-search") return SolutionSearchInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-candidate-acquisition") return CandidateAcquisitionInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-project-lookup") return ProjectLookupInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-content-plan") return ContentPlanInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-project-transaction") return ProjectTransactionInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-search") return SearchInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-compatible-versions") return CompatibleVersionsInspection.Run(args);

if (args.FirstOrDefault() == "--inspect-psd-lookup")
{
    try
    {
        if (args.Length is not (3 or 5) || args.Length == 5 && args[3] != "--culture") throw new ArgumentException("Use --inspect-psd-lookup <file> <query-paths-json-array> [--culture value].");
        if (args.Length == 5) CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(args[4]);
        var imported = PsdReader.Parse(File.ReadAllText(args[1]));
        using var queries = JsonDocument.Parse(args[2], new JsonDocumentOptions { MaxDepth = 16 });
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes))
        {
            writer.WriteStartArray();
            foreach (var path in queries.RootElement.EnumerateArray())
            {
                PsdValue current = imported;
                var found = true;
                foreach (var key in path.EnumerateArray())
                {
                    if (current is PsdTable objectValue && key.ValueKind == JsonValueKind.String && objectValue.Entries.TryGetValue(key.GetString()!, out var field)) current = field;
                    else if (current is PsdArray array && key.TryGetInt32(out var index) && index >= 0 && index < array.Items.Length) current = array.Items[index];
                    else { found = false; break; }
                }
                writer.WriteStartObject();
                writer.WriteBoolean("found", found);
                writer.WritePropertyName("value");
                if (found) WriteData(writer, current); else writer.WriteNullValue();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        Console.OpenStandardOutput().Write(bytes.GetBuffer(), 0, checked((int)bytes.Length));
        return 0;
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or FormatException or InvalidOperationException or JsonException)
    {
        ManagedInspection.WriteFailure("Core.InputReadFailed", exception.Message, "InvalidData", null, "check PSD1 lookup inspection input", null);
        return 2;
    }
}

if (args.FirstOrDefault() == "--inspect-managed") return ManagedInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-local-zips") return LocalZipInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-jar-display") return JarDisplayInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-local-jars") return LocalJarInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-editorial") return EditorialInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-classified-mods") return ClassificationInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-resource-order") return ResourceOrderInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-resource-view") return ResourceViewInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-inventory") return FullInventoryInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-selected-inventory") return SelectionInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-categories") return CategoryInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-numbered-inventory") return NumberingInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-write-reference-cache") return CacheWriteInspection.Run(args);
if (args.FirstOrDefault() is "--inspect-read-reference-cache" or "--inspect-resolve-reference") return CacheReadInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-update-targets") return UpdateTargetInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-batch-selectors") return BatchSelectorInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-classification-targets") return ClassificationTargetInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-resource-targets") return ResourceTargetInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-default-options") return DefaultOptionsInspection.Run(args);
if (args.FirstOrDefault() is "--inspect-read-category-cache" or "--inspect-category-id") return CategoryCacheInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-category-view") return CategoryListInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-write-category-cache") return CategoryCacheWriteInspection.Run(args);
if (args.FirstOrDefault() is "--inspect-source-tree" or "--inspect-source-changes" or "--inspect-text-hash") return SourceInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-compare-version") return VersionOrderingInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-version-range") return VersionRangeInspection.Run(args);
if (args.FirstOrDefault() == "--inspect-root-manifest") return RootManifestInspection.Run(args);

// Test-only transport peer: arbitrary replies exercise adapter validation before
// any parent session mutation. This mode is never part of the shipping CLI.
if (args is ["--request-path", _] && Environment.GetEnvironmentVariable("MODPACKTOOLS_TEST_RESPONSE_PATH") is { } responsePath)
{
    Console.Write(File.ReadAllText(responsePath));
    return int.Parse(Environment.GetEnvironmentVariable("MODPACKTOOLS_TEST_RESPONSE_EXIT") ?? "0", CultureInfo.InvariantCulture);
}

if (args is ["--inspect-psd1", var inputPath])
{
    try
    {
        var parsed = PsdReader.Parse(File.ReadAllText(inputPath));
        using var writer = new Utf8JsonWriter(Console.OpenStandardOutput());
        WriteData(writer, parsed);
        writer.Flush();
        return 0;
    }
    catch (PsdParseException exception)
    {
        Console.Error.WriteLine(exception.Message);
        return 1;
    }
    catch (Exception exception) when (exception is OverflowException or FormatException or IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine("Inspector limitation or input access failure: " + exception.Message);
        return 2;
    }
}

static void WriteData(Utf8JsonWriter writer, PsdValue value)
{
    switch (value)
    {
        case PsdTable table:
            writer.WriteStartObject();
            foreach (var pair in table.Entries)
            {
                writer.WritePropertyName(pair.Key);
                WriteData(writer, pair.Value);
            }
            writer.WriteEndObject();
            break;
        case PsdArray array:
            writer.WriteStartArray();
            foreach (var item in array.Items) WriteData(writer, item);
            writer.WriteEndArray();
            break;
        case PsdString text: writer.WriteStringValue(text.Value); break;
        case PsdBoolean flag: writer.WriteBooleanValue(flag.Value); break;
        case PsdNull: writer.WriteNullValue(); break;
        case PsdNumber number:
            writer.WriteNumberValue(decimal.Parse(number.Literal, NumberStyles.Float, CultureInfo.InvariantCulture));
            break;
        default: throw new InvalidOperationException("Unknown data node");
    }
}

var table = PsdReader.Parse("# data\n@{ Root = 'C:\\O''Brien'; Flags = @($true, $false, $null)\n Number = -1.25e2; Title = \"hello`nworld\"; Nested = @{Id='á'} }");
if (table.Entries["ROOT"] is not PsdString { Value: "C:\\O'Brien" } ||
    table.Entries["Flags"] is not PsdArray { Items.Length: 3 } ||
    table.Entries["Number"] is not PsdNumber { Literal: "-1.25e2" } ||
    table.Entries["Title"] is not PsdString { Value: "hello\nworld" })
    throw new InvalidOperationException("Typed PSD1 data mismatch");

string[] invalid = ["@{Root=$(Get-Content secret)}", "@{Root=\"$env:HOME\"}", "@{A=1;a=2}", "@{'é'=1;'e\u0301'=2}", "@{'ffi'=1;'ﬃ'=2}", "@{'a'=1;'a\u00ad'=2}", "@{'a'=1;\"a`0\"=2}", "@{A='unterminated}", "@{A=1 B=2}", "@{A=[int]1}", "@{A=Get-Date}", "@{A=@(1 2)}", "@{}; Get-Date", "<# unclosed"];
foreach (var source in invalid)
{
    try { PsdReader.Parse(source); }
    catch (PsdParseException) { continue; }
    throw new InvalidOperationException("Unsafe/invalid data accepted");
}
var arrays = PsdReader.Parse("@{A=@(1,@(2,3));B=@(@(1,2),@(3,4));C=@(@(1,2));D=\"`u{41}`u{1F600}\"}");
var distinctKeys = PsdReader.Parse("@{First=@{'é'=1};Second=@{'e\u0301'=2};'ß'=1;'ss'=2;'MOD'=1;'ＭＯＤ'=2}");
if (distinctKeys.Entries.Count != 6 || !((PsdTable)distinctKeys.Entries["First"]).Entries.ContainsKey("e\u0301"))
    throw new InvalidOperationException("Imported lookup or independent table scopes changed");
if (arrays.Entries["A"] is not PsdArray a || a.Items.Length != 2 || a.Items[1] is not PsdArray { Items.Length: 2 } ||
    arrays.Entries["B"] is not PsdArray b || b.Items.Length != 2 || b.Items.Any(item => item is not PsdArray { Items.Length: 2 }) ||
    arrays.Entries["C"] is not PsdArray c || c.Items.Length != 2 || c.Items[0] is not PsdNumber { Literal: "1" } ||
    arrays.Entries["D"] is not PsdString { Value: "A😀" })
    throw new InvalidOperationException("PSD1 nested arrays or Unicode escape mismatch");
var comments = PsdReader.Parse("@{A=@(1 <#x#>, @(2,3));B=1 <#x#>, 2}");
if (comments.Entries["A"] is not PsdArray { Items.Length: 2 } commented || commented.Items[1] is not PsdArray { Items.Length: 2 } ||
    comments.Entries["B"] is not PsdArray { Items.Length: 2 })
    throw new InvalidOperationException("Comments broke comma expression semantics");
foreach (var source in new[] { "@{A=@(1,)}", "@{A=@(1 <#x#>,)}", "@{A=1\n,2}", "@{<#outer <#nested#> end#>A=1}", "@{A=\"`u{D800}\"}", "@{A=١}", "@{A=\"`u{}\"}" })
{
    try { PsdReader.Parse(source); }
    catch (PsdParseException) { continue; }
    throw new InvalidOperationException("Invalid literal accepted");
}
foreach (var path in args)
{
    var data = PsdReader.Parse(File.ReadAllText(path));
    if (data.Entries.Count == 0) throw new InvalidOperationException("Real data fixture unexpectedly empty");
}
string[] limits = ["@{1='one'}", "@{A=" + string.Concat(Enumerable.Repeat("@(", 70)) + "1" + new string(')', 70) + "}", new string(' ', 4 * 1024 * 1024 + 1), "@{A=@(" + string.Join(',', Enumerable.Repeat("1", 100001)) + ")}"];
foreach (var source in limits)
{
    try { PsdReader.Parse(source); }
    catch (PsdParseException) { continue; }
    throw new InvalidOperationException("Unsupported key or parser limit not enforced");
}
if (PsdNumbers.Floating("99999999999999999999999999999999") != 9.999999999999999E31 ||
    PsdNumbers.Display("1.234567891234567") != "1.23456789123457" ||
    PsdNumbers.Display("1.0000000000000002") != "1" ||
    PsdNumbers.Display("79228162514264337593543950335") != "79228162514264337593543950335" ||
    !double.IsPositiveInfinity(PsdNumbers.Floating(new string('9', 1000000))) ||
    !double.IsNegativeInfinity(PsdNumbers.Floating("-" + new string('9', 310))) ||
    PsdNumbers.Floating(new string('0', 1000000) + "1") != 1 ||
    PsdNumbers.Floating("1" + new string('0', 308)) != (double)System.Numerics.BigInteger.Pow(10, 308))
    throw new InvalidOperationException("Bounded PSD1 numeric conversion mismatch");
foreach (var literal in new[] { new string('9', 309), new string('9', 310), "-" + new string('9', 309) })
{
    try { PsdReader.Parse("@{Value=" + literal + "}"); }
    catch (PsdParseException) { continue; }
    throw new InvalidOperationException("Non-importable overflowing integer was accepted");
}
var editorialData = PsdReader.Parse("@{Categories=@{qol=@{}};Mods=@{'modrinth:x'=@{Name='Override';Side='CLIENT';Category='QOL'}};ResourcePacks=@{}}");
var editorial = new EditorialMetadata(editorialData, (PsdTable)editorialData.Entries["Categories"],
    (PsdTable)editorialData.Entries["Mods"], (PsdTable)editorialData.Entries["ResourcePacks"], null);
var original = new InventoryItem("modrinth:x", ContentKind.Mod, "Original", "one.jar", "unknown",
    ContentSource.Packwiz, "v1", "metadata", true);
var shader = original with { Kind = ContentKind.ShaderPack };
ImmutableArray<InventoryItem> input = [original, original with { Filename = "two.jar" }, shader];
var classified = ModClassification.Apply(input, editorial);
if (original.Name != "Original" || original.Category != null || classified[0].Name != "Override" ||
    classified[0].Category != "QOL" || classified[0].Pinned != true || classified[0].VersionId != "v1" ||
    classified[1].Filename != "two.jar" || classified[1].Name != "Override" || classified[2] != shader ||
    !ModClassification.Apply(classified, editorial).SequenceEqual(classified))
    throw new InvalidOperationException("Classification immutability, field retention or idempotence failed");
var grouped = PsdReader.Parse("@{First=@{Side='client';Category='qol'};Second=@{Name=@();Side='server';Category='qol'}}");
var firstGroup = (PsdTable)grouped.Entries["First"];
firstGroup = firstGroup with { Entries = firstGroup.Entries.Add("Name", new PsdArray([new PsdArray([new PsdString("A"), new PsdString("B")])])) };
var programmaticMods = new PsdTable(editorial.Mods.Entries.SetItem("modrinth:x",
    new PsdArray([firstGroup, grouped.Entries["Second"]])));
var projected = ModClassification.Apply([original], editorial with { Mods = programmaticMods });
if (projected[0].Name != "A B" || projected[0].Side != "client server" || projected[0].InvalidCategory != "qol qol")
    throw new InvalidOperationException("Singleton projected field group did not collapse to its scalar value");
var resource = original with { Kind = ContentKind.ResourcePack, Filename = "A.zip", Name = "Technical" };
var localResource = InventoryItem.From(new LocalContentItem("local:z", ContentKind.ResourcePack, "Z", "z.zip", "client"));
ImmutableArray<InventoryItem> physicalResources = [resource, resource with { Filename = "a.zip", Name = "Other" }, localResource];
var activeMetadata = editorial with { ResourcePacks = PsdReader.Parse("@{'file/a.zip'=@{Name='Display'}}") };
var resourceView = ResourcePackActivation.Compose(physicalResources, ["FILE/a.zip", "file/missing.zip", "vanilla", "FILE/a.zip", "file/"], activeMetadata);
if (resourceView.Active[0].Name != "Display" || resourceView.Active[0].Source != ActiveResourceSource.Packwiz ||
    resourceView.Active[1].Source != ActiveResourceSource.Missing || resourceView.Active[2].Source != ActiveResourceSource.Builtin ||
    resourceView.Active[4].Filename != "" || resourceView.Active[4].Name != "file/" || resourceView.Active[4].Priority != 5 ||
    resourceView.Inactive.Length != 1 || resourceView.Inactive[0] != localResource || resource.Name != "Technical")
    throw new InvalidOperationException("Resource activation order, source, exclusion or immutability failed");
var rawSnapshot = new RawInventorySnapshot(new ModpackTools.Core.Projects.ProjectContext("fixture", "C:\\fixture", "Name", null,
    "fixture.mrpack", null, null, null, "index.toml", "C:\\fixture\\index.toml"), editorial,
    classified.Where(item => item.Kind == ContentKind.Mod).ToImmutableArray(), physicalResources, resourceView, [shader], []);
var selection = InventorySelector.Select(rawSnapshot, new(Category: "qol", Side: "client", Match: "Override modrinth:"));
if (selection.TotalMatches != 2 || !selection.IncludedTypes.SequenceEqual([ContentKind.Mod]) ||
    !selection.Filters.SequenceEqual(["type=mod", "category=qol", "side=client", "search=Override modrinth:"]) ||
    selection.Mods[0] != rawSnapshot.Mods[0] || rawSnapshot.Mods.Length != 2)
    throw new InvalidOperationException("Pure selection scope, cross-field match, labels or immutability failed");
foreach (var query in new[] { new RawInventoryQuery(Type: null), new RawInventoryQuery(State: null) })
{
    try { InventorySelector.Select(rawSnapshot, query); }
    catch (ModpackTools.Core.DomainException exception) when (exception.Target is ModpackTools.Core.TextTarget { Value: "" }) { continue; }
    throw new InvalidOperationException("Explicit null selection choice was silently defaulted");
}
var numberMetadata = editorial with { Categories = PsdReader.Parse("@{unclassified=@{Order=0};qol=@{Order=1}}") };
var identityA = original with { Name = "Same", Category = "unclassified", Filename = null };
var identityB = identityA with { };
var classifiedC = original with { Category = "qol" };
var unreferenced = original with { Category = null };
var activeIdentity = new ActiveResource("builtin", "Builtin", "", ActiveResourceSource.Builtin, 1);
var identitySelection = new InventorySelection(rawSnapshot.Project, numberMetadata,
    [identityA, identityB, identityA, classifiedC, unreferenced], [activeIdentity, activeIdentity], [localResource], [],
    [ContentKind.Mod, ContentKind.ResourcePack], []);
var numbered = InventoryReferences.Assign(identitySelection);
if (!numbered.Mods.Select(row => row.Number).SequenceEqual(new int?[] { 7, 6, 7, 4, null }) ||
    !numbered.ActiveResources.Select(row => row.Number).SequenceEqual([9, 9]) ||
    numbered.InactiveResources[0].Number != 10 || numbered.References.Length != 10 ||
    numbered.References[0].Filename != "" || numbered.References[0].Selector != identityA.Id ||
    numbered.References[7].Selector != activeIdentity.Id || numbered.References[9].Selector != localResource.Filename ||
    !ReferenceEquals(numbered.Mods[0].Item, identityA) || !ReferenceEquals(numbered.Mods[1].Item, identityB) ||
    !ReferenceEquals(numbered.Selection, identitySelection) || identityA != identityB || ReferenceEquals(identityA, identityB))
    throw new InvalidOperationException("Reference occurrence, object identity, null category or original-row retention failed");
UpdateTargetContracts.Verify(rawSnapshot);
BatchSelectorContracts.Verify();
ClassificationTargetContracts.Verify(rawSnapshot);
ResourceTargetContracts.Verify(rawSnapshot);
DefaultOptionsContracts.Verify();
CategorySelectorContracts.Verify();
CategoryListContracts.Verify(rawSnapshot);
CategoryCacheWriteContracts.Verify(rawSnapshot);
SourceTreeContracts.Verify();
VersionOrderingContracts.Verify();
VersionRangeContracts.Verify();
RootManifestContracts.Verify();
ForgeManifestContracts.Verify();
RootArchiveContracts.Verify();
ArtifactArchiveContracts.Verify();
RequirementContracts.Verify();
ConstraintEvidenceContracts.Verify();
GraphContracts.Verify();
GraphPolicyContracts.Verify();
ProviderCacheContracts.Verify();
ProviderAcquisitionContracts.VerifyAsync().GetAwaiter().GetResult();
ProviderCandidateContracts.VerifyAsync().GetAwaiter().GetResult();
CompatibleVersionContracts.VerifyAsync().GetAwaiter().GetResult();
SearchContracts.VerifyAsync().GetAwaiter().GetResult();
SolutionSearchContracts.VerifyAsync().GetAwaiter().GetResult();
ResolutionAcquisitionContracts.VerifyAsync().GetAwaiter().GetResult();
ProjectLookupContracts.VerifyAsync().GetAwaiter().GetResult();
ContentPlanContracts.VerifyAsync().GetAwaiter().GetResult();
ProjectTransactionContracts.VerifyAsync().GetAwaiter().GetResult();
ArtifactProviderContracts.VerifyAsync().GetAwaiter().GetResult();
ProjectHealthContracts.VerifyAsync().GetAwaiter().GetResult();
ImportedLookupContracts.Verify();
Console.WriteLine($"PSD1 native contracts passed: {invalid.Length + 7 + limits.Length} rejected inputs, typed literals, nested arrays, bounded numeric extraction, immutable classification/reference/alias identities, {args.Length} files");
return 0;
