using System.Collections.Generic;
using Mister.Version.Core.Models;

namespace Mister.Version.Core.Services
{
    /// <summary>
    /// Service for matching files against glob patterns
    /// </summary>
    public interface IFilePatternMatcher
    {
        /// <summary>
        /// Check if a file path matches a glob pattern
        /// </summary>
        /// <param name="filePath">File path to check</param>
        /// <param name="pattern">Glob pattern (e.g., "**/*.md", "**/docs/**")</param>
        /// <returns>True if the file matches the pattern</returns>
        bool Matches(string filePath, string pattern);

        /// <summary>
        /// Classify a list of changed files based on patterns
        /// </summary>
        /// <param name="changedFiles">List of changed file paths</param>
        /// <param name="config">Change detection configuration</param>
        /// <returns>Classification result</returns>
        ChangeClassification ClassifyChanges(IEnumerable<string> changedFiles, ChangeDetectionConfig config);

        /// <summary>
        /// Determine the required version bump type from classified changes
        /// </summary>
        /// <param name="classification">Change classification</param>
        /// <param name="config">Change detection configuration</param>
        /// <returns>Required bump type, or None if all changes should be ignored</returns>
        VersionBumpType DetermineBumpType(ChangeClassification classification, ChangeDetectionConfig config);
    }
}
