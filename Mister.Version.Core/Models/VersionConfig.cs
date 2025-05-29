using System.Collections.Generic;

namespace Mister.Version.Core.Models;

/// <summary>
/// Configuration for version calculation that can be loaded from YAML
/// </summary>
public class VersionConfig
{
    /// <summary>
    /// Base global version used as fallback when no tags or versions are found.
    /// Applies to all projects including test projects and artifacts normally ignored.
    /// </summary>
    public string BaseVersion { get; set; }
    
    public string DefaultIncrement { get; set; }

    /// <summary>
    /// Prerelease type to use for main/dev branches when incrementing versions
    /// Options: none, alpha, beta, rc
    /// </summary>
    public string PrereleaseType { get; set; }
    
    /// <summary>
    /// Custom tag prefix for version tags (default: v)
    /// </summary>
    public string TagPrefix { get; set; }
    
    /// <summary>
    /// Whether to skip versioning for test projects
    /// </summary>
    public bool? SkipTestProjects { get; set; }
    
    /// <summary>
    /// Whether to skip versioning for non-packable projects
    /// </summary>
    public bool? SkipNonPackableProjects { get; set; }
    
    /// <summary>
    /// Project-specific configuration
    /// </summary>
    public Dictionary<string, ProjectVersionConfig> Projects { get; set; }
}

/// <summary>
/// Project-specific version configuration
/// </summary>
public class ProjectVersionConfig
{
    /// <summary>
    /// Override prerelease type for this specific project
    /// </summary>
    public string PrereleaseType { get; set; }
    
    /// <summary>
    /// Force a specific version for this project
    /// </summary>
    public string ForceVersion { get; set; }
}