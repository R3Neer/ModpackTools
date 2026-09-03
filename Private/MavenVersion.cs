using System;
using System.Collections.Generic;
using System.Numerics;

namespace ModpackTools {
    // Maven-style numeric, qualifier and nested-list ordering. No CLR version parsing.
    public static class MavenVersion {
        abstract class Item { public abstract int Compare(Item other); public abstract bool Empty { get; } }
        sealed class Number : Item {
            internal readonly BigInteger Value;
            internal Number(string value) { Value = BigInteger.Parse(value); }
            public override bool Empty { get { return Value.IsZero; } }
            public override int Compare(Item other) { return other == null ? Value.Sign : other is Number ? Value.CompareTo(((Number)other).Value) : 1; }
        }
        sealed class Qualifier : Item {
            readonly string value;
            static readonly string[] known = { "alpha", "beta", "milestone", "rc", "snapshot", "", "sp" };
            internal Qualifier(string text, bool followedByDigit) {
                if (followedByDigit && text.Length == 1) {
                    if (text == "a") text = "alpha"; else if (text == "b") text = "beta"; else if (text == "m") text = "milestone";
                }
                if (text == "ga" || text == "final" || text == "release") text = "";
                if (text == "cr") text = "rc";
                value = text;
            }
            string Key { get { int n = Array.IndexOf(known, value); return n < 0 ? "7-" + value : n.ToString(); } }
            public override bool Empty { get { return value.Length == 0; } }
            public override int Compare(Item other) {
                if (other == null) return String.CompareOrdinal(Key, "5");
                if (other is Number || other is Items) return -1;
                return String.CompareOrdinal(Key, ((Qualifier)other).Key);
            }
        }
        sealed class Items : Item {
            internal readonly List<Item> Values = new List<Item>();
            public override bool Empty { get { return Values.Count == 0; } }
            internal void Normalize() {
                for (int i = Values.Count - 1; i >= 0; --i) {
                    if (Values[i].Empty) Values.RemoveAt(i);
                    else if (!(Values[i] is Items)) break;
                }
            }
            public override int Compare(Item other) {
                if (other is Number) return -1;
                if (other is Qualifier) return 1;
                var right = other as Items;
                int size = Math.Max(Values.Count, right == null ? 0 : right.Values.Count);
                for (int i = 0; i < size; i++) {
                    Item a = i < Values.Count ? Values[i] : null;
                    Item b = right != null && i < right.Values.Count ? right.Values[i] : null;
                    int c = a == null ? (b == null ? 0 : -b.Compare(null)) : a.Compare(b);
                    if (c != 0) return c;
                }
                return 0;
            }
        }
        static Item ParsePart(bool digit, string text) { return digit ? (Item)new Number(text) : new Qualifier(text, false); }
        static Items Parse(string input) {
            string text = input.ToLowerInvariant(); var root = new Items(); var list = root;
            var stack = new Stack<Items>(); stack.Push(root); int start = 0; bool digit = false;
            for (int i = 0; i < text.Length; i++) {
                char ch = text[i];
                if (ch == '.' || ch == '-') {
                    list.Values.Add(i == start ? new Number("0") : ParsePart(digit, text.Substring(start, i - start)));
                    start = i + 1;
                    if (ch == '-') {
                        if (digit) {
                            list.Normalize();
                            if (i + 1 < text.Length && Char.IsDigit(text[i + 1])) { var child = new Items(); list.Values.Add(child); list = child; stack.Push(list); }
                        } else { var child = new Items(); list.Values.Add(child); list = child; stack.Push(list); }
                    }
                } else if (Char.IsDigit(ch)) {
                    if (!digit && i > start) {
                        list.Values.Add(new Qualifier(text.Substring(start, i - start), true)); start = i;
                        var child = new Items(); list.Values.Add(child); list = child; stack.Push(list);
                    }
                    digit = true;
                } else {
                    if (digit && i > start) {
                        list.Values.Add(new Number(text.Substring(start, i - start))); start = i;
                        var child = new Items(); list.Values.Add(child); list = child; stack.Push(list);
                    }
                    digit = false;
                }
            }
            if (text.Length > start) list.Values.Add(ParsePart(digit, text.Substring(start)));
            while (stack.Count > 0) stack.Pop().Normalize();
            return root;
        }
        public static int Compare(string left, string right) { return Parse(left).Compare(Parse(right)); }
    }
}
