using System.Collections.Generic;

namespace Mister.Version.Core.Models
{
    /// <summary>
    /// Configuration for changelog generation
    /// </summary>
    public class ChangelogConfig
    {
        /// <summary>
        /// Whether changelog generation is enabled
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Output format (markdown, json, text)
        /// </summary>
        public string OutputFormat { get; set; } = "markdown";

        /// <summary>
        /// Path to write changelog file
        /// </summary>
        public string OutputPath { get; set; } = "CHANGELOG.md";

        /// <summary>
        /// Whether to include commit links
        /// </summary>
        public bool IncludeCommitLinks { get; set; } = true;

        /// <summary>
        /// Whether to include issue references
        /// </summary>
        public bool IncludeIssueReferences { get; set; } = true;

        /// <summary>
        /// Whether to include PR references
        /// </summary>
        public bool IncludePullRequestReferences { get; set; } = true;

        /// <summary>
        /// Whether to include commit authors
        /// </summary>
        public bool IncludeAuthors { get; set; } = false;

        /// <summary>
        /// Whether to include commit dates
        /// </summary>
        public bool IncludeDates { get; set; } = true;

        /// <summary>
        /// Repository URL for generating links (e.g., https://github.com/owner/repo)
        /// </summary>
        public string RepositoryUrl { get; set; }

        /// <summary>
        /// Custom sections configuration
        /// </summary>
        public List<ChangelogSectionConfig> Sections { get; set; } = new List<ChangelogSectionConfig>
        {
            new ChangelogSectionConfig
            {
                Title = "Breaking Changes",
                Emoji = "💥",
                CommitTypes = new List<string> { "breaking" },
                Order = 1
            },
            new ChangelogSectionConfig
            {
                Title = "Features",
                Emoji = "🚀",
                CommitTypes = new List<string> { "feat", "feature" },
                Order = 2
            },
            new ChangelogSectionConfig
            {
                Title = "Bug Fixes",
                Emoji = "🐛",
                CommitTypes = new List<string> { "fix", "bugfix" },
                Order = 3
            },
            new ChangelogSectionConfig
            {
                Title = "Performance",
                Emoji = "⚡",
                CommitTypes = new List<string> { "perf" },
                Order = 4
            },
            new ChangelogSectionConfig
            {
                Title = "Refactoring",
                Emoji = "♻️",
                CommitTypes = new List<string> { "refactor" },
                Order = 5
            },
            new ChangelogSectionConfig
            {
                Title = "Documentation",
                Emoji = "📝",
                CommitTypes = new List<string> { "docs" },
                Order = 6
            }
        };

        /// <summary>
        /// Whether to group breaking changes separately
        /// </summary>
        public bool GroupBreakingChanges { get; set; } = true;

        /// <summary>
        /// Whether to include scopes in output
        /// </summary>
        public bool IncludeScopes { get; set; } = true;

        /// <summary>
        /// Maximum number of entries per section (0 = unlimited)
        /// </summary>
        public int MaxEntriesPerSection { get; set; } = 0;
    }

    /// <summary>
    /// Configuration for a changelog section
    /// </summary>
    public class ChangelogSectionConfig
    {
        /// <summary>
        /// Section title
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Section emoji/icon
        /// </summary>
        public string Emoji { get; set; }

        /// <summary>
        /// Commit types that belong to this section
        /// </summary>
        public List<string> CommitTypes { get; set; } = new List<string>();

        /// <summary>
        /// Display order
        /// </summary>
        public int Order { get; set; }

        /// <summary>
        /// Whether to show this section even if empty
        /// </summary>
        public bool ShowIfEmpty { get; set; }
    }
}
