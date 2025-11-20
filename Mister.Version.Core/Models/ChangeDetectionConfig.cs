using System.Collections.Generic;

namespace Mister.Version.Core.Models
{
    /// <summary>
    /// Configuration for change detection and file pattern matching
    /// </summary>
    public class ChangeDetectionConfig
    {
        /// <summary>
        /// Whether change detection is enabled
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// File patterns to ignore (won't trigger version bumps)
        /// Uses glob patterns (e.g., "**/*.md", "**/docs/**")
        /// </summary>
        public List<string> IgnorePatterns { get; set; } = new List<string>();

        /// <summary>
        /// File patterns that require major version bumps
        /// </summary>
        public List<string> MajorPatterns { get; set; } = new List<string>();

        /// <summary>
        /// File patterns that require minor version bumps
        /// </summary>
        public List<string> MinorPatterns { get; set; } = new List<string>();

        /// <summary>
        /// File patterns that require patch version bumps
        /// </summary>
        public List<string> PatchPatterns { get; set; } = new List<string>();

        /// <summary>
        /// Only version when source code changes (ignore test/doc changes)
        /// When true, only files NOT matching ignore patterns will trigger versions
        /// </summary>
        public bool SourceOnlyMode { get; set; } = false;

        /// <summary>
        /// Minimum bump type required for ANY change
        /// Options: None, Patch, Minor, Major
        /// Default: None (uses file-based detection)
        /// </summary>
        public VersionBumpType MinimumBumpType { get; set; } = VersionBumpType.None;

        /// <summary>
        /// Additional directories to monitor for changes beyond the project directory.
        /// Changes in these directories will trigger version bumps according to file pattern rules.
        /// Paths can be absolute or relative to the repository root.
        /// </summary>
        public List<string> AdditionalMonitorPaths { get; set; } = new List<string>();
    }

    /// <summary>
    /// Result of change classification based on file patterns
    /// </summary>
    public class ChangeClassification
    {
        /// <summary>
        /// Whether this change should be ignored
        /// </summary>
        public bool ShouldIgnore { get; set; }

        /// <summary>
        /// Required version bump type based on file patterns
        /// </summary>
        public VersionBumpType RequiredBumpType { get; set; }

        /// <summary>
        /// Files that matched major patterns
        /// </summary>
        public List<string> MajorFiles { get; set; } = new List<string>();

        /// <summary>
        /// Files that matched minor patterns
        /// </summary>
        public List<string> MinorFiles { get; set; } = new List<string>();

        /// <summary>
        /// Files that matched patch patterns
        /// </summary>
        public List<string> PatchFiles { get; set; } = new List<string>();

        /// <summary>
        /// Files that were ignored
        /// </summary>
        public List<string> IgnoredFiles { get; set; } = new List<string>();

        /// <summary>
        /// Files that didn't match any pattern (use default behavior)
        /// </summary>
        public List<string> UnclassifiedFiles { get; set; } = new List<string>();

        /// <summary>
        /// Total number of changed files analyzed
        /// </summary>
        public int TotalFiles { get; set; }

        /// <summary>
        /// Reason for the classification
        /// </summary>
        public string Reason { get; set; }

        /// <summary>
        /// Whether there are any non-ignored changes
        /// </summary>
        public bool HasChanges => !ShouldIgnore && (RequiredBumpType != VersionBumpType.None || MajorFiles.Count > 0 || MinorFiles.Count > 0 || PatchFiles.Count > 0 || UnclassifiedFiles.Count > 0);
    }
}
