using System.Collections.Immutable;
using System.Globalization;
using ModpackTools.Core;
using ModpackTools.Core.Data;
using ModpackTools.Core.Inventory;

internal static class CategoryListContracts
{
    public static void Verify(RawInventorySnapshot snapshot)
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var categories = (PsdTable)PsdReader.Parse("@{Categories=@{z=@{Name='Same';Order=1};'ä'=@{Name='Same';Order=1};unclassified=@{Name='Custom'}}}").Entries["Categories"];
            var mods = PsdReader.Parse("@{orphan=@{Category='Z'};other=@{Category='unclassified'}}");
            var metadata = snapshot.Metadata with { Categories = categories, Mods = mods };
            var prepared = CategoryList.Prepare(metadata);
            if (prepared[0].Id != "ä" || prepared[1].Id != "z" || prepared[1].ModCount != 1)
                throw new InvalidOperationException("Category list lost cultural Id ordering or orphan editorial counts.");
            var physical = snapshot.Mods[0] with { Category = "UNCLASSIFIED" };
            var view = CategoryList.Compose(snapshot.Project, prepared, [physical, physical]);
            if (!ReferenceEquals(view.Project, snapshot.Project) || view.DefinedCategoryCount != 3 || view.Categories.Length != 4
                || view.Categories[2].Id != "unclassified" || view.Categories[2].ModCount != 1 || view.Categories[2].IsUnclassified
                || view.Categories[3].ModCount != 2 || view.Categories[3].Order is not null || !view.Categories[3].IsUnclassified
                || !view.Categories.Select(row => row.ReferenceNumber).SequenceEqual(new[] { 1, 2, 3, 4 }))
                throw new InvalidOperationException("Category view conflated editorial/physical counts, identities or appended rows.");
            var bad = metadata with { Mods = PsdReader.Parse("@{bad=$false}") };
            Expect(() => CategoryList.Prepare(bad), "Metadata.InvalidCategoryAssignment");
            bad = bad with { Categories = PsdReader.Parse("@{q=$false}") };
            Expect(() => CategoryList.Prepare(bad), "Metadata.InvalidCategoryAssignment");
            var empty = bad with { Categories = PsdReader.Parse("@{}") };
            if (!CategoryList.Prepare(empty).IsEmpty) throw new InvalidOperationException("Empty definitions inspected unused assignments.");
            Expect(() => CategoryList.Prepare(metadata with { Categories = PsdReader.Parse("@{q=$false}") }), "Metadata.InvalidCategoryDefinition");
            try { CategoryList.Compose(snapshot.Project, default, ImmutableArray<InventoryItem>.Empty); }
            catch (ArgumentException) { return; }
            throw new InvalidOperationException("Default category arrays were accepted.");
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    private static void Expect(Func<ImmutableArray<EditorialCategory>> action, string id)
    {
        try { action(); }
        catch (DomainException exception) when (exception.Id == "ModpackTools." + id) { return; }
        throw new InvalidOperationException("Expected category view error " + id);
    }
}
