using System.Collections.Generic;

namespace Mister.Version.Core.Models;

/// <summary>
/// Defines a group of projects that share a version number
/// </summary>
public class VersionGroup
{
    /// <summary>
    /// Name of the version group
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// List of project names that belong to this group
    /// Supports wildcard patterns (e.g., "Mister.Version.*")
    /// </summary>
    public List<string> Projects { get; set; } = new List<string>();

    /// <summary>
    /// Version policy strategy for this group (default: LockStep)
    /// </summary>
    public VersionPolicy Strategy { get; set; } = VersionPolicy.LockStep;

    /// <summary>
    /// Optional base version for this group
    /// If not specified, will use the highest version found in the group
    /// </summary>
    public string BaseVersion { get; set; }
}

/// <summary>
/// Configuration for version policies in a monorepo
/// </summary>
public class VersionPolicyConfig
{
    /// <summary>
    /// Default version policy for all projects (default: Independent)
    /// </summary>
    public VersionPolicy Policy { get; set; } = VersionPolicy.Independent;

    /// <summary>
    /// Version groups for grouped versioning strategy
    /// </summary>
    public Dictionary<string, VersionGroup> Groups { get; set; } = new Dictionary<string, VersionGroup>();
}
