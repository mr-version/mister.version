using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;

namespace Mister.Version.Tests
{
    public class EdgeCaseTests
    {
        [Fact]
        public void VersionCalculator_NullGitService_ThrowsException()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentNullException>(() => new VersionCalculator(null));
        }

        [Fact]
        public void VersionCalculator_NullOptions_ThrowsException()
        {
            // Arrange
            var gitService = new MockGitService();
            var calculator = new VersionCalculator(gitService);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => calculator.CalculateVersion(null));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void VersionCalculator_InvalidProjectPath_HandlesGracefully(string projectPath)
        {
            // Arrange
            var gitService = new MockGitService();
            var calculator = new VersionCalculator(gitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = projectPath,
                ProjectName = "TestProject"
            };

            // Act & Assert
            // Should handle gracefully without throwing
            var result = calculator.CalculateVersion(options);
            Assert.NotNull(result);
        }

        [Theory]
        [InlineData("invalid-version")]
        [InlineData("1.2.3.4.5")]
        [InlineData("v1.2.3")] // With prefix when not expected
        [InlineData("1.a.3")]
        [InlineData(".1.2")]
        [InlineData("1..3")]
        public void VersionCalculator_InvalidForcedVersion_HandlesGracefully(string invalidVersion)
        {
            // Arrange
            var gitService = new MockGitService();
            var calculator = new VersionCalculator(gitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/project.csproj",
                ProjectName = "TestProject",
                ForceVersion = invalidVersion
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(invalidVersion, result.Version); // Should still use the forced version
            Assert.True(result.VersionChanged);
            Assert.Equal("Forced version", result.ChangeReason);
        }

        [Theory]
        [InlineData("feature/release/1.2.3")] // Nested path that looks like release
        [InlineData("feature/v1.2.3-fixes")]  // Feature branch with version-like name
        [InlineData("hotfix/release-notes")]  // Contains "release" but not a release branch
        [InlineData("user/dev/feature")]      // Contains "dev" but not a dev branch
        public void GitService_AmbiguousBranchNames_ClassifiedCorrectly(string branchName)
        {
            // Arrange
            var gitService = new MockGitService();

            // Act
            var branchType = gitService.GetBranchType(branchName);

            // Assert
            Assert.Equal(BranchType.Feature, branchType); // All should be classified as feature branches
        }

        [Theory]
        [InlineData("1.0.0-alpha.1.2")]     // Multiple dots in prerelease
        [InlineData("1.0.0-alpha+beta")]     // Plus sign in prerelease (should be in build metadata)
        [InlineData("1.0.0-")]               // Empty prerelease
        [InlineData("1.0.0+")]               // Empty build metadata
        [InlineData("1.0.0-alpha.01")]       // Leading zero in numeric identifier
        public void ParseSemVer_UnusualButValidVersions_ParsedCorrectly(string version)
        {
            // Arrange
            var gitService = new MockGitService();

            // Act
            var result = gitService.ParseSemVer(version);

            // Assert
            // Some of these might be invalid according to strict semver, but we handle them gracefully
            if (version.EndsWith("-") || version.EndsWith("+"))
            {
                Assert.Null(result); // Invalid versions
            }
            else
            {
                Assert.NotNull(result);
                Assert.Equal(1, result.Major);
                Assert.Equal(0, result.Minor);
                Assert.Equal(0, result.Patch);
            }
        }

        [Fact]
        public void VersionCalculator_VeryLongBranchName_HandlesCorrectly()
        {
            // Arrange
            var veryLongBranchName = "feature/" + new string('a', 200); // 200+ character branch name
            var gitService = new MockGitService 
            { 
                CurrentBranchOverride = veryLongBranchName,
                GlobalVersionTagOverride = new VersionTag
                {
                    SemVer = new SemVer { Major = 1, Minor = 0, Patch = 0 },
                    IsGlobal = true,
                    Commit = new MockCommit()
                }
            };
            var calculator = new VersionCalculator(gitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/project.csproj",
                ProjectName = "TestProject"
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.NotNull(result);
            Assert.Contains(new string('a', 50), result.Version); // Should truncate or handle long names
        }

        [Theory]
        [InlineData("feature/special-chars-!@#$%")]
        [InlineData("feature/unicode-测试-分支")]
        [InlineData("feature/spaces in name")]
        [InlineData("feature/(parentheses)")]
        [InlineData("feature/[brackets]")]
        public void VersionCalculator_SpecialCharactersInBranchName_HandlesCorrectly(string branchName)
        {
            // Arrange
            var gitService = new MockGitService 
            { 
                CurrentBranchOverride = branchName,
                GlobalVersionTagOverride = new VersionTag
                {
                    SemVer = new SemVer { Major = 1, Minor = 0, Patch = 0 },
                    IsGlobal = true,
                    Commit = new MockCommit()
                }
            };
            var calculator = new VersionCalculator(gitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/project.csproj",
                ProjectName = "TestProject"
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.NotNull(result);
            Assert.DoesNotContain("!", result.Version);
            Assert.DoesNotContain("@", result.Version);
            Assert.DoesNotContain("#", result.Version);
            Assert.DoesNotContain(" ", result.Version);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(int.MaxValue)]
        public void VersionCalculator_ExtremeCommitHeights_HandlesCorrectly(int commitHeight)
        {
            // Arrange
            var gitService = new MockGitService
            {
                CurrentBranchOverride = "dev",
                CommitHeightOverride = commitHeight
            };
            var calculator = new VersionCalculator(gitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/project.csproj",
                ProjectName = "TestProject"
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.NotNull(result);
            if (commitHeight >= 0)
            {
                Assert.Contains($"dev.{commitHeight}", result.Version);
            }
        }

        [Fact]
        public void VersionCalculator_CircularDependencies_HandlesGracefully()
        {
            // Arrange
            var gitService = new MockGitService
            {
                GlobalVersionTagOverride = new VersionTag
                {
                    SemVer = new SemVer { Major = 1, Minor = 0, Patch = 0 },
                    IsGlobal = true,
                    Commit = new MockCommit()
                }
            };
            var calculator = new VersionCalculator(gitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/ProjectA/ProjectA.csproj",
                ProjectName = "ProjectA",
                Dependencies = new List<string>
                {
                    "ProjectB",
                    "ProjectC",
                    "ProjectA" // Circular reference
                }
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.NotNull(result);
            // Should not cause infinite loop or stack overflow
        }

        [Theory]
        [InlineData("1.0.0-alpha.999999999")] // Very large prerelease number
        [InlineData("999.999.999")]           // Maximum reasonable version
        [InlineData("0.0.0")]                 // Minimum version
        public void VersionCalculator_ExtremeSemVerValues_HandlesCorrectly(string version)
        {
            // Arrange
            var semVer = ParseSemVer(version);
            var gitService = new MockGitService
            {
                GlobalVersionTagOverride = new VersionTag
                {
                    SemVer = semVer,
                    IsGlobal = true,
                    Commit = new MockCommit()
                }
            };
            var calculator = new VersionCalculator(gitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/project.csproj",
                ProjectName = "TestProject"
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.SemVer.Major >= 0);
            Assert.True(result.SemVer.Minor >= 0);
            Assert.True(result.SemVer.Patch >= 0);
        }

        [Fact]
        public void VersionCalculator_NullTagCommit_HandlesAsInitialRepository()
        {
            // Arrange
            var gitService = new MockGitService
            {
                GlobalVersionTagOverride = new VersionTag
                {
                    SemVer = new SemVer { Major = 0, Minor = 1, Patch = 0 },
                    IsGlobal = true,
                    Commit = null // Null commit indicates initial repository
                }
            };
            var calculator = new VersionCalculator(gitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/project.csproj",
                ProjectName = "TestProject"
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.VersionChanged);
            Assert.Contains("Initial repository", result.ChangeReason);
        }

        [Theory]
        [InlineData("invalid-type")]
        [InlineData("ALPHA")] // Wrong case
        [InlineData("prerelease")]
        [InlineData("")]
        public void VersionOptions_InvalidPrereleaseType_HandlesGracefully(string prereleaseType)
        {
            // Arrange
            var gitService = new MockGitService
            {
                GlobalVersionTagOverride = new VersionTag
                {
                    SemVer = new SemVer { Major = 1, Minor = 0, Patch = 0 },
                    IsGlobal = true,
                    Commit = new MockCommit()
                }
            };
            var calculator = new VersionCalculator(gitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/project.csproj",
                ProjectName = "TestProject",
                PrereleaseType = prereleaseType
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.NotNull(result);
            // Should handle invalid prerelease types without crashing
        }

        // Helper method
        private SemVer ParseSemVer(string version)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                version,
                @"^(\d+)\.(\d+)\.(\d+)(?:-([^+]+))?(?:\+(.+))?$");

            if (!match.Success)
                return new SemVer { Major = 0, Minor = 1, Patch = 0 };

            return new SemVer
            {
                Major = int.Parse(match.Groups[1].Value),
                Minor = int.Parse(match.Groups[2].Value),
                Patch = int.Parse(match.Groups[3].Value),
                PreRelease = match.Groups[4].Success ? match.Groups[4].Value : null,
                BuildMetadata = match.Groups[5].Success ? match.Groups[5].Value : null
            };
        }
    }
}