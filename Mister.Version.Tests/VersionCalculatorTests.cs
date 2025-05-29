using System;
using System.Collections.Generic;
using Xunit;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;
using LibGit2Sharp;

namespace Mister.Version.Tests
{
    public class VersionCalculatorTests
    {
        private MockGitService CreateMockGitService(
            string currentBranch = "main",
            VersionTag globalTag = null,
            VersionTag projectTag = null,
            bool hasChanges = true,
            int commitHeight = 1)
        {
            return new MockGitService
            {
                CurrentBranchOverride = currentBranch,
                GlobalVersionTagOverride = globalTag,
                ProjectVersionTagOverride = projectTag,
                HasChangesOverride = hasChanges,
                CommitHeightOverride = commitHeight
            };
        }

        [Fact]
        public void CalculateVersion_InitialRepository_ReturnsBaseVersion()
        {
            // Arrange
            var gitService = CreateMockGitService(
                globalTag: new VersionTag
                {
                    SemVer = new SemVer { Major = 0, Minor = 1, Patch = 0 },
                    IsGlobal = true,
                    Commit = null // Initial repository
                });

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
            Assert.Equal("0.1.0", result.Version);
            Assert.True(result.VersionChanged);
            Assert.Contains("Initial repository", result.ChangeReason);
        }

        [Theory]
        [InlineData("none", "1.0.1", "Main branch: Incrementing patch version")]
        [InlineData("alpha", "1.0.1-alpha.1", "Main branch: Incrementing patch version with alpha prerelease")]
        [InlineData("beta", "1.0.1-beta.1", "Main branch: Incrementing patch version with beta prerelease")]
        [InlineData("rc", "1.0.1-rc.1", "Main branch: Incrementing patch version with rc prerelease")]
        public void CalculateVersion_MainBranch_WithPrereleaseType(string prereleaseType, string expectedVersion, string expectedReason)
        {
            // Arrange
            var gitService = CreateMockGitService(
                currentBranch: "main",
                globalTag: new VersionTag
                {
                    SemVer = new SemVer { Major = 1, Minor = 0, Patch = 0 },
                    IsGlobal = true,
                    Commit = new MockCommit()
                });

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
            Assert.Equal(expectedReason, result.ChangeReason);
        }

        [Theory]
        [InlineData("1.0.0-alpha.1", "1.0.0-alpha.2", "Main branch: Incrementing alpha version")]
        [InlineData("1.0.0-beta.3", "1.0.0-beta.4", "Main branch: Incrementing beta version")]
        [InlineData("1.0.0-rc.10", "1.0.0-rc.11", "Main branch: Incrementing rc version")]
        public void CalculateVersion_MainBranch_IncrementsPrereleaseNumber(string baseVersion, string expectedVersion, string expectedReason)
        {
            // Arrange
            var baseSemVer = ParseSemVer(baseVersion);
            var gitService = CreateMockGitService(
                currentBranch: "main",
                globalTag: new VersionTag
                {
                    SemVer = baseSemVer,
                    IsGlobal = true,
                    Commit = new MockCommit()
                });

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
            Assert.Equal(expectedReason, result.ChangeReason);
        }

        [Theory]
        [InlineData(1, "1.0.1-dev.1")]
        [InlineData(5, "1.0.1-dev.5")]
        [InlineData(42, "1.0.1-dev.42")]
        public void CalculateVersion_DevBranch_IncrementsMinorWithCommitHeight(int commitHeight, string expectedVersion)
        {
            // Arrange
            var gitService = CreateMockGitService(
                currentBranch: "dev",
                globalTag: new VersionTag
                {
                    SemVer = new SemVer { Major = 1, Minor = 0, Patch = 0 },
                    IsGlobal = true,
                    Commit = new MockCommit()
                },
                commitHeight: commitHeight);

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
            Assert.Equal(commitHeight, result.CommitHeight);
            Assert.Contains($"dev.{commitHeight}", result.ChangeReason);
        }

        [Theory]
        [InlineData("release/2.0.0", "2.0.0-rc.1")]
        [InlineData("release-3.1.0", "3.1.0-rc.1")]
        [InlineData("v4.5.2", "4.5.2-rc.1")] // Release branches can have patch versions
        public void CalculateVersion_ReleaseBranch_ExtractsVersionFromBranchName(string branchName, string expectedVersion)
        {
            // Arrange
            var gitService = CreateMockGitService(
                currentBranch: branchName,
                globalTag: new VersionTag
                {
                    SemVer = new SemVer { Major = 1, Minor = 0, Patch = 0 },
                    IsGlobal = true,
                    Commit = new MockCommit()
                });

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
            Assert.Contains("Release branch", result.ChangeReason);
        }

        [Theory]
        [InlineData("feature/new-feature", 1, "1.0.1-new-feature.1")]
        [InlineData("feature/cool_feature", 3, "1.0.1-cool-feature.3")]
        [InlineData("bugfix/fix-issue", 2, "1.0.1-fix-issue.2")]
        [InlineData("hotfix/urgent", 5, "1.0.1-urgent.5")]
        public void CalculateVersion_FeatureBranch_IncrementsMinorWithFeatureName(string branchName, int commitHeight, string expectedVersion)
        {
            // Arrange
            var gitService = CreateMockGitService(
                currentBranch: branchName,
                globalTag: new VersionTag
                {
                    SemVer = new SemVer { Major = 1, Minor = 0, Patch = 0 },
                    IsGlobal = true,
                    Commit = new MockCommit()
                },
                commitHeight: commitHeight);

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
            Assert.Equal(commitHeight, result.CommitHeight);
            Assert.Contains("Feature branch", result.ChangeReason);
        }

        [Fact]
        public void CalculateVersion_NoChanges_ReturnsBaseVersion()
        {
            // Arrange
            var gitService = CreateMockGitService(
                hasChanges: false,
                globalTag: new VersionTag
                {
                    SemVer = new SemVer { Major = 1, Minor = 2, Patch = 3 },
                    IsGlobal = true,
                    Commit = new MockCommit()
                });

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
            Assert.Equal("1.2.3", result.Version);
            Assert.False(result.VersionChanged);
            Assert.Contains("No changes detected", result.ChangeReason);
        }

        [Fact]
        public void CalculateVersion_ProjectSpecificTag_TakesPrecedenceOverGlobal()
        {
            // Arrange
            var globalTag = new VersionTag
            {
                SemVer = new SemVer { Major = 1, Minor = 0, Patch = 0 },
                IsGlobal = true,
                Commit = new MockCommit()
            };

            var projectTag = new VersionTag
            {
                SemVer = new SemVer { Major = 2, Minor = 5, Patch = 0 },
                IsGlobal = false,
                Commit = new MockCommit(),
                Tag = new MockTag("MyProject/v2.5.0")
            };

            var gitService = CreateMockGitService(
                globalTag: globalTag,
                projectTag: projectTag);

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
            Assert.Equal("2.5.1", result.Version); // Should increment from project tag, not global
            Assert.True(result.VersionChanged);
        }

        [Fact]
        public void CalculateVersion_ForcedVersion_ReturnsSpecifiedVersion()
        {
            // Arrange
            var gitService = CreateMockGitService();
            var calculator = new VersionCalculator(gitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/src/MyProject/MyProject.csproj",
                ProjectName = "MyProject",
                ForceVersion = "3.2.1-custom"
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.Equal("3.2.1-custom", result.Version);
            Assert.True(result.VersionChanged);
            Assert.Equal("Forced version", result.ChangeReason);
        }

        [Fact]
        public void CalculateVersion_TestProject_SkipsVersioning()
        {
            // Arrange
            var gitService = CreateMockGitService();
            var calculator = new VersionCalculator(gitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/src/MyProject.Tests/MyProject.Tests.csproj",
                ProjectName = "MyProject.Tests",
                IsTestProject = true,
                SkipTestProjects = true
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.Equal("1.0.0", result.Version);
            Assert.False(result.VersionChanged);
            Assert.Contains("Test project", result.ChangeReason);
        }

        [Fact]
        public void CalculateVersion_NonPackableProject_SkipsVersioning()
        {
            // Arrange
            var gitService = CreateMockGitService();
            var calculator = new VersionCalculator(gitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/src/MyTool/MyTool.csproj",
                ProjectName = "MyTool",
                IsPackable = false,
                SkipNonPackableProjects = true
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.Equal("1.0.0", result.Version);
            Assert.False(result.VersionChanged);
            Assert.Contains("Non-packable project", result.ChangeReason);
        }

        // Helper method to parse semantic version
        private SemVer ParseSemVer(string version)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                version, 
                @"^(\d+)\.(\d+)\.(\d+)(?:-([^+]+))?(?:\+(.+))?$");
            
            if (!match.Success)
                return null;

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


    // Mock LibGit2Sharp types
    public class MockCommit : Commit
    {
        public override Signature Author => new Signature("Test Author", "test@example.com", DateTimeOffset.Now);
        public override string MessageShort => "Test commit";
        public override string Sha => "abcdef1234567890abcdef1234567890abcdef12";
    }

    public class MockTag : Tag
    {
        private readonly string _name;
        
        public MockTag(string name)
        {
            _name = name;
        }

        public override string FriendlyName => _name;
    }
}