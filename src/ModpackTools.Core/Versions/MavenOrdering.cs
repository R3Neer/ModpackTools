using System.Globalization;
using System.Numerics;

namespace ModpackTools.Core.Versions;

// Adapted from the project's Private/MavenVersion.cs. Preserve its item ordering
// and normalization; bound construction before using recursive comparisons.
internal static class MavenOrdering
{
    private abstract class Item
    {
        public abstract int Compare(Item? other);
        public abstract bool Empty { get; }
    }
    private sealed class Number(BigInteger value) : Item
    {
        public override bool Empty => value.IsZero;
        public override int Compare(Item? other) => other is null ? value.Sign : other is Number number ? value.CompareTo(number.Value) : 1;
        private BigInteger Value => value;
    }
    private sealed class Qualifier : Item
    {
        private static readonly string[] Known = ["alpha", "beta", "milestone", "rc", "snapshot", "", "sp"];
        private readonly string value;
        private readonly string key;
        public Qualifier(string text, bool followedByDigit)
        {
            if (followedByDigit && text.Length == 1)
                text = text switch { "a" => "alpha", "b" => "beta", "m" => "milestone", _ => text };
            text = text switch { "ga" or "final" or "release" => "", "cr" => "rc", _ => text };
            value = text;
            var position = Array.IndexOf(Known, text);
            key = position < 0 ? "7-" + text : position.ToString(CultureInfo.CurrentCulture);
        }
        public override bool Empty => value.Length == 0;
        public override int Compare(Item? other) => other is null ? string.CompareOrdinal(key, "5")
            : other is Number or Items ? -1 : string.CompareOrdinal(key, ((Qualifier)other).key);
    }
    private sealed class Items : Item
    {
        public List<Item> Values { get; } = [];
        public override bool Empty => Values.Count == 0;
        public void Normalize()
        {
            for (var index = Values.Count - 1; index >= 0; index--)
            {
                if (Values[index].Empty) Values.RemoveAt(index);
                else if (Values[index] is not Items) break;
            }
        }
        public override int Compare(Item? other)
        {
            if (other is Number) return -1;
            if (other is Qualifier) return 1;
            var right = other as Items;
            var count = Math.Max(Values.Count, right?.Values.Count ?? 0);
            for (var index = 0; index < count; index++)
            {
                var leftItem = index < Values.Count ? Values[index] : null;
                var rightItem = right is not null && index < right.Values.Count ? right.Values[index] : null;
                var result = leftItem is null ? rightItem is null ? 0 : -rightItem.Compare(null) : leftItem.Compare(rightItem);
                if (result != 0) return result;
            }
            return 0;
        }
    }

    public static int Compare(string left, string right, VersionScope leftScope, VersionScope rightScope)
    {
        var a = Parse(left, leftScope);
        var b = Parse(right, rightScope);
        return a.Compare(b);
    }
    private static Items Parse(string input, VersionScope scope)
    {
        var text = input.ToLowerInvariant();
        Items List(int depth) { scope.Depth(depth); scope.Item(); return new(); }
        Item Part(bool digit, int start, int count, bool followedByDigit = false)
        {
            scope.Item();
            return digit ? new Number(scope.Number(text.AsSpan(start, count), start))
                : new Qualifier(text.Substring(start, count), followedByDigit);
        }
        var root = List(1);
        var list = root;
        var stack = new Stack<Items>();
        stack.Push(root);
        var start = 0;
        var digit = false;
        void Child()
        {
            var child = List(stack.Count + 1);
            list.Values.Add(child);
            list = child;
            stack.Push(child);
        }
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character is '.' or '-')
            {
                if (index == start) { scope.Item(); list.Values.Add(new Number(scope.Number("0", index))); }
                else list.Values.Add(Part(digit, start, index - start));
                start = index + 1;
                if (character == '-')
                {
                    if (digit)
                    {
                        list.Normalize();
                        if (index + 1 < text.Length && char.IsDigit(text[index + 1])) Child();
                    }
                    else Child();
                }
            }
            else if (char.IsDigit(character))
            {
                if (!digit && index > start)
                {
                    list.Values.Add(Part(false, start, index - start, true));
                    start = index;
                    Child();
                }
                digit = true;
            }
            else
            {
                if (digit && index > start)
                {
                    list.Values.Add(Part(true, start, index - start));
                    start = index;
                    Child();
                }
                digit = false;
            }
        }
        if (text.Length > start) list.Values.Add(Part(digit, start, text.Length - start));
        while (stack.TryPop(out var current)) current.Normalize();
        return root;
    }
}
