using System.Collections.Immutable;

namespace ModpackTools.Core.Data;

public abstract record PsdValue;
public sealed record PsdString(string Value) : PsdValue;
public sealed record PsdNumber(string Literal) : PsdValue;
public sealed record PsdBoolean(bool Value) : PsdValue;
public sealed record PsdNull : PsdValue;
public sealed record PsdArray(ImmutableArray<PsdValue> Items) : PsdValue;
public sealed record PsdTable(ImmutableDictionary<string, PsdValue> Entries) : PsdValue;

public sealed class PsdParseException(string message, int offset) : FormatException($"{message} (offset {offset}).")
{
    public int Offset { get; } = offset;
}
