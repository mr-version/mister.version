namespace Mister.Version.Core.Models;

/// <summary>
/// Classification of a commit based on conventional commit analysis
/// </summary>
public class CommitClassification
{
    /// <summary>
    /// The commit message being classified
    /// </summary>
    public string Message { get; set; }

    /// <summary>
    /// The commit SHA (short hash)
    /// </summary>
    public string CommitSha { get; set; }

    /// <summary>
    /// The type of version bump this commit indicates
    /// </summary>
    public VersionBumpType BumpType { get; set; }

    /// <summary>
    /// The commit type extracted from conventional commit (feat, fix, chore, etc.)
    /// </summary>
    public string CommitType { get; set; }

    /// <summary>
    /// The commit scope if present (e.g., "core" in "feat(core): add feature")
    /// </summary>
    public string Scope { get; set; }

    /// <summary>
    /// The commit description (subject line without type/scope)
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Whether this commit contains a breaking change indicator
    /// </summary>
    public bool IsBreakingChange { get; set; }

    /// <summary>
    /// Breaking change description if present
    /// </summary>
    public string BreakingChangeDescription { get; set; }

    /// <summary>
    /// Whether this commit should be ignored for versioning
    /// </summary>
    public bool ShouldIgnore { get; set; }

    /// <summary>
    /// Reason why this classification was determined
    /// </summary>
    public string Reason { get; set; }
}
