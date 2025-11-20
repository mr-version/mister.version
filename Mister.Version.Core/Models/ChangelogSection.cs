using System.Collections.Generic;

namespace Mister.Version.Core.Models
{
    /// <summary>
    /// Represents a section in a changelog (e.g., Breaking Changes, Features, Bug Fixes)
    /// </summary>
    public class ChangelogSection
    {
        /// <summary>
        /// Section title
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Section emoji/icon (optional)
        /// </summary>
        public string Emoji { get; set; }

        /// <summary>
        /// Commit types that belong to this section
        /// </summary>
        public List<string> CommitTypes { get; set; } = new List<string>();

        /// <summary>
        /// Entries in this section
        /// </summary>
        public List<ChangelogEntry> Entries { get; set; } = new List<ChangelogEntry>();

        /// <summary>
        /// Display order (lower numbers first)
        /// </summary>
        public int Order { get; set; }

        /// <summary>
        /// Whether to show this section even if empty
        /// </summary>
        public bool ShowIfEmpty { get; set; }
    }
}
