namespace Mister.Version.Core.Models;

/// <summary>
/// Type of semantic version bump based on commit analysis
/// </summary>
public enum VersionBumpType
{
    /// <summary>
    /// No version bump needed
    /// </summary>
    None,

    /// <summary>
    /// Patch version bump (0.0.X) - for bug fixes and minor changes
    /// </summary>
    Patch,

    /// <summary>
    /// Minor version bump (0.X.0) - for new features
    /// </summary>
    Minor,

    /// <summary>
    /// Major version bump (X.0.0) - for breaking changes
    /// </summary>
    Major
}
