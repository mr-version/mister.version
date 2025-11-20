namespace Mister.Version.Core.Models;

/// <summary>
/// Version scheme to use for versioning
/// </summary>
public enum VersionScheme
{
    /// <summary>
    /// Semantic Versioning (default) - MAJOR.MINOR.PATCH format
    /// </summary>
    SemVer,

    /// <summary>
    /// Calendar Versioning - Date-based versioning (e.g., 2025.11.0)
    /// </summary>
    CalVer
}
