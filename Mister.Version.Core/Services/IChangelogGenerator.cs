using System.Collections.Generic;
using LibGit2Sharp;
using Mister.Version.Core.Models;

namespace Mister.Version.Core.Services
{
    /// <summary>
    /// Service for generating changelogs from commit history
    /// </summary>
    public interface IChangelogGenerator
    {
        /// <summary>
        /// Generate a changelog from commits
        /// </summary>
        /// <param name="commits">List of commits to analyze</param>
        /// <param name="version">Version for this changelog</param>
        /// <param name="previousVersion">Previous version</param>
        /// <param name="config">Changelog configuration</param>
        /// <param name="conventionalCommitConfig">Conventional commit configuration for classification</param>
        /// <param name="projectName">Project name (optional, for monorepo scenarios)</param>
        /// <returns>Generated changelog</returns>
        Changelog GenerateChangelog(
            IEnumerable<Commit> commits,
            string version,
            string previousVersion,
            ChangelogConfig config,
            ConventionalCommitConfig conventionalCommitConfig,
            string projectName = null);

        /// <summary>
        /// Format a changelog as markdown
        /// </summary>
        /// <param name="changelog">Changelog to format</param>
        /// <param name="config">Changelog configuration</param>
        /// <returns>Markdown-formatted changelog</returns>
        string FormatAsMarkdown(Changelog changelog, ChangelogConfig config);

        /// <summary>
        /// Format a changelog as plain text
        /// </summary>
        /// <param name="changelog">Changelog to format</param>
        /// <param name="config">Changelog configuration</param>
        /// <returns>Plain text changelog</returns>
        string FormatAsText(Changelog changelog, ChangelogConfig config);

        /// <summary>
        /// Format a changelog as JSON
        /// </summary>
        /// <param name="changelog">Changelog to format</param>
        /// <returns>JSON-formatted changelog</returns>
        string FormatAsJson(Changelog changelog);
    }
}
