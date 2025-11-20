using System.Collections.Generic;

namespace Mister.Version.Core.Models
{
    /// <summary>
    /// Configuration for advanced Git repository integration scenarios
    /// </summary>
    public class GitIntegrationConfig
    {
        /// <summary>
        /// Enable shallow clone support (repos cloned with --depth)
        /// When enabled, versioning will work with limited history
        /// Default: true
        /// </summary>
        public bool ShallowCloneSupport { get; set; } = true;

        /// <summary>
        /// Fallback version to use when shallow clone prevents full history access
        /// Default: null (uses BaseVersion if available)
        /// </summary>
        public string ShallowCloneFallbackVersion { get; set; }

        /// <summary>
        /// Enable submodule support for version calculation
        /// When enabled, changes in submodules will trigger version bumps
        /// Default: false
        /// </summary>
        public bool SubmoduleSupport { get; set; } = false;

        /// <summary>
        /// Custom tag patterns for different project types
        /// Format: "ProjectName={pattern}" where {pattern} can use {name}, {version}, {prefix}
        /// Example: "MyLib={name}/{prefix}{version}", "MyApp={name}-{prefix}{version}"
        /// Default: uses standard patterns (ProjectName-v1.0.0, ProjectName/v1.0.0)
        /// </summary>
        public List<string> CustomTagPatterns { get; set; } = new List<string>();

        /// <summary>
        /// Enable branch-based versioning strategies
        /// When enabled, different branches can have different versioning behaviors
        /// Default: false
        /// </summary>
        public bool BranchBasedVersioning { get; set; } = false;

        /// <summary>
        /// Branch versioning rules in format "branchPattern=strategy"
        /// Patterns: main, dev, release/*, feature/*, hotfix/*
        /// Strategies: stable, prerelease, feature
        /// Example: "release/*=stable", "feature/*=feature"
        /// Default: empty (uses default branch type detection)
        /// </summary>
        public List<string> BranchVersioningRules { get; set; } = new List<string>();

        /// <summary>
        /// Include branch name in version metadata for feature branches
        /// When enabled, adds branch name to build metadata (e.g., 1.0.0+feature-auth)
        /// Default: false
        /// </summary>
        public bool IncludeBranchInMetadata { get; set; } = false;

        /// <summary>
        /// Enable tag ancestry validation
        /// When enabled, ensures version tags are reachable from current branch
        /// Useful for ensuring tags are on the correct branch
        /// Default: true
        /// </summary>
        public bool ValidateTagAncestry { get; set; } = true;

        /// <summary>
        /// Fetch depth for shallow clone operations
        /// When working with shallow clones, this determines how much history to fetch
        /// Default: 50
        /// </summary>
        public int FetchDepth { get; set; } = 50;
    }
}
