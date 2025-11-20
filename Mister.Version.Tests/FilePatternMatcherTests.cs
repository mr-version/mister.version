using System;
using System.Collections.Generic;
using Xunit;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;

namespace Mister.Version.Tests
{
    public class FilePatternMatcherTests
    {
        private readonly FilePatternMatcher _matcher;

        public FilePatternMatcherTests()
        {
            _matcher = new FilePatternMatcher();
        }

        #region Pattern Matching Tests

        [Theory]
        [InlineData("README.md", "*.md", true)]
        [InlineData("docs/guide.md", "*.md", false)]
        [InlineData("docs/guide.md", "**/*.md", true)]
        [InlineData("src/docs/api.md", "**/*.md", true)]
        [InlineData("README.txt", "*.md", false)]
        public void Matches_SimplePatterns_MatchesCorrectly(string filePath, string pattern, bool expected)
        {
            // Act
            var result = _matcher.Matches(filePath, pattern);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("docs/README.md", "**/docs/**", true)]
        [InlineData("src/docs/api.md", "**/docs/**", true)]
        [InlineData("project/docs/guide.md", "**/docs/**", true)]
        [InlineData("documentation/guide.md", "**/docs/**", false)]
        [InlineData("docs.md", "**/docs/**", false)]
        public void Matches_RecursiveDirectoryPattern_MatchesCorrectly(string filePath, string pattern, bool expected)
        {
            // Act
            var result = _matcher.Matches(filePath, pattern);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("src/PublicApi/IService.cs", "**/PublicApi/**", true)]
        [InlineData("lib/PublicApi/IRepository.cs", "**/PublicApi/**", true)]
        [InlineData("PublicApi/IController.cs", "**/PublicApi/**", true)]
        [InlineData("src/PublicApiClient/Service.cs", "**/PublicApi/**", false)]
        public void Matches_PublicApiPattern_MatchesCorrectly(string filePath, string pattern, bool expected)
        {
            // Act
            var result = _matcher.Matches(filePath, pattern);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("test.txt", "test.???", true)]
        [InlineData("test.md", "test.??", true)]  // Fixed: .md is 2 chars after the dot, should match ??
        [InlineData("test.cs", "test.??", true)]
        [InlineData("test.json", "test.????", true)]  // Fixed: .json is 4 chars after the dot, should match ????
        public void Matches_QuestionMarkWildcard_MatchesCorrectly(string filePath, string pattern, bool expected)
        {
            // Act
            var result = _matcher.Matches(filePath, pattern);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("src/Features/Auth.cs", "**/Features/**", true)]
        [InlineData("lib/Internal/Helper.cs", "**/Internal/**", true)]
        [InlineData("docs/guide.md", "**/*.md", true)]
        [InlineData(".editorconfig", "**/.editorconfig", true)]
        [InlineData("src/.editorconfig", "**/.editorconfig", true)]
        public void Matches_RealWorldPatterns_MatchesCorrectly(string filePath, string pattern, bool expected)
        {
            // Act
            var result = _matcher.Matches(filePath, pattern);

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Matches_WithBackslashes_NormalizesAndMatches()
        {
            // Arrange
            var filePath = "src\\docs\\README.md";
            var pattern = "**/docs/**";

            // Act
            var result = _matcher.Matches(filePath, pattern);

            // Assert
            Assert.True(result);
        }

        #endregion

        #region ClassifyChanges Tests

        [Fact]
        public void ClassifyChanges_NoFiles_ReturnsIgnoredClassification()
        {
            // Arrange
            var files = new List<string>();
            var config = new ChangeDetectionConfig { Enabled = true };

            // Act
            var result = _matcher.ClassifyChanges(files, config);

            // Assert
            Assert.True(result.ShouldIgnore);
            Assert.Equal(0, result.TotalFiles);
            Assert.Equal("No files changed", result.Reason);
        }

        [Fact]
        public void ClassifyChanges_OnlyIgnoredFiles_ClassifiesAsIgnored()
        {
            // Arrange
            var files = new List<string> { "README.md", "docs/guide.md", "CHANGELOG.md" };
            var config = new ChangeDetectionConfig
            {
                Enabled = true,
                IgnorePatterns = new List<string> { "**/*.md" }
            };

            // Act
            var result = _matcher.ClassifyChanges(files, config);

            // Assert
            Assert.Equal(3, result.TotalFiles);
            Assert.Equal(3, result.IgnoredFiles.Count);
            Assert.Empty(result.MajorFiles);
            Assert.Empty(result.MinorFiles);
            Assert.Empty(result.PatchFiles);
        }

        [Fact]
        public void ClassifyChanges_MajorPatternFiles_ClassifiesAsMajor()
        {
            // Arrange
            var files = new List<string> { "src/PublicApi/IService.cs", "src/PublicApi/IRepository.cs" };
            var config = new ChangeDetectionConfig
            {
                Enabled = true,
                MajorPatterns = new List<string> { "**/PublicApi/**" }
            };

            // Act
            var result = _matcher.ClassifyChanges(files, config);

            // Assert
            Assert.Equal(2, result.TotalFiles);
            Assert.Equal(2, result.MajorFiles.Count);
            Assert.Empty(result.MinorFiles);
            Assert.Empty(result.PatchFiles);
            Assert.Empty(result.IgnoredFiles);
        }

        [Fact]
        public void ClassifyChanges_MinorPatternFiles_ClassifiesAsMinor()
        {
            // Arrange
            var files = new List<string> { "src/Features/Auth.cs", "src/Features/Payment.cs" };
            var config = new ChangeDetectionConfig
            {
                Enabled = true,
                MinorPatterns = new List<string> { "**/Features/**" }
            };

            // Act
            var result = _matcher.ClassifyChanges(files, config);

            // Assert
            Assert.Equal(2, result.TotalFiles);
            Assert.Empty(result.MajorFiles);
            Assert.Equal(2, result.MinorFiles.Count);
            Assert.Empty(result.PatchFiles);
        }

        [Fact]
        public void ClassifyChanges_PatchPatternFiles_ClassifiesAsPatch()
        {
            // Arrange
            var files = new List<string> { "src/Internal/Helper.cs", "src/Internal/Util.cs" };
            var config = new ChangeDetectionConfig
            {
                Enabled = true,
                PatchPatterns = new List<string> { "**/Internal/**" }
            };

            // Act
            var result = _matcher.ClassifyChanges(files, config);

            // Assert
            Assert.Equal(2, result.TotalFiles);
            Assert.Empty(result.MajorFiles);
            Assert.Empty(result.MinorFiles);
            Assert.Equal(2, result.PatchFiles.Count);
        }

        [Fact]
        public void ClassifyChanges_MixedFiles_ClassifiesCorrectly()
        {
            // Arrange
            var files = new List<string>
            {
                "README.md",                    // Ignored
                "src/PublicApi/IService.cs",    // Major
                "src/Features/Auth.cs",         // Minor
                "src/Internal/Helper.cs",       // Patch
                "src/Core/Service.cs"           // Unclassified
            };
            var config = new ChangeDetectionConfig
            {
                Enabled = true,
                IgnorePatterns = new List<string> { "**/*.md" },
                MajorPatterns = new List<string> { "**/PublicApi/**" },
                MinorPatterns = new List<string> { "**/Features/**" },
                PatchPatterns = new List<string> { "**/Internal/**" }
            };

            // Act
            var result = _matcher.ClassifyChanges(files, config);

            // Assert
            Assert.Equal(5, result.TotalFiles);
            Assert.Single(result.IgnoredFiles);
            Assert.Single(result.MajorFiles);
            Assert.Single(result.MinorFiles);
            Assert.Single(result.PatchFiles);
            Assert.Single(result.UnclassifiedFiles);
        }

        [Fact]
        public void ClassifyChanges_IgnorePatternsFirst_IgnoresBeforeOtherPatterns()
        {
            // Arrange
            var files = new List<string> { "src/PublicApi/README.md" };
            var config = new ChangeDetectionConfig
            {
                Enabled = true,
                IgnorePatterns = new List<string> { "**/*.md" },
                MajorPatterns = new List<string> { "**/PublicApi/**" }
            };

            // Act
            var result = _matcher.ClassifyChanges(files, config);

            // Assert
            Assert.Single(result.IgnoredFiles);
            Assert.Empty(result.MajorFiles);
        }

        #endregion

        #region DetermineBumpType Tests

        [Fact]
        public void DetermineBumpType_AllIgnored_ReturnsNone()
        {
            // Arrange
            var classification = new ChangeClassification
            {
                TotalFiles = 2,
                IgnoredFiles = new List<string> { "README.md", "docs/guide.md" }
            };
            var config = new ChangeDetectionConfig { Enabled = true };

            // Act
            var result = _matcher.DetermineBumpType(classification, config);

            // Assert
            Assert.Equal(VersionBumpType.None, result);
            Assert.True(classification.ShouldIgnore);
        }

        [Fact]
        public void DetermineBumpType_MajorFiles_ReturnsMajor()
        {
            // Arrange
            var classification = new ChangeClassification
            {
                TotalFiles = 1,
                MajorFiles = new List<string> { "src/PublicApi/IService.cs" }
            };
            var config = new ChangeDetectionConfig { Enabled = true };

            // Act
            var result = _matcher.DetermineBumpType(classification, config);

            // Assert
            Assert.Equal(VersionBumpType.Major, result);
            Assert.False(classification.ShouldIgnore);
        }

        [Fact]
        public void DetermineBumpType_MinorFiles_ReturnsMinor()
        {
            // Arrange
            var classification = new ChangeClassification
            {
                TotalFiles = 1,
                MinorFiles = new List<string> { "src/Features/Auth.cs" }
            };
            var config = new ChangeDetectionConfig { Enabled = true };

            // Act
            var result = _matcher.DetermineBumpType(classification, config);

            // Assert
            Assert.Equal(VersionBumpType.Minor, result);
        }

        [Fact]
        public void DetermineBumpType_PatchFiles_ReturnsPatch()
        {
            // Arrange
            var classification = new ChangeClassification
            {
                TotalFiles = 1,
                PatchFiles = new List<string> { "src/Internal/Helper.cs" }
            };
            var config = new ChangeDetectionConfig { Enabled = true };

            // Act
            var result = _matcher.DetermineBumpType(classification, config);

            // Assert
            Assert.Equal(VersionBumpType.Patch, result);
        }

        [Fact]
        public void DetermineBumpType_MajorTakesPrecedence_ReturnsMajor()
        {
            // Arrange
            var classification = new ChangeClassification
            {
                TotalFiles = 3,
                MajorFiles = new List<string> { "src/PublicApi/IService.cs" },
                MinorFiles = new List<string> { "src/Features/Auth.cs" },
                PatchFiles = new List<string> { "src/Internal/Helper.cs" }
            };
            var config = new ChangeDetectionConfig { Enabled = true };

            // Act
            var result = _matcher.DetermineBumpType(classification, config);

            // Assert
            Assert.Equal(VersionBumpType.Major, result);
        }

        [Fact]
        public void DetermineBumpType_MinorTakesPrecedenceOverPatch_ReturnsMinor()
        {
            // Arrange
            var classification = new ChangeClassification
            {
                TotalFiles = 2,
                MinorFiles = new List<string> { "src/Features/Auth.cs" },
                PatchFiles = new List<string> { "src/Internal/Helper.cs" }
            };
            var config = new ChangeDetectionConfig { Enabled = true };

            // Act
            var result = _matcher.DetermineBumpType(classification, config);

            // Assert
            Assert.Equal(VersionBumpType.Minor, result);
        }

        [Fact]
        public void DetermineBumpType_UnclassifiedFiles_DefaultsToPatch()
        {
            // Arrange
            var classification = new ChangeClassification
            {
                TotalFiles = 1,
                UnclassifiedFiles = new List<string> { "src/Core/Service.cs" }
            };
            var config = new ChangeDetectionConfig { Enabled = true };

            // Act
            var result = _matcher.DetermineBumpType(classification, config);

            // Assert
            Assert.Equal(VersionBumpType.Patch, result);
        }

        [Fact]
        public void DetermineBumpType_SourceOnlyMode_IgnoresAllIgnoredFiles()
        {
            // Arrange
            var classification = new ChangeClassification
            {
                TotalFiles = 2,
                IgnoredFiles = new List<string> { "README.md", "docs/guide.md" }
            };
            var config = new ChangeDetectionConfig
            {
                Enabled = true,
                SourceOnlyMode = true
            };

            // Act
            var result = _matcher.DetermineBumpType(classification, config);

            // Assert
            Assert.Equal(VersionBumpType.None, result);
            Assert.True(classification.ShouldIgnore);
            Assert.Contains("source-only mode", classification.Reason);
        }

        [Fact]
        public void DetermineBumpType_SourceOnlyModeWithMixedFiles_IgnoresOnlyIgnored()
        {
            // Arrange
            var classification = new ChangeClassification
            {
                TotalFiles = 2,
                IgnoredFiles = new List<string> { "README.md" },
                PatchFiles = new List<string> { "src/Internal/Helper.cs" }
            };
            var config = new ChangeDetectionConfig
            {
                Enabled = true,
                SourceOnlyMode = true
            };

            // Act
            var result = _matcher.DetermineBumpType(classification, config);

            // Assert
            Assert.Equal(VersionBumpType.Patch, result);
            Assert.False(classification.ShouldIgnore);
        }

        [Fact]
        public void DetermineBumpType_MinimumBumpType_EnforcesMinimum()
        {
            // Arrange
            var classification = new ChangeClassification
            {
                TotalFiles = 1,
                PatchFiles = new List<string> { "src/Internal/Helper.cs" }
            };
            var config = new ChangeDetectionConfig
            {
                Enabled = true,
                MinimumBumpType = VersionBumpType.Minor
            };

            // Act
            var result = _matcher.DetermineBumpType(classification, config);

            // Assert
            Assert.Equal(VersionBumpType.Minor, result);
        }

        #endregion
    }
}
