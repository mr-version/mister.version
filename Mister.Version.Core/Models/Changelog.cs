using System;
using System.Collections.Generic;

namespace Mister.Version.Core.Models
{
    /// <summary>
    /// Represents a complete changelog for a version
    /// </summary>
    public class Changelog
    {
        /// <summary>
        /// Version this changelog is for
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// Previous version
        /// </summary>
        public string PreviousVersion { get; set; }

        /// <summary>
        /// Date the version was released
        /// </summary>
        public DateTimeOffset Date { get; set; }

        /// <summary>
        /// Project name (for monorepo scenarios)
        /// </summary>
        public string ProjectName { get; set; }

        /// <summary>
        /// Sections in this changelog
        /// </summary>
        public List<ChangelogSection> Sections { get; set; } = new List<ChangelogSection>();

        /// <summary>
        /// Total number of commits in this changelog
        /// </summary>
        public int TotalCommits { get; set; }

        /// <summary>
        /// Number of contributors
        /// </summary>
        public int ContributorCount { get; set; }

        /// <summary>
        /// List of contributors
        /// </summary>
        public List<string> Contributors { get; set; } = new List<string>();

        /// <summary>
        /// Version bump type
        /// </summary>
        public VersionBumpType BumpType { get; set; }
    }
}
