using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using LibGit2Sharp;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;

namespace Mister.Version.Tests
{
    public class ChangelogGeneratorTests
    {
        private readonly ChangelogGenerator _generator;
        private readonly ConventionalCommitConfig _defaultConventionalConfig;
        private readonly ChangelogConfig _defaultChangelogConfig;

        public ChangelogGeneratorTests()
        {
            _generator = new ChangelogGenerator(new ConventionalCommitAnalyzer());

            _defaultConventionalConfig = new ConventionalCommitConfig
            {
                Enabled = true,
                MajorPatterns = new List<string> { "BREAKING CHANGE:", "!:" },
                MinorPatterns = new List<string> { "feat:", "feature:" },
                PatchPatterns = new List<string> { "fix:", "bugfix:", "perf:", "refactor:" },
                IgnorePatterns = new List<string> { "chore:", "docs:", "style:", "test:", "ci:" }
            };

            _defaultChangelogConfig = new ChangelogConfig
            {
                Enabled = true,
                OutputFormat = "markdown",
                IncludeCommitLinks = true,
                IncludeIssueReferences = true,
                IncludePullRequestReferences = true,
                IncludeAuthors = false,
                GroupBreakingChanges = true,
                RepositoryUrl = "https://github.com/owner/repo"
            };
        }

        #region GenerateChangelog Tests

        [Fact]
        public void GenerateChangelog_NoCommits_ReturnsEmptyChangelog()
        {
            // Arrange
            var commits = new List<Commit>();

            // Act
            var changelog = _generator.GenerateChangelog(
                commits,
                "1.0.0",
                "0.9.0",
                _defaultChangelogConfig,
                _defaultConventionalConfig);

            // Assert
            Assert.NotNull(changelog);
            Assert.Equal("1.0.0", changelog.Version);
            Assert.Equal("0.9.0", changelog.PreviousVersion);
            Assert.Equal(0, changelog.TotalCommits);
            Assert.Equal(0, changelog.ContributorCount);
            Assert.Equal(VersionBumpType.None, changelog.BumpType);
        }

        [Fact]
        public void GenerateChangelog_WithFeatureCommit_CreatesFeaturesSection()
        {
            // Arrange
            var commits = new List<Commit>
            {
                CreateTestCommit("feat: add new feature", "John Doe")
            };

            // Act
            var changelog = _generator.GenerateChangelog(
                commits,
                "1.1.0",
                "1.0.0",
                _defaultChangelogConfig,
                _defaultConventionalConfig);

            // Assert
            Assert.Equal(1, changelog.TotalCommits);
            Assert.Equal(VersionBumpType.Minor, changelog.BumpType);

            var featuresSection = changelog.Sections.FirstOrDefault(s => s.Title == "Features");
            Assert.NotNull(featuresSection);
            Assert.Single(featuresSection.Entries);
            Assert.Equal("add new feature", featuresSection.Entries[0].Description);
        }

        [Fact]
        public void GenerateChangelog_WithBreakingChange_CreatesBreakingSection()
        {
            // Arrange
            var commits = new List<Commit>
            {
                CreateTestCommit("feat!: remove old API", "Jane Doe")
            };

            // Act
            var changelog = _generator.GenerateChangelog(
                commits,
                "2.0.0",
                "1.0.0",
                _defaultChangelogConfig,
                _defaultConventionalConfig);

            // Assert
            Assert.Equal(VersionBumpType.Major, changelog.BumpType);

            var breakingSection = changelog.Sections.FirstOrDefault(s => s.Title == "Breaking Changes");
            Assert.NotNull(breakingSection);
            Assert.Single(breakingSection.Entries);
            Assert.True(breakingSection.Entries[0].IsBreakingChange);
        }

        [Fact]
        public void GenerateChangelog_WithMultipleCommitTypes_GroupsCorrectly()
        {
            // Arrange
            var commits = new List<Commit>
            {
                CreateTestCommit("feat: add feature 1", "John Doe"),
                CreateTestCommit("feat: add feature 2", "Jane Doe"),
                CreateTestCommit("fix: resolve bug 1", "John Doe"),
                CreateTestCommit("fix: resolve bug 2", "Bob Smith"),
                CreateTestCommit("perf: optimize query", "Jane Doe"),
                CreateTestCommit("refactor: clean up code", "John Doe")
            };

            // Act
            var changelog = _generator.GenerateChangelog(
                commits,
                "1.1.0",
                "1.0.0",
                _defaultChangelogConfig,
                _defaultConventionalConfig);

            // Assert
            Assert.Equal(6, changelog.TotalCommits);
            Assert.Equal(3, changelog.ContributorCount);
            Assert.Equal(VersionBumpType.Minor, changelog.BumpType);

            var featuresSection = changelog.Sections.FirstOrDefault(s => s.Title == "Features");
            Assert.NotNull(featuresSection);
            Assert.Equal(2, featuresSection.Entries.Count);

            var bugFixesSection = changelog.Sections.FirstOrDefault(s => s.Title == "Bug Fixes");
            Assert.NotNull(bugFixesSection);
            Assert.Equal(2, bugFixesSection.Entries.Count);

            var performanceSection = changelog.Sections.FirstOrDefault(s => s.Title == "Performance");
            Assert.NotNull(performanceSection);
            Assert.Single(performanceSection.Entries);

            var refactoringSection = changelog.Sections.FirstOrDefault(s => s.Title == "Refactoring");
            Assert.NotNull(refactoringSection);
            Assert.Single(refactoringSection.Entries);
        }

        [Fact]
        public void GenerateChangelog_IgnoresConfiguredCommitTypes()
        {
            // Arrange
            var commits = new List<Commit>
            {
                CreateTestCommit("feat: add feature", "John Doe"),
                CreateTestCommit("chore: update dependencies", "Jane Doe"),
                CreateTestCommit("docs: update README", "Bob Smith"),
                CreateTestCommit("style: format code", "John Doe"),
                CreateTestCommit("test: add tests", "Jane Doe"),
                CreateTestCommit("ci: update pipeline", "Bob Smith")
            };

            // Act
            var changelog = _generator.GenerateChangelog(
                commits,
                "1.1.0",
                "1.0.0",
                _defaultChangelogConfig,
                _defaultConventionalConfig);

            // Assert
            Assert.Equal(6, changelog.TotalCommits);

            // Only the feature commit should be in sections
            var totalEntries = changelog.Sections.Sum(s => s.Entries.Count);
            Assert.Equal(1, totalEntries);
        }

        [Fact]
        public void GenerateChangelog_WithScopes_CapturesScopes()
        {
            // Arrange
            var commits = new List<Commit>
            {
                CreateTestCommit("feat(api): add new endpoint", "John Doe"),
                CreateTestCommit("fix(ui): resolve button issue", "Jane Doe")
            };

            // Act
            var changelog = _generator.GenerateChangelog(
                commits,
                "1.1.0",
                "1.0.0",
                _defaultChangelogConfig,
                _defaultConventionalConfig);

            // Assert
            var featuresSection = changelog.Sections.FirstOrDefault(s => s.Title == "Features");
            Assert.NotNull(featuresSection);
            Assert.Equal("api", featuresSection.Entries[0].Scope);
            Assert.Equal("add new endpoint", featuresSection.Entries[0].Description);

            var bugFixesSection = changelog.Sections.FirstOrDefault(s => s.Title == "Bug Fixes");
            Assert.NotNull(bugFixesSection);
            Assert.Equal("ui", bugFixesSection.Entries[0].Scope);
        }

        [Fact]
        public void GenerateChangelog_WithIssueReferences_ExtractsIssues()
        {
            // Arrange
            var commits = new List<Commit>
            {
                CreateTestCommit("feat: add feature (#42)", "John Doe"),
                CreateTestCommit("fix: resolve bug #123 and #456", "Jane Doe")
            };

            // Act
            var changelog = _generator.GenerateChangelog(
                commits,
                "1.1.0",
                "1.0.0",
                _defaultChangelogConfig,
                _defaultConventionalConfig);

            // Assert
            var featuresSection = changelog.Sections.FirstOrDefault(s => s.Title == "Features");
            Assert.NotNull(featuresSection);
            Assert.Contains("42", featuresSection.Entries[0].IssueReferences);

            var bugFixesSection = changelog.Sections.FirstOrDefault(s => s.Title == "Bug Fixes");
            Assert.NotNull(bugFixesSection);
            Assert.Contains("123", bugFixesSection.Entries[0].IssueReferences);
            Assert.Contains("456", bugFixesSection.Entries[0].IssueReferences);
        }

        [Fact]
        public void GenerateChangelog_WithPRReference_ExtractsPR()
        {
            // Arrange
            var commits = new List<Commit>
            {
                CreateTestCommit("feat: add feature (#42)", "John Doe")
            };

            // Act
            var changelog = _generator.GenerateChangelog(
                commits,
                "1.1.0",
                "1.0.0",
                _defaultChangelogConfig,
                _defaultConventionalConfig);

            // Assert
            var featuresSection = changelog.Sections.FirstOrDefault(s => s.Title == "Features");
            Assert.NotNull(featuresSection);
            Assert.Equal("42", featuresSection.Entries[0].PullRequestNumber);
        }

        [Fact]
        public void GenerateChangelog_WithBreakingChangeFooter_ExtractsDescription()
        {
            // Arrange
            var message = @"fix: resolve authentication bug

BREAKING CHANGE: The authentication flow has changed.
Users must re-authenticate.";

            var commits = new List<Commit>
            {
                CreateTestCommit(message, "John Doe")
            };

            // Act
            var changelog = _generator.GenerateChangelog(
                commits,
                "2.0.0",
                "1.0.0",
                _defaultChangelogConfig,
                _defaultConventionalConfig);

            // Assert
            var breakingSection = changelog.Sections.FirstOrDefault(s => s.Title == "Breaking Changes");
            Assert.NotNull(breakingSection);
            Assert.True(breakingSection.Entries[0].IsBreakingChange);
            Assert.Contains("authentication flow has changed", breakingSection.Entries[0].BreakingChangeDescription);
        }

        [Fact]
        public void GenerateChangelog_TracksContributors()
        {
            // Arrange
            var commits = new List<Commit>
            {
                CreateTestCommit("feat: feature 1", "John Doe"),
                CreateTestCommit("feat: feature 2", "Jane Doe"),
                CreateTestCommit("fix: fix 1", "John Doe"),
                CreateTestCommit("fix: fix 2", "Bob Smith")
            };

            // Act
            var changelog = _generator.GenerateChangelog(
                commits,
                "1.1.0",
                "1.0.0",
                _defaultChangelogConfig,
                _defaultConventionalConfig);

            // Assert
            Assert.Equal(3, changelog.ContributorCount);
            Assert.Contains("John Doe", changelog.Contributors);
            Assert.Contains("Jane Doe", changelog.Contributors);
            Assert.Contains("Bob Smith", changelog.Contributors);
        }

        #endregion

        #region FormatAsMarkdown Tests

        [Fact]
        public void FormatAsMarkdown_IncludesVersionHeader()
        {
            // Arrange
            var changelog = new Changelog
            {
                Version = "1.1.0",
                PreviousVersion = "1.0.0",
                Date = new DateTimeOffset(2025, 11, 20, 0, 0, 0, TimeSpan.Zero),
                TotalCommits = 5,
                ContributorCount = 2,
                BumpType = VersionBumpType.Minor
            };

            // Act
            var markdown = _generator.FormatAsMarkdown(changelog, _defaultChangelogConfig);

            // Assert
            Assert.Contains("## v1.1.0 (2025-11-20)", markdown);
            Assert.Contains("**Minor release**", markdown);
            Assert.Contains("5 commit(s)", markdown);
            Assert.Contains("2 contributor(s)", markdown);
        }

        [Fact]
        public void FormatAsMarkdown_WithProjectName_IncludesInHeader()
        {
            // Arrange
            var changelog = new Changelog
            {
                Version = "1.1.0",
                ProjectName = "Core",
                Date = DateTimeOffset.Now
            };

            // Act
            var markdown = _generator.FormatAsMarkdown(changelog, _defaultChangelogConfig);

            // Assert
            Assert.Contains("## Core v1.1.0", markdown);
        }

        [Fact]
        public void FormatAsMarkdown_WithSections_FormatsCorrectly()
        {
            // Arrange
            var changelog = new Changelog
            {
                Version = "1.1.0",
                Date = DateTimeOffset.Now,
                Sections = new List<ChangelogSection>
                {
                    new ChangelogSection
                    {
                        Title = "Features",
                        Emoji = "🚀",
                        Entries = new List<ChangelogEntry>
                        {
                            new ChangelogEntry
                            {
                                Description = "add new feature",
                                CommitSha = "abc1234"
                            }
                        }
                    }
                }
            };

            // Act
            var markdown = _generator.FormatAsMarkdown(changelog, _defaultChangelogConfig);

            // Assert
            Assert.Contains("### 🚀 Features", markdown);
            Assert.Contains("- add new feature", markdown);
        }

        [Fact]
        public void FormatAsMarkdown_WithScopes_IncludesScopes()
        {
            // Arrange
            var config = new ChangelogConfig
            {
                IncludeScopes = true
            };

            var changelog = new Changelog
            {
                Version = "1.1.0",
                Date = DateTimeOffset.Now,
                Sections = new List<ChangelogSection>
                {
                    new ChangelogSection
                    {
                        Title = "Features",
                        Emoji = "🚀",
                        Entries = new List<ChangelogEntry>
                        {
                            new ChangelogEntry
                            {
                                Scope = "api",
                                Description = "add new endpoint",
                                CommitSha = "abc1234"
                            }
                        }
                    }
                }
            };

            // Act
            var markdown = _generator.FormatAsMarkdown(changelog, config);

            // Assert
            Assert.Contains("**api:** add new endpoint", markdown);
        }

        [Fact]
        public void FormatAsMarkdown_WithRepositoryUrl_GeneratesLinks()
        {
            // Arrange
            var changelog = new Changelog
            {
                Version = "1.1.0",
                Date = DateTimeOffset.Now,
                Sections = new List<ChangelogSection>
                {
                    new ChangelogSection
                    {
                        Title = "Features",
                        Entries = new List<ChangelogEntry>
                        {
                            new ChangelogEntry
                            {
                                Description = "add feature",
                                CommitSha = "abc1234",
                                IssueReferences = new[] { "42" }
                            }
                        }
                    }
                }
            };

            // Act
            var markdown = _generator.FormatAsMarkdown(changelog, _defaultChangelogConfig);

            // Assert
            Assert.Contains("[#42](https://github.com/owner/repo/issues/42)", markdown);
            Assert.Contains("[abc1234](https://github.com/owner/repo/commit/abc1234)", markdown);
        }

        [Fact]
        public void FormatAsMarkdown_WithBreakingChange_HighlightsIt()
        {
            // Arrange
            var changelog = new Changelog
            {
                Version = "2.0.0",
                Date = DateTimeOffset.Now,
                Sections = new List<ChangelogSection>
                {
                    new ChangelogSection
                    {
                        Title = "Breaking Changes",
                        Emoji = "💥",
                        Entries = new List<ChangelogEntry>
                        {
                            new ChangelogEntry
                            {
                                Description = "remove old API",
                                IsBreakingChange = true,
                                BreakingChangeDescription = "The old API has been removed"
                            }
                        }
                    }
                }
            };

            // Act
            var markdown = _generator.FormatAsMarkdown(changelog, _defaultChangelogConfig);

            // Assert
            Assert.Contains("### 💥 Breaking Changes", markdown);
            Assert.Contains("⚠️ **BREAKING:** The old API has been removed", markdown);
        }

        #endregion

        #region FormatAsText Tests

        [Fact]
        public void FormatAsText_FormatsCorrectly()
        {
            // Arrange
            var changelog = new Changelog
            {
                Version = "1.1.0",
                PreviousVersion = "1.0.0",
                Date = new DateTimeOffset(2025, 11, 20, 0, 0, 0, TimeSpan.Zero),
                TotalCommits = 3,
                ContributorCount = 2,
                BumpType = VersionBumpType.Minor,
                Sections = new List<ChangelogSection>
                {
                    new ChangelogSection
                    {
                        Title = "Features",
                        Entries = new List<ChangelogEntry>
                        {
                            new ChangelogEntry { Description = "add feature" }
                        }
                    }
                }
            };

            // Act
            var text = _generator.FormatAsText(changelog, _defaultChangelogConfig);

            // Assert
            Assert.Contains("v1.1.0 (2025-11-20)", text);
            Assert.Contains("MINOR release", text);
            Assert.Contains("Features", text);
            Assert.Contains("* add feature", text);
        }

        #endregion

        #region FormatAsJson Tests

        [Fact]
        public void FormatAsJson_ReturnsValidJson()
        {
            // Arrange
            var changelog = new Changelog
            {
                Version = "1.1.0",
                PreviousVersion = "1.0.0",
                Date = DateTimeOffset.Now,
                TotalCommits = 1,
                BumpType = VersionBumpType.Minor
            };

            // Act
            var json = _generator.FormatAsJson(changelog);

            // Assert
            Assert.NotNull(json);
            Assert.Contains("\"version\": \"1.1.0\"", json);
            Assert.Contains("\"previousVersion\": \"1.0.0\"", json);
            Assert.Contains("\"bumpType\":", json);
        }

        #endregion

        #region Helper Methods

        private Commit CreateTestCommit(string message, string author = "Test Author")
        {
            return new TestCommitWithAuthor(message, author);
        }

        #endregion
    }

    // Test commit class with author support for changelog tests
    public class TestCommitWithAuthor : Commit
    {
        private readonly string _message;
        private readonly string _authorName;

        public TestCommitWithAuthor(string message, string authorName)
        {
            _message = message;
            _authorName = authorName;
        }

        public override string Message => _message;
        public override string MessageShort => _message.Split('\n')[0];
        public override string Sha => Guid.NewGuid().ToString("N").Substring(0, 40);
        public override Signature Author => new Signature(_authorName, $"{_authorName.Replace(" ", "")}@example.com", DateTimeOffset.Now);
    }
}
