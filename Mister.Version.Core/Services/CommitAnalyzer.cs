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

        // "BREAKING CHANGE:" footer (or its synonym "BREAKING-CHANGE:") at the start of a line after the subject.
        // Case-sensitive as the Conventional Commits spec requires, so prose such as "not a breaking change:" in a
        // description does not make a fix a major release.
        private static readonly Regex BreakingChangePattern = new Regex(
            @"(?:\A|\n)(?:BREAKING CHANGE|BREAKING-CHANGE):[ \t]*(?<description>[^\r\n]*)",
            RegexOptions.Compiled
        );

        // Prefixes in front of the conventional type that teams commonly add: "[ABC-123] ", "ABC-123: ", gitmoji
        // (":sparkles: " or an emoji), Azure DevOps "Merged PR 12: "
        private static readonly Regex SubjectPrefixPattern = new Regex(
            @"^(?:\s*(?:\[[^\]\r\n]+\]:?|\(?[A-Z][A-Z0-9]+-\d+\)?:?|#\d+:?|:[a-z0-9_+\-]+:|Merged PR \d+:|(?:[^\w\s\[\]('""`:#]|\p{Mn}|\p{Cf})+)\s*)+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant
        );

        // Merge commits whose own subject says nothing; GitHub puts the pull request title in the body
        private static readonly Regex GenericMergeSubjectPattern = new Regex(
            @"^Merge (?:pull request|branch|remote-tracking branch|commit|tag)\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant
        );

        /// <summary>
        /// The message with its subject reduced to what conventional-commit parsing should see: for a generic
        /// "Merge pull request #12 from ..." the pull request title from the body, and without ticket, gitmoji or
        /// "Merged PR 12:" prefixes. The body is kept (for "BREAKING CHANGE:" footers).
        /// </summary>
        public static string GetEffectiveMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return message ?? string.Empty;

            var normalized = message.Replace("\r\n", "\n");
            var newline = normalized.IndexOf('\n');
            var subject = newline >= 0 ? normalized.Substring(0, newline) : normalized;
            var body = newline >= 0 ? normalized.Substring(newline + 1) : string.Empty;

            if (GenericMergeSubjectPattern.IsMatch(subject))
            {
                var title = body.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
                if (title != null)
                {
                    subject = title;
                }
            }

            if (!subject.StartsWith("Revert ", StringComparison.Ordinal))
            {
                var prefix = SubjectPrefixPattern.Match(subject);
                if (prefix.Success && prefix.Length < subject.Length)
                {
                    subject = subject.Substring(prefix.Length);
                }
            }

            return body.Length > 0 ? subject + "\n" + body : subject;
        }

        /// <summary>Whether a commit message has a recognizable conventional type (after <see cref="GetEffectiveMessage"/>).</summary>
        public static bool HasConventionalType(string message) =>
            ConventionalCommitPattern.IsMatch(GetEffectiveMessage(message ?? string.Empty));

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

            var message = GetEffectiveMessage(commit.Message ?? string.Empty);
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
            if (config.IgnorePatterns != null && config.IgnorePatterns.Any(pattern => MessageMatchesPattern(message, classification, pattern)))
            {
                return VersionBumpType.None;
            }

            // Check for major patterns
            if (config.MajorPatterns != null && config.MajorPatterns.Any(pattern => MessageMatchesPattern(message, classification, pattern)))
            {
                return VersionBumpType.Major;
            }

            // Check for minor patterns
            if (config.MinorPatterns != null && config.MinorPatterns.Any(pattern => MessageMatchesPattern(message, classification, pattern)))
            {
                return VersionBumpType.Minor;
            }

            // Check for patch patterns
            if (config.PatchPatterns != null && config.PatchPatterns.Any(pattern => MessageMatchesPattern(message, classification, pattern)))
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
            if (config.IgnorePatterns != null && config.IgnorePatterns.Any(pattern => MessageMatchesPattern(message, null, pattern, commitType)))
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

        private static readonly Regex TypePattern = new Regex(@"^(?<type>[A-Za-z][\w-]*)!?:$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Matches a configured commit pattern against a commit.
        /// "type:" patterns (feat:, fix:, docs:, ...) match the parsed conventional-commit type, with or without a
        /// scope ("feat(api): ...") or '!'; they used to be raw substring matches, so "feat(api):" fell through to a
        /// patch bump and a "docs:" mentioned inside a fix description made the fix ignored. "BREAKING CHANGE:" and
        /// "!:" are recognized structurally. Other (free-form) patterns match within the subject line.
        /// </summary>
        private bool MessageMatchesPattern(string message, CommitClassification classification, string pattern, string commitType = null)
        {
            if (string.IsNullOrEmpty(message) || string.IsNullOrEmpty(pattern))
            {
                return false;
            }

            var trimmed = pattern.Trim();
            var type = classification?.CommitType ?? commitType;

            if (trimmed == "!:")
            {
                return classification != null && classification.IsBreakingChange;
            }

            if (trimmed.Equals("BREAKING CHANGE:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("BREAKING-CHANGE:", StringComparison.OrdinalIgnoreCase))
            {
                return BreakingChangePattern.IsMatch(message.Replace("\r\n", "\n"));
            }

            var newline = message.IndexOf('\n');
            var subject = (newline >= 0 ? message.Substring(0, newline) : message).TrimEnd('\r');

            var typeMatch = TypePattern.Match(trimmed);
            if (typeMatch.Success)
            {
                if (!string.IsNullOrEmpty(type) && type != "unknown")
                {
                    return string.Equals(type, typeMatch.Groups["type"].Value, StringComparison.OrdinalIgnoreCase);
                }

                // A type pattern only describes the start of a subject: 'Revert "feat: x"' or
                // "Merge branch 'fix: y'" are not features or fixes
                return subject.TrimStart().StartsWith(trimmed, StringComparison.OrdinalIgnoreCase);
            }

            // Free-form pattern: case-insensitive match within the subject (first) line
            return subject.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase) >= 0;
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
