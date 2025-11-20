using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using LibGit2Sharp;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;

namespace Mister.Version.Tests
{
    public class ConventionalCommitAnalyzerTests
    {
        private readonly ConventionalCommitConfig _defaultConfig;
        private readonly ConventionalCommitAnalyzer _analyzer;

        public ConventionalCommitAnalyzerTests()
        {
            _defaultConfig = new ConventionalCommitConfig
            {
                Enabled = true,
                MajorPatterns = new List<string> { "BREAKING CHANGE:", "!:" },
                MinorPatterns = new List<string> { "feat:", "feature:" },
                PatchPatterns = new List<string> { "fix:", "bugfix:", "perf:", "refactor:" },
                IgnorePatterns = new List<string> { "chore:", "docs:", "style:", "test:", "ci:" }
            };
            _analyzer = new ConventionalCommitAnalyzer();
        }

        #region ClassifyCommit Tests

        [Theory]
        [InlineData("feat: add new feature", "feat", null, "add new feature", VersionBumpType.Minor, false)]
        [InlineData("feature: implement dashboard", "feature", null, "implement dashboard", VersionBumpType.Minor, false)]
        [InlineData("fix: resolve bug", "fix", null, "resolve bug", VersionBumpType.Patch, false)]
        [InlineData("bugfix: patch security issue", "bugfix", null, "patch security issue", VersionBumpType.Patch, false)]
        [InlineData("perf: optimize queries", "perf", null, "optimize queries", VersionBumpType.Patch, false)]
        [InlineData("refactor: clean up code", "refactor", null, "clean up code", VersionBumpType.Patch, false)]
        public void ClassifyCommit_StandardPatterns_ClassifiesCorrectly(
            string message, string expectedType, string expectedScope, string expectedDescription,
            VersionBumpType expectedBumpType, bool expectedBreaking)
        {
            // Arrange
            var commit = CreateMockCommit(message);

            // Act
            var result = _analyzer.ClassifyCommit(commit, _defaultConfig);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(expectedType, result.CommitType);
            Assert.Equal(expectedScope, result.Scope);
            Assert.Equal(expectedDescription, result.Description);
            Assert.Equal(expectedBumpType, result.BumpType);
            Assert.Equal(expectedBreaking, result.IsBreakingChange);
            Assert.False(result.ShouldIgnore);
        }

        [Theory]
        [InlineData("feat(core): add new API", "feat", "core", "add new API")]
        [InlineData("fix(ui): resolve layout issue", "fix", "ui", "resolve layout issue")]
        [InlineData("perf(db): optimize indexes", "perf", "db", "optimize indexes")]
        [InlineData("feat(api-client): support pagination", "feat", "api-client", "support pagination")]
        public void ClassifyCommit_WithScope_ParsesScopeCorrectly(
            string message, string expectedType, string expectedScope, string expectedDescription)
        {
            // Arrange
            var commit = CreateMockCommit(message);

            // Act
            var result = _analyzer.ClassifyCommit(commit, _defaultConfig);

            // Assert
            Assert.Equal(expectedType, result.CommitType);
            Assert.Equal(expectedScope, result.Scope);
            Assert.Equal(expectedDescription, result.Description);
        }

        [Theory]
        [InlineData("feat!: remove deprecated API")]
        [InlineData("fix!: breaking bugfix")]
        [InlineData("refactor!: major refactoring")]
        public void ClassifyCommit_WithBreakingIndicator_IdentifiesBreakingChange(string message)
        {
            // Arrange
            var commit = CreateMockCommit(message);

            // Act
            var result = _analyzer.ClassifyCommit(commit, _defaultConfig);

            // Assert
            Assert.True(result.IsBreakingChange);
            Assert.Equal(VersionBumpType.Major, result.BumpType);
            Assert.Contains("Breaking change detected", result.Reason);
        }

        [Fact]
        public void ClassifyCommit_WithBreakingChangeFooter_IdentifiesBreakingChange()
        {
            // Arrange
            var message = @"feat: add new authentication

BREAKING CHANGE: Old OAuth flow has been removed. Please migrate to the new flow.";
            var commit = CreateMockCommit(message);

            // Act
            var result = _analyzer.ClassifyCommit(commit, _defaultConfig);

            // Assert
            Assert.True(result.IsBreakingChange);
            Assert.Equal(VersionBumpType.Major, result.BumpType);
            Assert.Equal("Old OAuth flow has been removed. Please migrate to the new flow.", result.BreakingChangeDescription);
        }

        [Theory]
        [InlineData("chore: update dependencies")]
        [InlineData("docs: update README")]
        [InlineData("style: format code")]
        [InlineData("test: add unit tests")]
        [InlineData("ci: update pipeline")]
        public void ClassifyCommit_IgnorePatterns_MarksAsIgnored(string message)
        {
            // Arrange
            var commit = CreateMockCommit(message);

            // Act
            var result = _analyzer.ClassifyCommit(commit, _defaultConfig);

            // Assert
            Assert.True(result.ShouldIgnore);
            Assert.Equal(VersionBumpType.None, result.BumpType);
        }

        [Fact]
        public void ClassifyCommit_NonConventionalCommit_UsesUnknownType()
        {
            // Arrange
            var message = "Just a regular commit message";
            var commit = CreateMockCommit(message);

            // Act
            var result = _analyzer.ClassifyCommit(commit, _defaultConfig);

            // Assert
            Assert.Equal("unknown", result.CommitType);
            Assert.Equal(message, result.Description);
            Assert.Equal(VersionBumpType.Patch, result.BumpType);
        }

        [Fact]
        public void ClassifyCommit_NullCommit_ReturnsIgnoredClassification()
        {
            // Act
            var result = _analyzer.ClassifyCommit(null, _defaultConfig);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.ShouldIgnore);
            Assert.Equal(VersionBumpType.None, result.BumpType);
            Assert.Equal("Null commit", result.Reason);
        }

        [Theory]
        [InlineData("feat: add feature\n\nBREAKING CHANGE: removed old API")]
        [InlineData("feat!: add feature")]
        public void ClassifyCommit_MultipleBreakingIndicators_StillIdentifiesAsBreaking(string message)
        {
            // Arrange
            var commit = CreateMockCommit(message);

            // Act
            var result = _analyzer.ClassifyCommit(commit, _defaultConfig);

            // Assert
            Assert.True(result.IsBreakingChange);
            Assert.Equal(VersionBumpType.Major, result.BumpType);
        }

        #endregion

        #region AnalyzeBumpType Tests

        [Fact]
        public void AnalyzeBumpType_NoCommits_ReturnsNone()
        {
            // Arrange
            var commits = new List<Commit>();

            // Act
            var result = _analyzer.AnalyzeBumpType(commits, _defaultConfig);

            // Assert
            Assert.Equal(VersionBumpType.None, result);
        }

        [Fact]
        public void AnalyzeBumpType_DisabledConfig_ReturnsPatch()
        {
            // Arrange
            var commits = new List<Commit> { CreateMockCommit("feat: add feature") };
            var disabledConfig = new ConventionalCommitConfig { Enabled = false };

            // Act
            var result = _analyzer.AnalyzeBumpType(commits, disabledConfig);

            // Assert
            Assert.Equal(VersionBumpType.Patch, result);
        }

        [Fact]
        public void AnalyzeBumpType_OnlyPatchCommits_ReturnsPatch()
        {
            // Arrange
            var commits = new List<Commit>
            {
                CreateMockCommit("fix: resolve bug 1"),
                CreateMockCommit("bugfix: resolve bug 2"),
                CreateMockCommit("perf: optimize queries")
            };

            // Act
            var result = _analyzer.AnalyzeBumpType(commits, _defaultConfig);

            // Assert
            Assert.Equal(VersionBumpType.Patch, result);
        }

        [Fact]
        public void AnalyzeBumpType_OnlyMinorCommits_ReturnsMinor()
        {
            // Arrange
            var commits = new List<Commit>
            {
                CreateMockCommit("feat: add feature 1"),
                CreateMockCommit("feature: add feature 2")
            };

            // Act
            var result = _analyzer.AnalyzeBumpType(commits, _defaultConfig);

            // Assert
            Assert.Equal(VersionBumpType.Minor, result);
        }

        [Fact]
        public void AnalyzeBumpType_OnlyMajorCommits_ReturnsMajor()
        {
            // Arrange
            var commits = new List<Commit>
            {
                CreateMockCommit("feat!: breaking feature"),
                CreateMockCommit("fix: resolve bug\n\nBREAKING CHANGE: API changed")
            };

            // Act
            var result = _analyzer.AnalyzeBumpType(commits, _defaultConfig);

            // Assert
            Assert.Equal(VersionBumpType.Major, result);
        }

        [Fact]
        public void AnalyzeBumpType_MixedCommits_ReturnsMajor()
        {
            // Arrange
            var commits = new List<Commit>
            {
                CreateMockCommit("fix: resolve bug"),
                CreateMockCommit("feat: add feature"),
                CreateMockCommit("feat!: breaking change"),
                CreateMockCommit("chore: update deps")
            };

            // Act
            var result = _analyzer.AnalyzeBumpType(commits, _defaultConfig);

            // Assert
            Assert.Equal(VersionBumpType.Major, result);
        }

        [Fact]
        public void AnalyzeBumpType_MinorAndPatchCommits_ReturnsMinor()
        {
            // Arrange
            var commits = new List<Commit>
            {
                CreateMockCommit("fix: resolve bug 1"),
                CreateMockCommit("feat: add feature"),
                CreateMockCommit("fix: resolve bug 2")
            };

            // Act
            var result = _analyzer.AnalyzeBumpType(commits, _defaultConfig);

            // Assert
            Assert.Equal(VersionBumpType.Minor, result);
        }

        [Fact]
        public void AnalyzeBumpType_OnlyIgnoredCommits_ReturnsNone()
        {
            // Arrange
            var commits = new List<Commit>
            {
                CreateMockCommit("chore: update dependencies"),
                CreateMockCommit("docs: update README"),
                CreateMockCommit("style: format code")
            };

            // Act
            var result = _analyzer.AnalyzeBumpType(commits, _defaultConfig);

            // Assert
            Assert.Equal(VersionBumpType.None, result);
        }

        [Fact]
        public void AnalyzeBumpType_MixedWithIgnored_IgnoresProperCommits()
        {
            // Arrange
            var commits = new List<Commit>
            {
                CreateMockCommit("chore: update dependencies"),
                CreateMockCommit("feat: add feature"),
                CreateMockCommit("docs: update README"),
                CreateMockCommit("fix: resolve bug")
            };

            // Act
            var result = _analyzer.AnalyzeBumpType(commits, _defaultConfig);

            // Assert
            Assert.Equal(VersionBumpType.Minor, result);
        }

        #endregion

        #region Custom Configuration Tests

        [Fact]
        public void AnalyzeBumpType_CustomMajorPattern_RecognizesPattern()
        {
            // Arrange
            var customConfig = new ConventionalCommitConfig
            {
                Enabled = true,
                MajorPatterns = new List<string> { "breaking:", "MAJOR:" },
                MinorPatterns = new List<string> { "feat:" },
                PatchPatterns = new List<string> { "fix:" },
                IgnorePatterns = new List<string> { "chore:" }
            };
            var commits = new List<Commit>
            {
                CreateMockCommit("breaking: remove old API")
            };

            // Act
            var result = _analyzer.AnalyzeBumpType(commits, customConfig);

            // Assert
            Assert.Equal(VersionBumpType.Major, result);
        }

        [Fact]
        public void AnalyzeBumpType_CustomMinorPattern_RecognizesPattern()
        {
            // Arrange
            var customConfig = new ConventionalCommitConfig
            {
                Enabled = true,
                MajorPatterns = new List<string> { "BREAKING CHANGE:" },
                MinorPatterns = new List<string> { "feature:", "enhancement:" },
                PatchPatterns = new List<string> { "fix:" },
                IgnorePatterns = new List<string>()
            };
            var commits = new List<Commit>
            {
                CreateMockCommit("enhancement: improve performance")
            };

            // Act
            var result = _analyzer.AnalyzeBumpType(commits, customConfig);

            // Assert
            Assert.Equal(VersionBumpType.Minor, result);
        }

        [Fact]
        public void AnalyzeBumpType_CustomIgnorePattern_IgnoresCommit()
        {
            // Arrange
            var customConfig = new ConventionalCommitConfig
            {
                Enabled = true,
                MajorPatterns = new List<string> { "BREAKING CHANGE:" },
                MinorPatterns = new List<string> { "feat:" },
                PatchPatterns = new List<string> { "fix:" },
                IgnorePatterns = new List<string> { "wip:", "temp:" }
            };
            var commits = new List<Commit>
            {
                CreateMockCommit("wip: work in progress"),
                CreateMockCommit("fix: resolve bug")
            };

            // Act
            var result = _analyzer.AnalyzeBumpType(commits, customConfig);

            // Assert
            Assert.Equal(VersionBumpType.Patch, result);
        }

        #endregion

        #region Edge Cases

        [Theory]
        [InlineData("FEAT: add feature")]
        [InlineData("FIX: resolve bug")]
        [InlineData("Feature: implement dashboard")]
        public void ClassifyCommit_CaseInsensitive_RecognizesPatterns(string message)
        {
            // Arrange
            var commit = CreateMockCommit(message);

            // Act
            var result = _analyzer.ClassifyCommit(commit, _defaultConfig);

            // Assert
            Assert.NotEqual(VersionBumpType.None, result.BumpType);
            Assert.False(result.ShouldIgnore);
        }

        [Theory]
        [InlineData("feat : add feature")]
        [InlineData("fix  :  resolve bug")]
        public void ClassifyCommit_ExtraSpaces_HandlesGracefully(string message)
        {
            // Arrange
            var commit = CreateMockCommit(message);

            // Act
            var result = _analyzer.ClassifyCommit(commit, _defaultConfig);

            // Assert
            Assert.NotNull(result);
            // Should still match the patterns despite extra spaces
        }

        [Fact]
        public void ClassifyCommit_EmptyMessage_HandlesGracefully()
        {
            // Arrange
            var commit = CreateMockCommit("");

            // Act
            var result = _analyzer.ClassifyCommit(commit, _defaultConfig);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("unknown", result.CommitType);
        }

        [Fact]
        public void ClassifyCommit_MultilineMessage_ParsesCorrectly()
        {
            // Arrange
            var message = @"feat: add new authentication

This is a longer description of the feature.
It spans multiple lines.

And has multiple paragraphs.";
            var commit = CreateMockCommit(message);

            // Act
            var result = _analyzer.ClassifyCommit(commit, _defaultConfig);

            // Assert
            Assert.Equal("feat", result.CommitType);
            Assert.Equal("add new authentication", result.Description);
            Assert.Equal(VersionBumpType.Minor, result.BumpType);
        }

        [Fact]
        public void ClassifyCommit_BreakingChangeInMiddleOfBody_Detects()
        {
            // Arrange
            var message = @"fix: resolve authentication bug

This fix resolves an issue with authentication.

BREAKING CHANGE: The authentication flow has changed.
Users must re-authenticate.

Additional notes here.";
            var commit = CreateMockCommit(message);

            // Act
            var result = _analyzer.ClassifyCommit(commit, _defaultConfig);

            // Assert
            Assert.True(result.IsBreakingChange);
            Assert.Equal(VersionBumpType.Major, result.BumpType);
            Assert.Contains("authentication flow has changed", result.BreakingChangeDescription);
        }

        #endregion

        #region Priority and Precedence Tests

        [Fact]
        public void AnalyzeBumpType_MajorTakesPrecedenceOverMinor()
        {
            // Arrange
            var commits = new List<Commit>
            {
                CreateMockCommit("feat: add feature"),
                CreateMockCommit("feat!: breaking feature")
            };

            // Act
            var result = _analyzer.AnalyzeBumpType(commits, _defaultConfig);

            // Assert
            Assert.Equal(VersionBumpType.Major, result);
        }

        [Fact]
        public void AnalyzeBumpType_MajorTakesPrecedenceOverPatch()
        {
            // Arrange
            var commits = new List<Commit>
            {
                CreateMockCommit("fix: resolve bug"),
                CreateMockCommit("fix!: breaking bugfix")
            };

            // Act
            var result = _analyzer.AnalyzeBumpType(commits, _defaultConfig);

            // Assert
            Assert.Equal(VersionBumpType.Major, result);
        }

        [Fact]
        public void AnalyzeBumpType_MinorTakesPrecedenceOverPatch()
        {
            // Arrange
            var commits = new List<Commit>
            {
                CreateMockCommit("fix: resolve bug 1"),
                CreateMockCommit("feat: add feature"),
                CreateMockCommit("fix: resolve bug 2")
            };

            // Act
            var result = _analyzer.AnalyzeBumpType(commits, _defaultConfig);

            // Assert
            Assert.Equal(VersionBumpType.Minor, result);
        }

        #endregion

        #region Helper Methods

        private Commit CreateMockCommit(string message)
        {
            return new TestCommit(message);
        }

        #endregion
    }

    // Mock LibGit2Sharp Commit for testing
    public class TestCommit : Commit
    {
        private readonly string _message;

        public TestCommit(string message)
        {
            _message = message;
        }

        public override string Message => _message;
        public override string MessageShort => _message.Split('\n')[0];
        public override string Sha => Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N").Substring(0, 8);
        public override Signature Author => new Signature("Test Author", "test@example.com", DateTimeOffset.Now);
    }
}
