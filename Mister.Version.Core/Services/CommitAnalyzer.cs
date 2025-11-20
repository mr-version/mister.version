using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LibGit2Sharp;
using Mister.Version.Core.Models;

namespace Mister.Version.Core.Services
{
    /// <summary>
    /// Service for analyzing commits to determine version bump types
    /// </summary>
    public interface ICommitAnalyzer
    {
        /// <summary>
        /// Analyzes a collection of commits to determine the highest version bump type needed
        /// </summary>
        /// <param name="commits">Commits to analyze</param>
        /// <param name="config">Conventional commit configuration</param>
        /// <returns>The highest version bump type found across all commits</returns>
        VersionBumpType AnalyzeBumpType(IEnumerable<Commit> commits, ConventionalCommitConfig config);

        /// <summary>
        /// Classifies a single commit message according to conventional commit standards
        /// </summary>
        /// <param name="commit">The commit to classify</param>
        /// <param name="config">Conventional commit configuration</param>
        /// <returns>Classification of the commit</returns>
        CommitClassification ClassifyCommit(Commit commit, ConventionalCommitConfig config);
    }

    /// <summary>
    /// Analyzes commits using conventional commit patterns to determine semantic version bumps
    /// </summary>
    public class ConventionalCommitAnalyzer : ICommitAnalyzer
    {
        private readonly Action<string, string> _logger;

        // Regex pattern for conventional commit format: type(scope)!: description
        // Captures: type, scope (optional), ! (optional), description
        private static readonly Regex ConventionalCommitPattern = new Regex(
            @"^(?<type>\w+)(?:\((?<scope>[^)]+)\))?(?<breaking>!)?\s*:\s*(?<description>.+?)(?:\r?\n|$)",
            RegexOptions.Compiled | RegexOptions.Singleline
        );

        // Regex to find BREAKING CHANGE in commit body/footer
        private static readonly Regex BreakingChangePattern = new Regex(
            @"^BREAKING CHANGE:\s*(?<description>.+?)(?:\r?\n|$)",
            RegexOptions.Compiled | RegexOptions.Multiline
        );

        public ConventionalCommitAnalyzer(Action<string, string> logger = null)
        {
            _logger = logger ?? ((level, message) => { });
        }

        /// <inheritdoc />
        public VersionBumpType AnalyzeBumpType(IEnumerable<Commit> commits, ConventionalCommitConfig config)
        {
            if (commits == null || !commits.Any())
            {
                _logger("Debug", "No commits to analyze");
                return VersionBumpType.None;
            }

            if (config == null || !config.Enabled)
            {
                _logger("Debug", "Conventional commits disabled, using legacy patch behavior");
                return VersionBumpType.Patch;
            }

            var classifications = commits
                .Select(c => ClassifyCommit(c, config))
                .Where(c => !c.ShouldIgnore)
                .ToList();

            if (!classifications.Any())
            {
                _logger("Debug", "All commits were ignored by patterns");
                return VersionBumpType.None;
            }

            // Return the highest bump type found
            // Priority: Major > Minor > Patch > None
            if (classifications.Any(c => c.BumpType == VersionBumpType.Major))
            {
                var majorCommit = classifications.First(c => c.BumpType == VersionBumpType.Major);
                _logger("Info", $"Major version bump triggered by: {majorCommit.CommitSha} - {majorCommit.Description}");
                return VersionBumpType.Major;
            }

            if (classifications.Any(c => c.BumpType == VersionBumpType.Minor))
            {
                var minorCommit = classifications.First(c => c.BumpType == VersionBumpType.Minor);
                _logger("Info", $"Minor version bump triggered by: {minorCommit.CommitSha} - {minorCommit.Description}");
                return VersionBumpType.Minor;
            }

            if (classifications.Any(c => c.BumpType == VersionBumpType.Patch))
            {
                var patchCommit = classifications.First(c => c.BumpType == VersionBumpType.Patch);
                _logger("Info", $"Patch version bump triggered by: {patchCommit.CommitSha} - {patchCommit.Description}");
                return VersionBumpType.Patch;
            }

            _logger("Debug", "No version-triggering commits found");
            return VersionBumpType.None;
        }

        /// <inheritdoc />
        public CommitClassification ClassifyCommit(Commit commit, ConventionalCommitConfig config)
        {
            if (commit == null)
            {
                return new CommitClassification
                {
                    BumpType = VersionBumpType.None,
                    ShouldIgnore = true,
                    Reason = "Null commit"
                };
            }

            var message = commit.Message ?? string.Empty;
            var shortSha = commit.Sha?.Substring(0, Math.Min(7, commit.Sha.Length)) ?? "unknown";

            var classification = new CommitClassification
            {
                Message = message,
                CommitSha = shortSha
            };

            // Check for breaking changes in the commit body/footer
            var breakingMatch = BreakingChangePattern.Match(message);
            if (breakingMatch.Success)
            {
                classification.IsBreakingChange = true;
                classification.BreakingChangeDescription = breakingMatch.Groups["description"].Value.Trim();
            }

            // Parse conventional commit format
            var match = ConventionalCommitPattern.Match(message);
            if (match.Success)
            {
                classification.CommitType = match.Groups["type"].Value.ToLowerInvariant();
                classification.Scope = match.Groups["scope"].Success ? match.Groups["scope"].Value : null;
                classification.Description = match.Groups["description"].Value.Trim();

                // Check for breaking change indicator (!)
                if (match.Groups["breaking"].Success)
                {
                    classification.IsBreakingChange = true;
                    if (string.IsNullOrEmpty(classification.BreakingChangeDescription))
                    {
                        classification.BreakingChangeDescription = classification.Description;
                    }
                }
            }
            else
            {
                // Not a conventional commit - use the full message as description
                classification.CommitType = "unknown";
                classification.Description = message.Split('\n')[0].Trim(); // Use first line
            }

            // Determine bump type based on patterns
            classification.BumpType = DetermineBumpType(message, classification, config);
            classification.ShouldIgnore = classification.BumpType == VersionBumpType.None &&
                                          ShouldIgnoreCommit(message, classification.CommitType, config);

            classification.Reason = GetClassificationReason(classification, config);

            _logger("Debug", $"Classified commit {shortSha}: {classification.CommitType} -> {classification.BumpType} ({classification.Reason})");

            return classification;
        }

        private VersionBumpType DetermineBumpType(string message, CommitClassification classification, ConventionalCommitConfig config)
        {
            // Breaking changes always trigger major version bumps
            if (classification.IsBreakingChange)
            {
                return VersionBumpType.Major;
            }

            // Check if commit should be ignored
            if (config.IgnorePatterns != null && config.IgnorePatterns.Any(pattern => MessageMatchesPattern(message, pattern)))
            {
                return VersionBumpType.None;
            }

            // Check for major patterns
            if (config.MajorPatterns != null && config.MajorPatterns.Any(pattern => MessageMatchesPattern(message, pattern)))
            {
                return VersionBumpType.Major;
            }

            // Check for minor patterns
            if (config.MinorPatterns != null && config.MinorPatterns.Any(pattern => MessageMatchesPattern(message, pattern)))
            {
                return VersionBumpType.Minor;
            }

            // Check for patch patterns
            if (config.PatchPatterns != null && config.PatchPatterns.Any(pattern => MessageMatchesPattern(message, pattern)))
            {
                return VersionBumpType.Patch;
            }

            // Default: if we have file changes but no matching pattern, treat as patch
            // This maintains backward compatibility with the existing behavior
            return VersionBumpType.Patch;
        }

        private bool ShouldIgnoreCommit(string message, string commitType, ConventionalCommitConfig config)
        {
            // Check ignore patterns
            if (config.IgnorePatterns != null && config.IgnorePatterns.Any(pattern => MessageMatchesPattern(message, pattern)))
            {
                return true;
            }

            // Known ignore types not in patterns
            var ignoreTypes = new[] { "chore", "docs", "style", "test", "ci", "build" };
            if (ignoreTypes.Contains(commitType))
            {
                return true;
            }

            return false;
        }

        private bool MessageMatchesPattern(string message, string pattern)
        {
            if (string.IsNullOrEmpty(message) || string.IsNullOrEmpty(pattern))
            {
                return false;
            }

            // Case-insensitive prefix match
            return message.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string GetClassificationReason(CommitClassification classification, ConventionalCommitConfig config)
        {
            if (classification.IsBreakingChange)
            {
                return "Breaking change detected";
            }

            if (classification.ShouldIgnore)
            {
                return $"Ignored commit type: {classification.CommitType}";
            }

            switch (classification.BumpType)
            {
                case VersionBumpType.Major:
                    return "Major pattern matched";
                case VersionBumpType.Minor:
                    return $"Minor pattern matched ({classification.CommitType})";
                case VersionBumpType.Patch:
                    return $"Patch pattern matched ({classification.CommitType})";
                case VersionBumpType.None:
                    return "No version bump pattern matched";
                default:
                    return "Unknown";
            }
        }
    }
}
