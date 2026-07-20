namespace D365LabelCreator.Models;

/// <summary>Persisted user settings, stored per-user under %AppData%\D365LabelCreator\config.json.</summary>
public sealed class AppConfig
{
    public string? PackagesLocalDirectory { get; set; }
    public string? ProjectsDirectory { get; set; }

    /// <summary>Prefix forced onto defaulted label ids; kept between validations and sessions.</summary>
    public string? IdPrefix { get; set; }
}
