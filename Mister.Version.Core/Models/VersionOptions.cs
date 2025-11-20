using System.Collections.Generic;

namespace Mister.Version.Core.Models;

/// <summary>
/// Version calculation options
/// </summary>
public class VersionOptions
{
    public string RepoRoot { get; set; }
    public string ProjectPath { get; set; }
    public string ProjectName { get; set; }
    public string TagPrefix { get; set; } = "v";
    public string ForceVersion { get; set; }
    public List<string> Dependencies { get; set; } = new List<string>();
    public bool Debug { get; set; } = false;
    public bool ExtraDebug { get; set; } = false;
    public bool SkipTestProjects { get; set; } = true;
    public bool SkipNonPackableProjects { get; set; } = true;
    public bool IsTestProject { get; set; } = false;
    public bool IsPackable { get; set; } = true;
    
    /// <summary>
    /// Prerelease type to use for main/dev branches when incrementing versions.
    /// Options: none, alpha, beta, rc
    /// Default: none (no prerelease suffix)
    /// </summary>
    public string PrereleaseType { get; set; } = "none";
    
    /// <summary>
    /// Base global version used as fallback when no tags or versions are found.
    /// Applies to all projects including test projects and artifacts normally ignored.
    /// </summary>
    public string BaseVersion { get; set; }
    
    /// <summary>
    /// Default increment level for version bumps.
    /// Options: patch, minor, major
    /// Default: patch
    /// </summary>
    public string DefaultIncrement { get; set; } = "patch";

    /// <summary>
    /// Conventional commits configuration for semantic version bump detection
    /// </summary>
    public ConventionalCommitConfig CommitConventions { get; set; }

    /// <summary>
    /// Change detection configuration for file pattern-based versioning
    /// </summary>
    public ChangeDetectionConfig ChangeDetection { get; set; }

    /// <summary>
    /// Git integration configuration for advanced repository scenarios
    /// </summary>
    public GitIntegrationConfig GitIntegration { get; set; }

    /// <summary>
    /// Additional directories to monitor for changes beyond the project directory.
    /// Changes in these directories will trigger version bumps according to file pattern rules.
    /// </summary>
    public List<string> AdditionalMonitorPaths { get; set; } = new List<string>();

    /// <summary>
    /// Version policy configuration for coordinating versions across projects
    /// </summary>
    public VersionPolicyConfig VersionPolicy { get; set; }

    /// <summary>
    /// Version scheme to use (SemVer or CalVer)
    /// Default: SemVer
    /// </summary>
    public VersionScheme Scheme { get; set; } = VersionScheme.SemVer;

    /// <summary>
    /// CalVer configuration (only used when Scheme is CalVer)
    /// </summary>
    public CalVerConfig CalVer { get; set; }

    /// <summary>
    /// Version validation constraints
    /// </summary>
    public VersionConstraints Constraints { get; set; }
}