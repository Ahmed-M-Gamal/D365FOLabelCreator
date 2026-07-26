using System.Collections.Generic;

namespace D365LabelCreator.Models;

/// <summary>Persisted user settings, stored per-user under %AppData%\D365LabelCreator\config.json.</summary>
public sealed class AppConfig
{
    public string? PackagesLocalDirectory { get; set; }
    public string? ProjectsDirectory { get; set; }

    /// <summary>Prefix forced onto defaulted label ids; kept between validations and sessions.</summary>
    public string? IdPrefix { get; set; }

    /// <summary>
    /// Labels skipped by the user. Key = model name, Value = list of normalised label keys
    /// (LabelGroup.NormalizeKey(text)). Used to hide skipped labels on subsequent loads.
    /// </summary>
    public Dictionary<string, List<string>>? SkippedLabelsByModel { get; set; }
}
