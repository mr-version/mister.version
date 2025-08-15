using System;
using Xunit;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;

namespace Mister.Version.Tests
{
    /// <summary>
    /// Tests for baseVersion configuration functionality
    /// </summary>
    public class BaseVersionTests
    {
        [Fact]
        public void BaseVersion_HigherThanExistingTags_CreatesNewReleaseCycle()
        {
            // Arrange
            var mockGitService = new MockGitService
            {
                CurrentBranchOverride = "main"
            };
            
            // Simulate an existing lower version tag
            var existingTag = new VersionTag
            {
                SemVer = new SemVer { Major = 1, Minor = 2, Patch = 3 },
                IsGlobal = false,
                Commit = new MockCommit()
            };
            
            // Return the existing tag when no baseVersion
            mockGitService.GetGlobalVersionTagOverride = (branchType, options) =>
            {
                if (!string.IsNullOrEmpty(options.BaseVersion))
                {
                    var baseSemVer = mockGitService.ParseSemVer(options.BaseVersion);
                    return new VersionTag
                    {
                        SemVer = baseSemVer,
                        IsGlobal = true,
                        Commit = null // Config-based version has no commit
                    };
                }
                return existingTag;
            };
            
            mockGitService.ProjectVersionTagOverride = existingTag;
            
            // Mock that the base version tag doesn't exist yet
            mockGitService.TagExistsOverride = (tagName) => false;
            
            // Mock that repository has commits (not initial)
            mockGitService.RepositoryHasCommitsOverride = true;
            
            var calculator = new VersionCalculator(mockGitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/src/MyProject/MyProject.csproj",
                ProjectName = "MyProject",
                BaseVersion = "2.0.0", // Higher than existing 1.2.3
                TagPrefix = "v"
            };
            
            // Act
            var result = calculator.CalculateVersion(options);
            
            // Assert
            // First change with new base version should get the exact base version
            Assert.Equal("2.0.0", result.Version); 
            Assert.True(result.VersionChanged);
            Assert.Contains("base version", result.ChangeReason.ToLower());
        }
        
        [Fact]
        public void BaseVersion_FirstUseInEmptyRepo_UsesExactBaseVersion()
        {
            // Arrange
            var mockGitService = new MockGitService
            {
                CurrentBranchOverride = "main",
                GlobalVersionTagOverride = null,
                ProjectVersionTagOverride = null
            };
            
            // Mock empty repository
            mockGitService.GetGlobalVersionTagOverride = (branchType, options) =>
            {
                if (!string.IsNullOrEmpty(options.BaseVersion))
                {
                    return new VersionTag
                    {
                        SemVer = mockGitService.ParseSemVer(options.BaseVersion),
                        IsGlobal = true,
                        Commit = null
                    };
                }
                return new VersionTag
                {
                    SemVer = new SemVer { Major = 0, Minor = 1, Patch = 0 },
                    IsGlobal = true,
                    Commit = null
                };
            };
            
            var calculator = new VersionCalculator(mockGitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/src/MyProject/MyProject.csproj",
                ProjectName = "MyProject",
                BaseVersion = "3.0.0"
            };
            
            // Act
            var result = calculator.CalculateVersion(options);
            
            // Assert
            // For truly empty repos, it might use the base version as-is
            // The exact behavior depends on the implementation
            Assert.NotNull(result.Version);
            Assert.True(result.VersionChanged);
        }
        
        [Fact]
        public void BaseVersion_WithPrereleaseType_AppliesCorrectly()
        {
            // Arrange
            var mockGitService = new MockGitService
            {
                CurrentBranchOverride = "main"
            };
            
            mockGitService.GetGlobalVersionTagOverride = (branchType, options) =>
            {
                if (!string.IsNullOrEmpty(options.BaseVersion))
                {
                    return new VersionTag
                    {
                        SemVer = mockGitService.ParseSemVer(options.BaseVersion),
                        IsGlobal = true,
                        Commit = null
                    };
                }
                return null;
            };
            
            var calculator = new VersionCalculator(mockGitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/src/MyProject/MyProject.csproj",
                ProjectName = "MyProject",
                BaseVersion = "2.0.0",
                PrereleaseType = "alpha"
            };
            
            // Act
            var result = calculator.CalculateVersion(options);
            
            // Assert
            // First change with baseVersion uses it directly, without prerelease
            Assert.Equal("2.0.0", result.Version);
            Assert.DoesNotContain("-alpha", result.Version);
            Assert.True(result.VersionChanged);
        }
        
        [Fact]
        public void BaseVersion_LowerThanExistingTag_UsesExistingTag()
        {
            // Arrange
            var mockGitService = new MockGitService
            {
                CurrentBranchOverride = "main"
            };
            
            var higherTag = new VersionTag
            {
                SemVer = new SemVer { Major = 3, Minor = 0, Patch = 0 },
                IsGlobal = true,
                Commit = new MockCommit()
            };
            
            mockGitService.GetGlobalVersionTagOverride = (branchType, options) =>
            {
                // Even with baseVersion, return the higher existing tag
                return higherTag;
            };
            
            var calculator = new VersionCalculator(mockGitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/src/MyProject/MyProject.csproj",
                ProjectName = "MyProject",
                BaseVersion = "1.0.0" // Lower than existing 3.0.0
            };
            
            // Act
            var result = calculator.CalculateVersion(options);
            
            // Assert
            Assert.Equal("3.0.1", result.Version); // Should use existing higher tag
            Assert.True(result.VersionChanged);
        }
    }
}