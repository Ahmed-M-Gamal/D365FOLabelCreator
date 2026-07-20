namespace D365LabelCreator.ViewModels;

/// <summary>A metadata object type available in the type filter (e.g. AxTable -> "Table").</summary>
public sealed class TypeOption
{
    public required string ElementType { get; init; }

    public string Display => Friendly(ElementType);

    public override string ToString() => Display;

    /// <summary>Drops the leading "Ax" for a friendlier label, e.g. "AxEnumExtension" -> "EnumExtension".</summary>
    public static string Friendly(string elementType) =>
        elementType.StartsWith("Ax", System.StringComparison.Ordinal) ? elementType[2..] : elementType;
}

/// <summary>A single metadata object available in the item filter.</summary>
public sealed class ItemOption
{
    public required string ElementType { get; init; }
    public required string Name { get; init; }

    public string Display => $"{TypeOption.Friendly(ElementType)} / {Name}";

    public override string ToString() => Display;
}
