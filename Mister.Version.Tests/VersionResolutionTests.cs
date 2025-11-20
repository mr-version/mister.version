using System;
using System.Collections.Generic;
using Xunit;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;
using LibGit2Sharp;

namespace Mister.Version.Tests
{
    /// <summary>
    /// Tests for version resolution logic, specifically the DetermineBaseVersion method
    /// and global vs project tag priority scenarios
    /// </summary>
    public class VersionResolutionTests
    {
        private VersionCalculator CreateVersionCalculator(MockGitService gitService = null)
        {
            return new VersionCalculator(gitService ?? new MockGitService());
        }

        private MockGitService CreateMockGitService(
            VersionTag globalTag = null,
            VersionTag projectTag = null,
            string currentBranch = "main",
            bool hasChanges = true)
        {
            return new MockGitService
            {
                CurrentBranchOverride = currentBranch,
                GlobalVersionTagOverride = globalTag,
                ProjectVersionTagOverride = projectTag,
                HasChangesOverride = hasChanges,
                CommitHeightOverride = 1
            };
        }

        [Fact]
        public void DetermineBaseVersion_NoTagsPresent_UsesConfigBaseVersion()
        {
            // Arrange
            var gitService = CreateMockGitService(globalTag: null, projectTag: null);
            var calculator = CreateVersionCalculator(gitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/src/MyProject/MyProject.csproj",
                ProjectName = "MyProject",
                BaseVersion = "2.0.0"
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.Equal("2.0.0", result.Version);
            Assert.True(result.VersionChanged);
            Assert.Contains("First change with new base version from configuration", result.ChangeReason);
        }

        [Fact]
        public void DetermineBaseVersion_OnlyProjectTag_UsesProjectTag()
        {
            // Arrange
            var projectTag = new VersionTag
            {
                SemVer = new SemVer { Major = 1, Minor = 5, Patch = 3 },
                IsGlobal = false,
                Commit = new MockCommit()
            };

            var gitService = CreateMockGitService(globalTag: null, projectTag: projectTag);
            var calculator = CreateVersionCalculator(gitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/src/MyProject/MyProject.csproj",
                ProjectName = "MyProject",
                BaseVersion = "1.0.0"
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.Equal("1.5.4", result.Version); // Should increment patch from project tag
            Assert.True(result.VersionChanged);
        }

        [Fact]
        public void DetermineBaseVersion_OnlyGlobalTag_UsesGlobalTag()
        {
            // Arrange
            var globalTag = new VersionTag
            {
                SemVer = new SemVer { Major = 2, Minor = 1, Patch = 0 },
                IsGlobal = true,
                Commit = new MockCommit()
            };

            var gitService = CreateMockGitService(globalTag: globalTag, projectTag: null);
            var calculator = CreateVersionCalculator(gitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/src/MyProject/MyProject.csproj",
                ProjectName = "MyProject",
                BaseVersion = "1.0.0"
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.Equal("2.1.1", result.Version); // Should increment patch from global tag
            Assert.True(result.VersionChanged);
        }

        [Theory]
        [InlineData("2.0.0", "1.5.3", "2.0.1")] // Global major higher -> use global
        [InlineData("1.6.0", "1.5.3", "1.6.1")] // Global minor higher -> use global
        [InlineData("1.5.0", "1.5.3", "1.5.4")] // Project patch higher -> use project
        [InlineData("1.4.0", "1.5.3", "1.5.4")] // Project minor higher -> use project
        [InlineData("1.0.0", "1.5.3", "1.5.4")] // Project major.minor higher -> use project
        public void DetermineBaseVersion_BothTagsPresent_UsesHigherMajorMinorVersion(
            string globalVersion, string projectVersion, string expectedResult)
        {
            // Arrange
            var globalSemVer = ParseSemVer(globalVersion);
            var projectSemVer = ParseSemVer(projectVersion);

            var globalTag = new VersionTag
            {
                SemVer = globalSemVer,
                IsGlobal = true,
                Commit = new MockCommit()
            };

            var projectTag = new VersionTag
            {
                SemVer = projectSemVer,
                IsGlobal = false,
                Commit = new MockCommit()
            };

            var gitService = CreateMockGitService(globalTag: globalTag, projectTag: projectTag);
            var calculator = CreateVersionCalculator(gitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/src/MyProject/MyProject.csproj",
                ProjectName = "MyProject"
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.Equal(expectedResult, result.Version);
            Assert.True(result.VersionChanged);
        }

        [Fact]
        public void DetermineBaseVersion_GlobalVersionHigher_LogsNewReleaseCycle()
        {
            // Arrange
            var logMessages = new List<(string level, string message)>();
            
            var globalTag = new VersionTag
            {
                SemVer = new SemVer { Major = 2, Minor = 0, Patch = 0 },
                IsGlobal = true,
                Commit = new MockCommit()
            };

            var projectTag = new VersionTag
            {
                SemVer = new SemVer { Major = 1, Minor = 5, Patch = 3 },
                IsGlobal = false,
                Commit = new MockCommit()
            };

            var gitService = CreateMockGitService(globalTag: globalTag, projectTag: projectTag);
            var calculator = new VersionCalculator(gitService, logger: (level, message) => logMessages.Add((level, message)));
            
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/src/MyProject/MyProject.csproj",
                ProjectName = "MyProject",
                Debug = true
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.Equal("2.0.1", result.Version);
            Assert.Contains(logMessages, log => 
                log.level == "Debug" && 
                log.message.Contains("Using global version 2.0.0 over project version 1.5.3 (new release cycle)"));
        }

        [Fact]
        public void DetermineBaseVersion_ProjectVersionHigher_UsesProjectTag()
        {
            // Arrange
            var globalTag = new VersionTag
            {
                SemVer = new SemVer { Major = 1, Minor = 2, Patch = 0 },
                IsGlobal = true,
                Commit = new MockCommit()
            };

            var projectTag = new VersionTag
            {
                SemVer = new SemVer { Major = 1, Minor = 5, Patch = 3 },
                IsGlobal = false,
                Commit = new MockCommit()
            };

            var gitService = CreateMockGitService(globalTag: globalTag, projectTag: projectTag);
            var calculator = CreateVersionCalculator(gitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/src/MyProject/MyProject.csproj",
                ProjectName = "MyProject"
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.Equal("1.5.4", result.Version); // Uses project tag + increment
            Assert.True(result.VersionChanged);
        }

        [Theory]
        [InlineData("8.3.0", "8.2.2", true)]   // New minor release cycle
        [InlineData("9.0.0", "8.2.2", true)]   // New major release cycle
        [InlineData("8.2.0", "8.2.2", false)]  // Same major.minor, project higher
        [InlineData("8.1.0", "8.2.2", false)]  // Lower minor version
        [InlineData("7.5.0", "8.2.2", false)]  // Lower major version
        public void DetermineBaseVersion_NewReleaseCycleDetection(
            string globalVersion, string projectVersion, bool shouldUseGlobal)
        {
            // Arrange
            var globalSemVer = ParseSemVer(globalVersion);
            var projectSemVer = ParseSemVer(projectVersion);

            var globalTag = new VersionTag
            {
                SemVer = globalSemVer,
                IsGlobal = true,
                Commit = new MockCommit()
            };

            var projectTag = new VersionTag
            {
                SemVer = projectSemVer,
                IsGlobal = false,
                Commit = new MockCommit()
            };

            var gitService = CreateMockGitService(globalTag: globalTag, projectTag: projectTag);
            var calculator = CreateVersionCalculator(gitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/src/MyProject/MyProject.csproj",
                ProjectName = "MyProject"
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            var expectedBaseVersion = shouldUseGlobal ? globalSemVer : projectSemVer;
            var expectedResult = new SemVer 
            { 
                Major = expectedBaseVersion.Major, 
                Minor = expectedBaseVersion.Minor, 
                Patch = expectedBaseVersion.Patch + 1 
            };

            Assert.Equal(expectedResult.ToVersionString(), result.Version);
        }

        [Fact]
        public void DetermineBaseVersion_ConfigBaseVersionAsGlobalFallback_WorksCorrectly()
        {
            // Arrange - No global git tag, but config baseVersion should act as global
            var projectTag = new VersionTag
            {
                SemVer = new SemVer { Major = 1, Minor = 5, Patch = 3 },
                IsGlobal = false,
                Commit = new MockCommit()
            };

            // Mock git service that returns a "default" global tag based on config
            var gitService = new MockGitService
            {
                CurrentBranchOverride = "main",
                ProjectVersionTagOverride = projectTag,
                HasChangesOverride = true,
                CommitHeightOverride = 1
            };

            // Override GetGlobalVersionTag to simulate config fallback
            gitService.GetGlobalVersionTagOverride = (branchType, options) => new VersionTag
            {
                SemVer = ParseSemVer(options.BaseVersion ?? "8.3.0"),
                IsGlobal = true,
                Commit = null
            };

            var calculator = CreateVersionCalculator(gitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/src/MyProject/MyProject.csproj",
                ProjectName = "MyProject",
                BaseVersion = "8.3.0" // Config baseVersion higher than project tag
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.Equal("8.3.0", result.Version); // Should use config baseVersion, not increment since it's treated as initial
            Assert.True(result.VersionChanged);
        }

        [Theory]
        [InlineData("none", "2.0.1")]
        [InlineData("alpha", "2.0.1-alpha.1")]
        [InlineData("beta", "2.0.1-beta.1")]
        [InlineData("rc", "2.0.1-rc.1")]
        public void DetermineBaseVersion_NewReleaseCycleWithPrerelease_WorksCorrectly(
            string prereleaseType, string expectedVersion)
        {
            // Arrange
            var globalTag = new VersionTag
            {
                SemVer = new SemVer { Major = 2, Minor = 0, Patch = 0 },
                IsGlobal = true,
                Commit = new MockCommit()
            };

            var projectTag = new VersionTag
            {
                SemVer = new SemVer { Major = 1, Minor = 5, Patch = 3 },
                IsGlobal = false,
                Commit = new MockCommit()
            };

            var gitService = CreateMockGitService(globalTag: globalTag, projectTag: projectTag);
            var calculator = CreateVersionCalculator(gitService);
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
        }

        private SemVer ParseSemVer(string version)
        {
            var parts = version.Split('.');
            return new SemVer
            {
                Major = int.Parse(parts[0]),
                Minor = int.Parse(parts[1]),
                Patch = int.Parse(parts[2])
            };
        }
    }
}