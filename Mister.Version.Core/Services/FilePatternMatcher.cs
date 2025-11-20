using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Mister.Version.Core.Models;

namespace Mister.Version.Core.Services
{
    /// <summary>
    /// Service for matching files against glob patterns
    /// </summary>
    public class FilePatternMatcher : IFilePatternMatcher
    {
        /// <summary>
        /// Check if a file path matches a glob pattern
        /// </summary>
        public bool Matches(string filePath, string pattern)
        {
            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(pattern))
                return false;

            // Normalize path separators to forward slashes
            filePath = filePath.Replace('\\', '/');
            pattern = pattern.Replace('\\', '/');

            // Convert glob pattern to regex
            var regex = GlobToRegex(pattern);
            return regex.IsMatch(filePath);
        }

        /// <summary>
        /// Classify a list of changed files based on patterns
        /// </summary>
        public ChangeClassification ClassifyChanges(IEnumerable<string> changedFiles, ChangeDetectionConfig config)
        {
            var classification = new ChangeClassification();
            var files = changedFiles?.ToList() ?? new List<string>();
            classification.TotalFiles = files.Count;

            if (files.Count == 0)
            {
                classification.ShouldIgnore = true;
                classification.Reason = "No files changed";
                return classification;
            }

            foreach (var file in files)
            {
                var normalized = file.Replace('\\', '/');
                bool classified = false;

                // Check ignore patterns first
                if (config.IgnorePatterns != null && config.IgnorePatterns.Any(p => Matches(normalized, p)))
                {
                    classification.IgnoredFiles.Add(file);
                    classified = true;
                    continue;
                }

                // Check major patterns
                if (config.MajorPatterns != null && config.MajorPatterns.Any(p => Matches(normalized, p)))
                {
                    classification.MajorFiles.Add(file);
                    classified = true;
                    continue;
                }

                // Check minor patterns
                if (config.MinorPatterns != null && config.MinorPatterns.Any(p => Matches(normalized, p)))
                {
                    classification.MinorFiles.Add(file);
                    classified = true;
                    continue;
                }

                // Check patch patterns
                if (config.PatchPatterns != null && config.PatchPatterns.Any(p => Matches(normalized, p)))
                {
                    classification.PatchFiles.Add(file);
                    classified = true;
                    continue;
                }

                // If not classified, add to unclassified
                if (!classified)
                {
                    classification.UnclassifiedFiles.Add(file);
                }
            }

            return classification;
        }

        /// <summary>
        /// Determine the required version bump type from classified changes
        /// </summary>
        public VersionBumpType DetermineBumpType(ChangeClassification classification, ChangeDetectionConfig config)
        {
            // If source-only mode is enabled, ignore all files that matched ignore patterns
            if (config.SourceOnlyMode)
            {
                var nonIgnoredCount = classification.TotalFiles - classification.IgnoredFiles.Count;
                if (nonIgnoredCount == 0)
                {
                    classification.ShouldIgnore = true;
                    classification.Reason = "All changes are in ignored files (source-only mode)";
                    classification.RequiredBumpType = VersionBumpType.None;
                    return VersionBumpType.None;
                }
            }
            else
            {
                // In normal mode, if ALL files are ignored, skip versioning
                if (classification.IgnoredFiles.Count == classification.TotalFiles && classification.TotalFiles > 0)
                {
                    classification.ShouldIgnore = true;
                    classification.Reason = "All changes are in ignored files";
                    classification.RequiredBumpType = VersionBumpType.None;
                    return VersionBumpType.None;
                }
            }

            // Determine bump type based on priority: Major > Minor > Patch > None
            VersionBumpType bumpType = VersionBumpType.None;
            var reasons = new List<string>();

            if (classification.MajorFiles.Count > 0)
            {
                bumpType = VersionBumpType.Major;
                reasons.Add($"{classification.MajorFiles.Count} file(s) require major version bump");
            }
            else if (classification.MinorFiles.Count > 0)
            {
                bumpType = VersionBumpType.Minor;
                reasons.Add($"{classification.MinorFiles.Count} file(s) require minor version bump");
            }
            else if (classification.PatchFiles.Count > 0)
            {
                bumpType = VersionBumpType.Patch;
                reasons.Add($"{classification.PatchFiles.Count} file(s) require patch version bump");
            }
            else if (classification.UnclassifiedFiles.Count > 0)
            {
                // Unclassified files use the minimum bump type from config
                bumpType = config.MinimumBumpType;
                if (bumpType == VersionBumpType.None)
                {
                    bumpType = VersionBumpType.Patch; // Default to patch if not specified
                }
                reasons.Add($"{classification.UnclassifiedFiles.Count} unclassified file(s) changed");
            }

            // Apply minimum bump type if configured
            if (config.MinimumBumpType > bumpType)
            {
                bumpType = config.MinimumBumpType;
                reasons.Add($"Minimum bump type enforced: {config.MinimumBumpType}");
            }

            classification.RequiredBumpType = bumpType;
            classification.ShouldIgnore = bumpType == VersionBumpType.None;
            classification.Reason = string.Join("; ", reasons);

            return bumpType;
        }

        /// <summary>
        /// Convert a glob pattern to a regular expression
        /// </summary>
        private Regex GlobToRegex(string pattern)
        {
            // Escape special regex characters except * and ?
            var regexPattern = Regex.Escape(pattern)
                .Replace("\\*\\*/", "DOUBLE_STAR_SLASH")  // Temporarily replace **/
                .Replace("\\*", "SINGLE_STAR")            // Temporarily replace *
                .Replace("\\?", "SINGLE_CHAR")            // Temporarily replace ?
                .Replace("DOUBLE_STAR_SLASH", "(.*/)?")   // **/ matches zero or more directories
                .Replace("SINGLE_STAR", "[^/]*")           // * matches any characters except /
                .Replace("SINGLE_CHAR", "[^/]");           // ? matches any single character except /

            // Ensure pattern matches the entire path
            regexPattern = "^" + regexPattern + "$";

            return new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
    }
}
