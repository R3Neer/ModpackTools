using System.Collections.Immutable;
using System.Text;
using ModpackTools.Core.Data;
using ModpackTools.Core.Graph;
using ModpackTools.Core.Inventory;
using ModpackTools.Core.Processes;
using ModpackTools.Core.Projects;
using ModpackTools.Core.Transactions;

namespace ModpackTools.Core.Resolution;

public sealed record ModSideRequest(ImmutableArray<string> Selectors, string Side, bool DryRun = false);
public sealed record ModSideLimits(ProjectHealthLimits? Health = null, TransactionLimits? Transaction = null, NativeToolLimits? Tool = null);

public static class ModSideOperation
{
    public static async Task<TransactionResult<string>> ExecuteAsync(ProjectContext project, ModSideRequest request,
        string configurationDirectory, string stateDirectory, DateTimeOffset now, Func<PackwizObservation> resolvePackwiz, string nativeHost,
        ModSideLimits? limits = null, TransactionExpectation? expected = null, CancellationToken cancellation = default,
        Func<TransactionPreview<string>, CancellationToken, ValueTask>? prepareOutput = null)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(resolvePackwiz); limits ??= new();
        if (request.Selectors.IsDefaultOrEmpty || request.Selectors.Length > 1024 || request.Selectors.Any(value => string.IsNullOrEmpty(value) || value.Length > 4096)
            || string.IsNullOrEmpty(request.Side) || request.Side.Length > 4096)
            throw new DomainException("Content.InvalidSideRequest", "Side changes require bounded nonempty mod selectors and side", "select installed mods and client, host or both");
        var config = MutationConfiguration.Resolve(project, configurationDirectory);
        var selectors = BatchSelectorResolver.ResolveFromDirectory(request.Selectors, config, project.Id, now);
        return await ProjectTransaction.ExecuteAsync(project, stateDirectory, Prepare, dryRun: request.DryRun, expected: expected,
            limits: limits.Transaction, cancellation: cancellation, prepareOutput: prepareOutput).ConfigureAwait(false);

        async Task<string> Prepare(ProjectContext stage, CancellationToken token)
        {
            var before = await ProjectHealth.ReadAsync(stage, config, now, limits: limits.Health, cancellation: token).ConfigureAwait(false);
            var targets = ModClassificationSelector.Resolve(before.Inventory, selectors).OrderBy(item => item.Id, StringComparer.CurrentCultureIgnoreCase)
                .DistinctBy(item => item.Id, StringComparer.CurrentCultureIgnoreCase).ToImmutableArray();
            var side = request.Side.ToLowerInvariant(); if (side == "host") side = "server";
            if (side is not ("client" or "server" or "both"))
                throw new DomainException("Content.InvalidSide", $"Side '{request.Side}' is not recognized; allowed values: client, host, both", "modpack mod set-side <client|host|both> <mod...>", target: request.Side);
            var metadata = before.Inventory.Metadata; var mods = metadata.Mods.Entries; var local = false;
            foreach (var target in targets)
            {
                token.ThrowIfCancellationRequested();
                if (target.Source == ContentSource.Packwiz && target.MetadataPath is { } path)
                {
                    try
                    {
                        var updated = TomlStringEdits.Apply(TextFile.Read(path), [new("", "side", side)], 4 * 1024 * 1024);
                        if (TomlDeclarationReader.Read(updated, new()).Get("side") is not TomlText field || field.Value != side || new TomlDocument(updated).String("side") != side)
                            throw new FormatException("The side edit cannot be represented by the public inventory reader.");
                        File.WriteAllText(path, updated, new UTF8Encoding(false, true));
                    }
                    catch (FormatException error)
                    {
                        throw new DomainException("Content.UnsupportedSideEdit", "Managed side could not be edited without changing its data meaning", "repair the root side field in the managed TOML", "InvalidData",
                            TransactionFiles.Within(project.Root, Path.GetRelativePath(stage.Root, path)), details: error.Message);
                    }
                }
                else
                {
                    var entries = mods.TryGetValue(target.Id, out var prior)
                        ? (prior as PsdTable ?? throw new DomainException("Metadata.InvalidOverride", "Requested mod override must be a table", "repair the mod metadata", "InvalidData", target.Id)).Entries
                        : ImmutableDictionary<string, PsdValue>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
                    mods = mods.SetItem(target.Id, new PsdTable(entries.SetItem("Side", new PsdString(side)))); local = true;
                }
            }
            if (local)
            {
                var path = TransactionFiles.Within(stage.Root, ".modpack/metadata.psd1"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, PsdDataWriter.Write(new(metadata.Data.Entries.SetItem("Mods", new PsdTable(mods))), 4 * 1024 * 1024), new UTF8Encoding(false, true));
            }
            var after = await ProjectHealth.ReadAsync(stage, config, now, limits: limits.Health, cancellation: token).ConfigureAwait(false);
            var observed = after.Inventory.Mods.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var target in targets)
                if (!observed.TryGetValue(target.Id, out var value) || value.Side != side)
                    throw new DomainException("Content.SideNotRetained", "Requested side is masked by another override or cannot be read back", "inspect the matching Mods side override and managed TOML", "InvalidResult", target.Id);
            GraphPolicy.Assert(after.RawReport, before.RawReport);
            if (targets.Any(target => target.Source == ContentSource.Packwiz))
                _ = await PackwizRefresh.RefreshAsync(stage, resolvePackwiz(), nativeHost, limits.Tool, token).ConfigureAwait(false);
            return side;
        }
    }
}
