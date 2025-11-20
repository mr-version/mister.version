using System;

namespace Mister.Version.Core.Models
{
    /// <summary>
    /// Represents a single entry in a changelog
    /// </summary>
    public class ChangelogEntry
    {
        /// <summary>
        /// Commit SHA
        /// </summary>
        public string CommitSha { get; set; }

        /// <summary>
        /// Commit type (feat, fix, etc.)
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Scope of the change (optional)
        /// </summary>
        public string Scope { get; set; }

        /// <summary>
        /// Description of the change
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Full commit message
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Whether this is a breaking change
        /// </summary>
        public bool IsBreakingChange { get; set; }

        /// <summary>
        /// Breaking change description (if applicable)
        /// </summary>
        public string BreakingChangeDescription { get; set; }

        /// <summary>
        /// Commit author
        /// </summary>
        public string Author { get; set; }

        /// <summary>
        /// Commit date
        /// </summary>
        public DateTimeOffset Date { get; set; }

        /// <summary>
        /// GitHub issue numbers referenced in the commit
        /// </summary>
        public string[] IssueReferences { get; set; } = Array.Empty<string>();

        /// <summary>
        /// GitHub PR number (if available)
        /// </summary>
        public string PullRequestNumber { get; set; }

        /// <summary>
        /// Version bump type for this entry
        /// </summary>
        public VersionBumpType BumpType { get; set; }
    }
}
