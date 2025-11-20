using System;
using System.Collections.Generic;

namespace Mister.Version.Core.Models;

/// <summary>
/// Version calculation result
/// </summary>
public class VersionResult
{
    public string Version { get; set; }
    public SemVer SemVer { get; set; }
    public bool VersionChanged { get; set; }
    public string ChangeReason { get; set; }
    public string CommitSha { get; set; }
    public DateTime? CommitDate { get; set; }
    public string CommitMessage { get; set; }
    public BranchType BranchType { get; set; }
    public string BranchName { get; set; }
    public int CommitHeight { get; set; }
    public string PreviousVersion { get; set; }
    public string PreviousCommitSha { get; set; }

    /// <summary>
    /// Version bump type determined from commit analysis (Major, Minor, Patch, None)
    /// </summary>
    public VersionBumpType? BumpType { get; set; }

    /// <summary>
    /// Whether conventional commits analysis was used for this version calculation
    /// </summary>
    public bool ConventionalCommitsEnabled { get; set; }

    /// <summary>
    /// List of commit classifications analyzed (for detailed output)
    /// </summary>
    public List<CommitClassification> CommitClassifications { get; set; }

    /// <summary>
    /// Version policy applied to this project (if any)
    /// </summary>
    public VersionPolicy? VersionPolicyApplied { get; set; }

    /// <summary>
    /// Name of the version group this project belongs to (if grouped policy)
    /// </summary>
    public string VersionGroupName { get; set; }

    /// <summary>
    /// List of other projects that share this version (for lock-step/grouped policies)
    /// </summary>
    public List<string> LinkedProjects { get; set; }

    /// <summary>
    /// Version scheme used for this calculation (SemVer or CalVer)
    /// </summary>
    public VersionScheme Scheme { get; set; } = VersionScheme.SemVer;

    /// <summary>
    /// CalVer configuration used (if CalVer scheme was used)
    /// </summary>
    public CalVerConfig CalVerConfig { get; set; }

    /// <summary>
    /// Validation result for this version (if validation was enabled)
    /// </summary>
    public ValidationResult ValidationResult { get; set; }

    /// <summary>
    /// Helper property to get calculated version string (same as Version)
    /// </summary>
    public string CalculatedVersion => Version;
}