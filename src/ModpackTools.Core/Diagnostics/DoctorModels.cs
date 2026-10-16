using System.Collections.Immutable;
using ModpackTools.Core.Build;
using ModpackTools.Core.Graph;

namespace ModpackTools.Core.Diagnostics;

public sealed record DoctorItem(string Status, string Text);
public sealed record DoctorCheck(string Section, string Status, string Label, string Value, string Detail, ImmutableArray<DoctorItem> Items);
public sealed record DoctorReport(ImmutableArray<DoctorCheck> Checks)
{
    public int Failures => Checks.Count(check => check.Status == "fail");
    public int Warnings => Checks.Count(check => check.Status == "warn");
}
public sealed record DoctorLimits(ProjectHealthLimits? Health = null, ArtifactFreshnessLimits? Freshness = null, int MaximumProjects = 10000,
    int MaximumDisplayItems = 100000, int MaximumDisplayCharacters = 64 * 1024 * 1024, int MaximumDirectories = 100000);
