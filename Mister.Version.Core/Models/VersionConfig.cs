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

    /// <summary>
    /// Conventional commits configuration for semantic version bump detection
    /// </summary>
    public ConventionalCommitConfig CommitConventions { get; set; }
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

/// <summary>
/// Configuration for conventional commit parsing and semantic version bump detection
/// </summary>
public class ConventionalCommitConfig
{
    /// <summary>
    /// Enable conventional commit analysis for automatic version bump detection
    /// Default: false (uses legacy patch-only behavior)
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Commit message patterns that trigger major version bumps
    /// Default: ["BREAKING CHANGE:", "!:"]
    /// </summary>
    public List<string> MajorPatterns { get; set; } = new List<string> { "BREAKING CHANGE:", "!:" };

    /// <summary>
    /// Commit message patterns that trigger minor version bumps
    /// Default: ["feat:", "feature:"]
    /// </summary>
    public List<string> MinorPatterns { get; set; } = new List<string> { "feat:", "feature:" };

    /// <summary>
    /// Commit message patterns that trigger patch version bumps
    /// Default: ["fix:", "bugfix:", "perf:", "refactor:"]
    /// </summary>
    public List<string> PatchPatterns { get; set; } = new List<string> { "fix:", "bugfix:", "perf:", "refactor:" };

    /// <summary>
    /// Commit message patterns that should be ignored for versioning
    /// Default: ["chore:", "docs:", "style:", "test:", "ci:"]
    /// </summary>
    public List<string> IgnorePatterns { get; set; } = new List<string> { "chore:", "docs:", "style:", "test:", "ci:" };
}