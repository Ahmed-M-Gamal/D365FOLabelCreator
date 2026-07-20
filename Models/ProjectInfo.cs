using System;
using System.Collections.Generic;

namespace D365LabelCreator.Models;

/// <summary>A D365 VS project (.rnrproj): the model it targets and the metadata items it contains.</summary>
public sealed class ProjectInfo
{
    public required string Name { get; init; }
    public required string ProjectPath { get; init; }

    /// <summary>The &lt;Model&gt; the project references, e.g. "SOG_OPR".</summary>
    public required string Model { get; init; }

    /// <summary>Lower-cased "type|name" keys of the metadata items in the project.</summary>
    public HashSet<string> ItemKeys { get; } = new();

    public static string Key(string elementType, string name) =>
        (elementType + "|" + name).ToLowerInvariant();

    public bool Contains(string elementType, string name) => ItemKeys.Contains(Key(elementType, name));

    /// <summary>Shows the referenced model alongside the project name in the filter dropdown.</summary>
    public override string ToString() =>
        string.IsNullOrWhiteSpace(Model) ? Name : $"{Name}  ({Model})";
}

/// <summary>A VS solution (.sln) and the .rnrproj projects it references.</summary>
public sealed class SolutionInfo
{
    public required string Name { get; init; }
    public required string SolutionPath { get; init; }
    public List<ProjectInfo> Projects { get; } = new();

    /// <summary>Union of all item keys across the solution's projects.</summary>
    public HashSet<string> AllItemKeys
    {
        get
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in Projects)
                set.UnionWith(p.ItemKeys);
            return set;
        }
    }

    public override string ToString() => Name;
}
