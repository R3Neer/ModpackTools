namespace ModpackTools.Core;

public abstract record DiagnosticTarget;
public sealed record TextTarget(string Value) : DiagnosticTarget;
public sealed record NumberTarget(long Value) : DiagnosticTarget;
public sealed record DecimalTarget(decimal Value) : DiagnosticTarget;
public sealed record FloatingTarget(double Value) : DiagnosticTarget;
public sealed record BooleanTarget(bool Value) : DiagnosticTarget;
public sealed record TextListTarget(string[] Values) : DiagnosticTarget;
