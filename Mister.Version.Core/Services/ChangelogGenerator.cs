using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LibGit2Sharp;
using Mister.Version.Core.Models;

namespace Mister.Version.Core.Services
{
    /// <summary>
    /// Service for generating changelogs from commit history
    /// </summary>
    public class ChangelogGenerator : IChangelogGenerator
    {
        private readonly ICommitAnalyzer _commitAnalyzer;

        // Regex patterns for extracting references
        private static readonly Regex IssueReferencePattern = new Regex(
            @"#(\d+)",
            RegexOptions.Compiled);

        private static readonly Regex PullRequestPattern = new Regex(
            @"\(#(\d+)\)\s*$",
            RegexOptions.Compiled);

        public ChangelogGenerator(ICommitAnalyzer commitAnalyzer)
        {
            _commitAnalyzer = commitAnalyzer ?? new ConventionalCommitAnalyzer();
        }

        public Changelog GenerateChangelog(
            IEnumerable<Commit> commits,
            string version,
            string previousVersion,
            ChangelogConfig config,
            ConventionalCommitConfig conventionalCommitConfig,
            string projectName = null)
        {
            var commitList = commits?.ToList() ?? new List<Commit>();

            var changelog = new Changelog
            {
                Version = version,
                PreviousVersion = previousVersion,
                Date = DateTimeOffset.Now,
                ProjectName = projectName,
                TotalCommits = commitList.Count
            };

            // Analyze and classify all commits
            var entries = new List<ChangelogEntry>();
            var contributors = new HashSet<string>();

            foreach (var commit in commitList)
            {
                var classification = _commitAnalyzer.ClassifyCommit(commit, conventionalCommitConfig);

                // Skip ignored commits
                if (classification.ShouldIgnore)
                    continue;

                var entry = CreateChangelogEntry(commit, classification, config);
                entries.Add(entry);

                if (!string.IsNullOrEmpty(commit.Author?.Name))
                    contributors.Add(commit.Author.Name);
            }

            // Determine overall bump type
            changelog.BumpType = entries.Any()
                ? entries.Max(e => e.BumpType)
                : VersionBumpType.None;

            // Group entries into sections
            changelog.Sections = GroupEntriesIntoSections(entries, config);

            // Set contributor information
            changelog.Contributors = contributors.OrderBy(c => c).ToList();
            changelog.ContributorCount = contributors.Count;

            return changelog;
        }

        private ChangelogEntry CreateChangelogEntry(Commit commit, CommitClassification classification, ChangelogConfig config)
        {
            var entry = new ChangelogEntry
            {
                CommitSha = commit.Sha?.Substring(0, Math.Min(7, commit.Sha.Length)),
                Type = classification.CommitType,
                Scope = classification.Scope,
                Description = classification.Description,
                Message = commit.Message,
                IsBreakingChange = classification.IsBreakingChange,
                BreakingChangeDescription = classification.BreakingChangeDescription,
                Author = commit.Author?.Name,
                Date = commit.Author?.When ?? DateTimeOffset.Now,
                BumpType = classification.BumpType
            };

            // Extract issue references
            if (config.IncludeIssueReferences)
            {
                entry.IssueReferences = ExtractIssueReferences(commit.Message);
            }

            // Extract PR number (usually at end of message)
            if (config.IncludePullRequestReferences)
            {
                entry.PullRequestNumber = ExtractPullRequestNumber(commit.Message);
            }

            return entry;
        }

        private string[] ExtractIssueReferences(string message)
        {
            var matches = IssueReferencePattern.Matches(message);
            return matches
                .Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .ToArray();
        }

        private string ExtractPullRequestNumber(string message)
        {
            var match = PullRequestPattern.Match(message);
            return match.Success ? match.Groups[1].Value : null;
        }

        private List<ChangelogSection> GroupEntriesIntoSections(List<ChangelogEntry> entries, ChangelogConfig config)
        {
            var sections = new List<ChangelogSection>();

            // Handle breaking changes separately if configured
            if (config.GroupBreakingChanges)
            {
                var breakingEntries = entries.Where(e => e.IsBreakingChange).ToList();
                if (breakingEntries.Any())
                {
                    var breakingSection = config.Sections.FirstOrDefault(s =>
                        s.CommitTypes.Contains("breaking"))
                        ?? new ChangelogSectionConfig
                        {
                            Title = "Breaking Changes",
                            Emoji = "💥",
                            Order = 0
                        };

                    sections.Add(new ChangelogSection
                    {
                        Title = breakingSection.Title,
                        Emoji = breakingSection.Emoji,
                        Order = breakingSection.Order,
                        Entries = LimitEntries(breakingEntries, config),
                        ShowIfEmpty = breakingSection.ShowIfEmpty
                    });

                    // Remove breaking changes from further processing
                    entries = entries.Where(e => !e.IsBreakingChange).ToList();
                }
            }

            // Group remaining entries by section
            foreach (var sectionConfig in config.Sections.OrderBy(s => s.Order))
            {
                // Skip breaking changes section if we already handled it
                if (config.GroupBreakingChanges && sectionConfig.CommitTypes.Contains("breaking"))
                    continue;

                var sectionEntries = entries
                    .Where(e => sectionConfig.CommitTypes.Contains(e.Type.ToLowerInvariant()))
                    .ToList();

                if (sectionEntries.Any() || sectionConfig.ShowIfEmpty)
                {
                    sections.Add(new ChangelogSection
                    {
                        Title = sectionConfig.Title,
                        Emoji = sectionConfig.Emoji,
                        CommitTypes = sectionConfig.CommitTypes,
                        Order = sectionConfig.Order,
                        Entries = LimitEntries(sectionEntries, config),
                        ShowIfEmpty = sectionConfig.ShowIfEmpty
                    });
                }
            }

            return sections.OrderBy(s => s.Order).ToList();
        }

        private List<ChangelogEntry> LimitEntries(List<ChangelogEntry> entries, ChangelogConfig config)
        {
            if (config.MaxEntriesPerSection > 0 && entries.Count > config.MaxEntriesPerSection)
            {
                return entries.Take(config.MaxEntriesPerSection).ToList();
            }
            return entries;
        }

        public string FormatAsMarkdown(Changelog changelog, ChangelogConfig config)
        {
            var sb = new StringBuilder();

            // Header
            var projectPrefix = !string.IsNullOrEmpty(changelog.ProjectName)
                ? $"{changelog.ProjectName} "
                : "";

            sb.AppendLine($"## {projectPrefix}v{changelog.Version} ({changelog.Date:yyyy-MM-dd})");
            sb.AppendLine();

            // Add summary if there's a previous version
            if (!string.IsNullOrEmpty(changelog.PreviousVersion))
            {
                var bumpTypeText = changelog.BumpType switch
                {
                    VersionBumpType.Major => "**Major release**",
                    VersionBumpType.Minor => "**Minor release**",
                    VersionBumpType.Patch => "**Patch release**",
                    _ => "Release"
                };
                sb.AppendLine($"{bumpTypeText} - {changelog.TotalCommits} commit(s) by {changelog.ContributorCount} contributor(s)");
                sb.AppendLine();
            }

            // Sections
            foreach (var section in changelog.Sections.Where(s => s.Entries.Any() || s.ShowIfEmpty))
            {
                sb.AppendLine($"### {section.Emoji} {section.Title}");
                sb.AppendLine();

                if (!section.Entries.Any())
                {
                    sb.AppendLine("_No changes_");
                    sb.AppendLine();
                    continue;
                }

                foreach (var entry in section.Entries)
                {
                    sb.Append("- ");

                    // Add scope if enabled and present
                    if (config.IncludeScopes && !string.IsNullOrEmpty(entry.Scope))
                    {
                        sb.Append($"**{entry.Scope}:** ");
                    }

                    // Add description
                    sb.Append(entry.Description);

                    // Add issue references
                    if (config.IncludeIssueReferences && entry.IssueReferences.Length > 0)
                    {
                        foreach (var issue in entry.IssueReferences)
                        {
                            if (!string.IsNullOrEmpty(config.RepositoryUrl))
                            {
                                sb.Append($" [#{issue}]({config.RepositoryUrl}/issues/{issue})");
                            }
                            else
                            {
                                sb.Append($" #{issue}");
                            }
                        }
                    }

                    // Add PR reference
                    if (config.IncludePullRequestReferences && !string.IsNullOrEmpty(entry.PullRequestNumber))
                    {
                        if (!string.IsNullOrEmpty(config.RepositoryUrl))
                        {
                            sb.Append($" ([#{entry.PullRequestNumber}]({config.RepositoryUrl}/pull/{entry.PullRequestNumber}))");
                        }
                        else
                        {
                            sb.Append($" (#{entry.PullRequestNumber})");
                        }
                    }

                    // Add commit link
                    if (config.IncludeCommitLinks && !string.IsNullOrEmpty(entry.CommitSha))
                    {
                        if (!string.IsNullOrEmpty(config.RepositoryUrl))
                        {
                            sb.Append($" ([{entry.CommitSha}]({config.RepositoryUrl}/commit/{entry.CommitSha}))");
                        }
                        else
                        {
                            sb.Append($" ({entry.CommitSha})");
                        }
                    }

                    // Add author if enabled
                    if (config.IncludeAuthors && !string.IsNullOrEmpty(entry.Author))
                    {
                        sb.Append($" - @{entry.Author}");
                    }

                    sb.AppendLine();

                    // Add breaking change description if present
                    if (entry.IsBreakingChange && !string.IsNullOrEmpty(entry.BreakingChangeDescription))
                    {
                        sb.AppendLine($"  > ⚠️ **BREAKING:** {entry.BreakingChangeDescription}");
                    }
                }

                sb.AppendLine();
            }

            // Contributors section
            if (changelog.Contributors.Any() && config.IncludeAuthors)
            {
                sb.AppendLine("### 👥 Contributors");
                sb.AppendLine();
                foreach (var contributor in changelog.Contributors)
                {
                    sb.AppendLine($"- @{contributor}");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public string FormatAsText(Changelog changelog, ChangelogConfig config)
        {
            var sb = new StringBuilder();

            // Header
            var projectPrefix = !string.IsNullOrEmpty(changelog.ProjectName)
                ? $"{changelog.ProjectName} "
                : "";

            sb.AppendLine($"{projectPrefix}v{changelog.Version} ({changelog.Date:yyyy-MM-dd})");
            sb.AppendLine(new string('=', 60));
            sb.AppendLine();

            // Summary
            if (!string.IsNullOrEmpty(changelog.PreviousVersion))
            {
                var bumpTypeText = changelog.BumpType switch
                {
                    VersionBumpType.Major => "MAJOR release",
                    VersionBumpType.Minor => "MINOR release",
                    VersionBumpType.Patch => "PATCH release",
                    _ => "Release"
                };
                sb.AppendLine($"{bumpTypeText} - {changelog.TotalCommits} commit(s) by {changelog.ContributorCount} contributor(s)");
                sb.AppendLine();
            }

            // Sections
            foreach (var section in changelog.Sections.Where(s => s.Entries.Any() || s.ShowIfEmpty))
            {
                sb.AppendLine($"{section.Title}");
                sb.AppendLine(new string('-', section.Title.Length));
                sb.AppendLine();

                if (!section.Entries.Any())
                {
                    sb.AppendLine("  No changes");
                    sb.AppendLine();
                    continue;
                }

                foreach (var entry in section.Entries)
                {
                    sb.Append("  * ");

                    if (config.IncludeScopes && !string.IsNullOrEmpty(entry.Scope))
                    {
                        sb.Append($"[{entry.Scope}] ");
                    }

                    sb.Append(entry.Description);

                    if (config.IncludeIssueReferences && entry.IssueReferences.Length > 0)
                    {
                        sb.Append($" (#{string.Join(", #", entry.IssueReferences)})");
                    }

                    if (!string.IsNullOrEmpty(entry.CommitSha))
                    {
                        sb.Append($" ({entry.CommitSha})");
                    }

                    if (config.IncludeAuthors && !string.IsNullOrEmpty(entry.Author))
                    {
                        sb.Append($" - {entry.Author}");
                    }

                    sb.AppendLine();

                    if (entry.IsBreakingChange && !string.IsNullOrEmpty(entry.BreakingChangeDescription))
                    {
                        sb.AppendLine($"    WARNING: {entry.BreakingChangeDescription}");
                    }
                }

                sb.AppendLine();
            }

            // Contributors
            if (changelog.Contributors.Any() && config.IncludeAuthors)
            {
                sb.AppendLine("Contributors");
                sb.AppendLine("-----------");
                sb.AppendLine();
                foreach (var contributor in changelog.Contributors)
                {
                    sb.AppendLine($"  * {contributor}");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public string FormatAsJson(Changelog changelog)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            return JsonSerializer.Serialize(changelog, options);
        }
    }
}
