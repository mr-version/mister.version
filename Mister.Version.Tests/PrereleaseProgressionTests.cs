using System;
using Xunit;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;

namespace Mister.Version.Tests
{
    public class PrereleaseProgressionTests
    {
        [Theory]
        [InlineData("1.0.0-alpha.1", "1.0.0-alpha.2")]
        [InlineData("1.0.0-alpha.5", "1.0.0-alpha.6")]
        [InlineData("1.0.0-alpha.99", "1.0.0-alpha.100")]
        [InlineData("2.5.3-alpha.1", "2.5.3-alpha.2")]
        public void VersionCalculator_MainBranch_IncrementsAlphaPrerelease(string baseVersion, string expectedVersion)
        {
            // Arrange
            var baseSemVer = ParseSemVer(baseVersion);
            var gitService = new MockGitService
            {
                CurrentBranchOverride = "main",
                GlobalVersionTagOverride = new VersionTag
                {
                    SemVer = baseSemVer,
                    IsGlobal = true,
                    Commit = new MockCommit()
                }
            };

            var calculator = new VersionCalculator(gitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/src/MyProject/MyProject.csproj",
                ProjectName = "MyProject"
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.Equal(expectedVersion, result.Version);
            Assert.True(result.VersionChanged);
            Assert.Contains("Incrementing alpha version", result.ChangeReason);
        }

        [Theory]
        [InlineData("1.0.0-beta.1", "1.0.0-beta.2")]
        [InlineData("1.0.0-beta.10", "1.0.0-beta.11")]
        [InlineData("2.1.0-beta.1", "2.1.0-beta.2")]
        [InlineData("0.5.0-beta.25", "0.5.0-beta.26")]
        public void VersionCalculator_MainBranch_IncrementsBetaPrerelease(string baseVersion, string expectedVersion)
        {
            // Arrange
            var baseSemVer = ParseSemVer(baseVersion);
            var gitService = new MockGitService
            {
                CurrentBranchOverride = "main",
                GlobalVersionTagOverride = new VersionTag
                {
                    SemVer = baseSemVer,
                    IsGlobal = true,
                    Commit = new MockCommit()
                }
            };

            var calculator = new VersionCalculator(gitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/src/MyProject/MyProject.csproj",
                ProjectName = "MyProject"
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.Equal(expectedVersion, result.Version);
            Assert.True(result.VersionChanged);
            Assert.Contains("Incrementing beta version", result.ChangeReason);
        }

        [Theory]
        [InlineData("1.0.0-rc.1", "1.0.0-rc.2")]
        [InlineData("1.0.0-rc.5", "1.0.0-rc.6")]
        [InlineData("3.2.1-rc.10", "3.2.1-rc.11")]
        [InlineData("0.9.0-rc.1", "0.9.0-rc.2")]
        public void VersionCalculator_MainBranch_IncrementsRcPrerelease(string baseVersion, string expectedVersion)
        {
            // Arrange
            var baseSemVer = ParseSemVer(baseVersion);
            var gitService = new MockGitService
            {
                CurrentBranchOverride = "main",
                GlobalVersionTagOverride = new VersionTag
                {
                    SemVer = baseSemVer,
                    IsGlobal = true,
                    Commit = new MockCommit()
                }
            };

            var calculator = new VersionCalculator(gitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/src/MyProject/MyProject.csproj",
                ProjectName = "MyProject"
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.Equal(expectedVersion, result.Version);
            Assert.True(result.VersionChanged);
            Assert.Contains("Incrementing rc version", result.ChangeReason);
        }

        [Theory]
        [InlineData("1.0.0-unknown.1", "none", "1.0.1")]
        [InlineData("1.0.0-unknown.1", "alpha", "1.0.1-alpha.1")]
        [InlineData("1.0.0-custom", "beta", "1.0.1-beta.1")]
        [InlineData("1.0.0-dev.123", "rc", "1.0.1-rc.1")]
        public void VersionCalculator_MainBranch_UnknownPrereleaseFormat_IncrementsPatchWithConfiguredPrerelease(
            string baseVersion, string prereleaseType, string expectedVersion)
        {
            // Arrange
            var baseSemVer = ParseSemVer(baseVersion);
            var gitService = new MockGitService
            {
                CurrentBranchOverride = "main",
                GlobalVersionTagOverride = new VersionTag
                {
                    SemVer = baseSemVer,
                    IsGlobal = true,
                    Commit = new MockCommit()
                }
            };

            var calculator = new VersionCalculator(gitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/src/MyProject/MyProject.csproj",
                ProjectName = "MyProject",
                PrereleaseType = prereleaseType
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.Equal(expectedVersion, result.Version);
            Assert.True(result.VersionChanged);
            Assert.Contains("Incrementing patch version", result.ChangeReason);
        }

        [Fact]
        public void VersionCalculator_PrereleaseProgression_AlphaToBeta()
        {
            // Test simulating progression from alpha to beta manually
            // In practice, this would be done by updating tags between builds

            var scenarios = new[]
            {
                ("1.0.0-alpha.5", "1.0.0-alpha.6"),    // Alpha increment
                ("1.0.0-beta.1", "1.0.0-beta.2"),      // Beta increment after manual promotion
                ("1.0.0-rc.1", "1.0.0-rc.2"),          // RC increment after manual promotion
            };

            foreach (var (baseVersion, expectedVersion) in scenarios)
            {
                var baseSemVer = ParseSemVer(baseVersion);
                var gitService = new MockGitService
                {
                    CurrentBranchOverride = "main",
                    GlobalVersionTagOverride = new VersionTag
                    {
                        SemVer = baseSemVer,
                        IsGlobal = true,
                        Commit = new MockCommit()
                    }
                };

                var calculator = new VersionCalculator(gitService);
                var options = new VersionOptions
                {
                    RepoRoot = "/test",
                    ProjectPath = "/test/src/MyProject/MyProject.csproj",
                    ProjectName = "MyProject"
                };

                var result = calculator.CalculateVersion(options);

                Assert.Equal(expectedVersion, result.Version);
                Assert.True(result.VersionChanged);
            }
        }

        [Theory]
        [InlineData("1.0.0-alpha.0", "1.0.0-alpha.1")]   // Zero should increment to 1
        [InlineData("1.0.0-beta.0", "1.0.0-beta.1")]
        [InlineData("1.0.0-rc.0", "1.0.0-rc.1")]
        public void VersionCalculator_PrereleaseProgression_HandlesZeroVersion(string baseVersion, string expectedVersion)
        {
            // Arrange
            var baseSemVer = ParseSemVer(baseVersion);
            var gitService = new MockGitService
            {
                CurrentBranchOverride = "main",
                GlobalVersionTagOverride = new VersionTag
                {
                    SemVer = baseSemVer,
                    IsGlobal = true,
                    Commit = new MockCommit()
                }
            };

            var calculator = new VersionCalculator(gitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/src/MyProject/MyProject.csproj",
                ProjectName = "MyProject"
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.Equal(expectedVersion, result.Version);
            Assert.True(result.VersionChanged);
        }

        [Theory]
        [InlineData("1.0.0-alpha", "1.0.1")]              // Malformed alpha -> patch increment
        [InlineData("1.0.0-beta", "1.0.1")]               // Malformed beta -> patch increment
        [InlineData("1.0.0-rc", "1.0.1")]                 // Malformed rc -> patch increment
        [InlineData("1.0.0-alpha.x", "1.0.1")]            // Non-numeric -> patch increment
        [InlineData("1.0.0-beta.1.2", "1.0.1")]           // Too many parts -> patch increment
        public void VersionCalculator_PrereleaseProgression_MalformedPrerelease_IncrementsPatch(
            string baseVersion, string expectedVersion)
        {
            // Arrange
            var baseSemVer = ParseSemVer(baseVersion);
            var gitService = new MockGitService
            {
                CurrentBranchOverride = "main",
                GlobalVersionTagOverride = new VersionTag
                {
                    SemVer = baseSemVer,
                    IsGlobal = true,
                    Commit = new MockCommit()
                }
            };

            var calculator = new VersionCalculator(gitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/src/MyProject/MyProject.csproj",
                ProjectName = "MyProject",
                PrereleaseType = "none"
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.Equal(expectedVersion, result.Version);
            Assert.True(result.VersionChanged);
            Assert.Contains("Incrementing patch version", result.ChangeReason);
        }

        [Theory]
        [InlineData("release/2.0.0", 1, "2.0.0")]
        [InlineData("release/2.0.0", 5, "2.0.0")]
        [InlineData("release-3.1.0", 1, "3.1.0")]
        [InlineData("v4.2.0", 10, "4.2.0")]
        public void VersionCalculator_ReleaseBranch_ProducesFinalVersions(
            string branchName, int commitHeight, string expectedVersion)
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
                },
                CommitHeightOverride = commitHeight
            };

            var calculator = new VersionCalculator(gitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/src/MyProject/MyProject.csproj",
                ProjectName = "MyProject"
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.Equal(expectedVersion, result.Version);
            Assert.True(result.VersionChanged);
            Assert.DoesNotContain("rc", result.Version);
        }

        // Helper method to parse semantic version for testing
        private SemVer ParseSemVer(string version)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                version,
                @"^(\d+)\.(\d+)\.(\d+)(?:-([^+]+))?(?:\+(.+))?$");

            if (!match.Success)
                throw new ArgumentException($"Invalid version format: {version}");

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