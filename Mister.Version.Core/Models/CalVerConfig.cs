using System;

namespace Mister.Version.Core.Models;

/// <summary>
/// Configuration for Calendar Versioning (CalVer)
/// </summary>
public class CalVerConfig
{
    /// <summary>
    /// Format pattern for CalVer versioning
    /// Supported formats:
    /// - YYYY.MM.PATCH (e.g., 2025.11.0) - Ubuntu style
    /// - YY.0M.PATCH (e.g., 25.11.0) - Short year with zero-padded month
    /// - YYYY.WW.PATCH (e.g., 2025.47.0) - Week-based versioning
    /// - YYYY.0M.PATCH (e.g., 2025.11.0) - Full year with zero-padded month
    /// </summary>
    public string Format { get; set; } = "YYYY.MM.PATCH";

    /// <summary>
    /// Optional start date for CalVer calculation. If not specified, uses current date.
    /// Format: YYYY-MM-DD (e.g., "2025-01-01")
    /// </summary>
    public string StartDate { get; set; }

    /// <summary>
    /// Whether to reset patch version at the start of each month/week
    /// Default: true
    /// </summary>
    public bool ResetPatchPeriodically { get; set; } = true;

    /// <summary>
    /// Custom separator between version components (default: ".")
    /// </summary>
    public string Separator { get; set; } = ".";

    /// <summary>
    /// Gets the start date as a DateTime, or DateTime.MinValue if not set
    /// </summary>
    public DateTime GetStartDate()
    {
        if (string.IsNullOrEmpty(StartDate))
            return DateTime.MinValue;

        if (DateTime.TryParse(StartDate, out var date))
            return date;

        return DateTime.MinValue;
    }

    /// <summary>
    /// Validates the CalVer format string
    /// </summary>
    public bool IsValidFormat()
    {
        var validFormats = new[] { "YYYY.MM.PATCH", "YY.0M.PATCH", "YYYY.WW.PATCH", "YYYY.0M.PATCH" };
        return Array.Exists(validFormats, f => f.Equals(Format, StringComparison.OrdinalIgnoreCase));
    }
}
