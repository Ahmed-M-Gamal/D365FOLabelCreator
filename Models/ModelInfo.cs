namespace D365LabelCreator.Models;

/// <summary>
/// A D365 F&O model that is customizable (Descriptor AxModelInfo/Customization = Allow).
/// </summary>
public sealed class ModelInfo
{
    /// <summary>Model name, e.g. "SOG_OPR". Equals the model folder name under the package.</summary>
    public required string Name { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>The package folder, e.g. ...\PackagesLocalDirectory\SOG_OPR.</summary>
    public required string PackageDir { get; init; }

    /// <summary>The model folder, e.g. ...\PackagesLocalDirectory\SOG_OPR\SOG_OPR.</summary>
    public required string ModelDir { get; init; }

    /// <summary>Full path of the descriptor xml under the package's Descriptor folder.</summary>
    public required string DescriptorPath { get; init; }

    public override string ToString() => Name;
}
