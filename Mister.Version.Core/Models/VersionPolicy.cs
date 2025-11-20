namespace Mister.Version.Core.Models;

/// <summary>
/// Version policy strategy for coordinating versions across projects in a monorepo
/// </summary>
public enum VersionPolicy
{
    /// <summary>
    /// Each project versions independently based on its own changes (default behavior)
    /// </summary>
    Independent,

    /// <summary>
    /// All projects share a single version number and bump together
    /// </summary>
    LockStep,

    /// <summary>
    /// Projects are organized into groups, each group shares a version
    /// </summary>
    Grouped
}
