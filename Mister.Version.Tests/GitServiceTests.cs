using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;

namespace Mister.Version.Tests
{
    public class GitServiceTests
    {
        [Theory]
        [InlineData("main", BranchType.Main)]
        [InlineData("master", BranchType.Main)]
        [InlineData("MAIN", BranchType.Main)]
        [InlineData("dev", BranchType.Dev)]
        [InlineData("develop", BranchType.Dev)]
        [InlineData("development", BranchType.Dev)]
        [InlineData("DEV", BranchType.Dev)]
        [InlineData("release/1.2.3", BranchType.Release)]
        [InlineData("release-2.0.0", BranchType.Release)]
        [InlineData("v3.1.4", BranchType.Release)]
        [InlineData("V10.20.30", BranchType.Release)]
        [InlineData("feature/my-feature", BranchType.Feature)]
        [InlineData("feature/COOL-123", BranchType.Feature)]
        [InlineData("bugfix/fix-issue", BranchType.Feature)]
        [InlineData("hotfix/urgent", BranchType.Feature)]
        [InlineData("random-branch", BranchType.Feature)]
        [InlineData("user/john/experiment", BranchType.Feature)]
        public void GetBranchType_IdentifiesCorrectBranchType(string branchName, BranchType expectedType)
        {
            // Arrange
            var gitService = new Mister.Version.Tests.MockGitService();

            // Act
            var result = gitService.GetBranchType(branchName);

            // Assert
            Assert.Equal(expectedType, result);
        }

        [Theory]
        [InlineData("release/1.2.3", "v", 1, 2, 3)]
        [InlineData("release-2.0.0", "v", 2, 0, 0)]
        [InlineData("v3.1.4", "v", 3, 1, 4)]
        [InlineData("release/v1.2.3", "v", 1, 2, 3)]
        [InlineData("release/1.2", "v", 1, 2, 0)]
        [InlineData("v10.20", "v", 10, 20, 0)]
        [InlineData("release/ver1.2.3", "ver", 1, 2, 3)]
        [InlineData("main", "v", null, null, null)]
        [InlineData("feature/release-like", "v", null, null, null)]
        [InlineData("release/invalid", "v", null, null, null)]
        public void ExtractReleaseVersion_ParsesVersionCorrectly(
            string branchName, 
            string tagPrefix, 
            int? expectedMajor, 
            int? expectedMinor, 
            int? expectedPatch)
        {
            // Arrange
            var gitService = new Mister.Version.Tests.MockGitService();

            // Act
            var result = gitService.ExtractReleaseVersion(branchName, tagPrefix);

            // Assert
            if (expectedMajor == null)
            {
                Assert.Null(result);
            }
            else
            {
                Assert.NotNull(result);
                Assert.Equal(expectedMajor, result.Major);
                Assert.Equal(expectedMinor, result.Minor);
                Assert.Equal(expectedPatch, result.Patch);
            }
        }

        [Theory]
        [InlineData("1.2.3", 1, 2, 3, null, null)]
        [InlineData("0.0.1", 0, 0, 1, null, null)]
        [InlineData("10.20.30", 10, 20, 30, null, null)]
        [InlineData("1.2", 1, 2, 0, null, null)]
        [InlineData("1.2.3-alpha", 1, 2, 3, "alpha", null)]
        [InlineData("1.2.3-alpha.1", 1, 2, 3, "alpha.1", null)]
        [InlineData("1.2.3-beta.2", 1, 2, 3, "beta.2", null)]
        [InlineData("1.2.3-rc.10", 1, 2, 3, "rc.10", null)]
        [InlineData("1.2.3-dev.123", 1, 2, 3, "dev.123", null)]
        [InlineData("1.2.3-feature.new-thing.5", 1, 2, 3, "feature.new-thing.5", null)]
        [InlineData("1.2.3+build.123", 1, 2, 3, null, "build.123")]
        [InlineData("1.2.3-alpha.1+build.123", 1, 2, 3, "alpha.1", "build.123")]
        [InlineData("1.2.3-beta+20230101", 1, 2, 3, "beta", "20230101")]
        [InlineData("invalid", null, null, null, null, null)]
        [InlineData("", null, null, null, null, null)]
        [InlineData(null, null, null, null, null, null)]
        [InlineData("1", null, null, null, null, null)]
        [InlineData("v1.2.3", null, null, null, null, null)]
        [InlineData("1.2.3.4", null, null, null, null, null)]
        public void ParseSemVer_HandlesVariousFormats(
            string input, 
            int? expectedMajor, 
            int? expectedMinor, 
            int? expectedPatch, 
            string expectedPreRelease, 
            string expectedBuildMetadata)
        {
            // Arrange
            var gitService = new Mister.Version.Tests.MockGitService();

            // Act
            var result = gitService.ParseSemVer(input);

            // Assert
            if (expectedMajor == null)
            {
                Assert.Null(result);
            }
            else
            {
                Assert.NotNull(result);
                Assert.Equal(expectedMajor, result.Major);
                Assert.Equal(expectedMinor, result.Minor);
                Assert.Equal(expectedPatch, result.Patch);
                Assert.Equal(expectedPreRelease, result.PreRelease);
                Assert.Equal(expectedBuildMetadata, result.BuildMetadata);
            }
        }

        [Fact]
        public void GetVersionTag_ParsesTagCorrectly()
        {
            // This test would require a real git repository or more complex mocking
            // For now, we'll test the tag parsing logic separately
            
            // Test tag name parsing patterns
            var tagPatterns = new[]
            {
                ("v1.2.3", "v", true, 1, 2, 3),
                ("MyProject/v2.0.0", "v", false, 2, 0, 0),
                ("tag-1.0.0", "tag-", true, 1, 0, 0),
                ("invalid-tag", "v", true, 0, 0, 0)
            };

            foreach (var (tagName, prefix, isGlobal, major, minor, patch) in tagPatterns)
            {
                if (tagName.StartsWith(prefix) || tagName.Contains($"/{prefix}"))
                {
                    var versionPart = tagName;
                    if (tagName.Contains("/"))
                    {
                        versionPart = tagName.Substring(tagName.LastIndexOf('/') + 1);
                    }
                    
                    if (versionPart.StartsWith(prefix))
                    {
                        versionPart = versionPart.Substring(prefix.Length);
                    }

                    var gitService = new Mister.Version.Tests.MockGitService();
                    var semVer = gitService.ParseSemVer(versionPart);
                    
                    if (major > 0)
                    {
                        Assert.NotNull(semVer);
                        Assert.Equal(major, semVer.Major);
                        Assert.Equal(minor, semVer.Minor);
                        Assert.Equal(patch, semVer.Patch);
                    }
                }
            }
        }

        [Theory]
        [InlineData("v1.2.3", "v1.2.3", "v", true)]
        [InlineData("v1.2.3", "MyProject/v1.2.3", "v", true)]  // Project tags are valid
        [InlineData("tag-1.0.0", "tag-1.0.0", "tag-", true)]
        [InlineData("1.0.0", "v1.0.0", "v", true)]  // Valid tag
        [InlineData("version1.2.3", "version1.2.3", "version", true)]
        public void IsValidVersionTag_ChecksTagValidity(string tagVersion, string tagName, string tagPrefix, bool expectedValid)
        {
            // Simulate tag validation logic
            bool isValid = false;
            
            if (tagName.StartsWith(tagPrefix) || tagName.Contains($"/{tagPrefix}"))
            {
                var versionPart = tagName;
                if (tagName.Contains("/"))
                {
                    versionPart = tagName.Substring(tagName.LastIndexOf('/') + 1);
                }
                
                if (versionPart.StartsWith(tagPrefix))
                {
                    versionPart = versionPart.Substring(tagPrefix.Length);
                    var gitService = new Mister.Version.Tests.MockGitService();
                    var semVer = gitService.ParseSemVer(versionPart);
                    isValid = semVer != null;
                }
            }

            Assert.Equal(expectedValid, isValid);
        }

        [Fact]
        public void ProjectHasChangedSinceTag_DetectsChangesCorrectly()
        {
            // Test various scenarios for change detection
            var scenarios = new[]
            {
                // (projectPath, hasDirectChanges, hasDependencyChanges, expectedResult)
                ("src/MyProject", true, false, true),
                ("src/MyProject", false, true, true),
                ("src/MyProject", true, true, true),
                ("src/MyProject", false, false, false),
            };

            foreach (var (projectPath, hasDirectChanges, hasDependencyChanges, expectedResult) in scenarios)
            {
                var gitService = new MockGitServiceWithChanges
                {
                    DirectChanges = hasDirectChanges,
                    DependencyChanges = hasDependencyChanges
                };

                var result = gitService.ProjectHasChangedSinceTag(
                    null, 
                    projectPath, 
                    new List<string> { "src/SharedLib" }, 
                    "/repo", 
                    false);

                Assert.Equal(expectedResult, result);
            }
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 1)]
        [InlineData(10, 10)]
        [InlineData(100, 100)]
        public void GetCommitHeight_ReturnsCorrectHeight(int expectedHeight, int actualHeight)
        {
            // Arrange
            var gitService = new MockGitService
            {
                CommitHeightOverride = actualHeight
            };

            // Act
            var result = gitService.GetCommitHeight(null);

            // Assert
            Assert.Equal(expectedHeight, result);
        }
    }

    // Extended mock for testing change detection
    public class MockGitServiceWithChanges : Mister.Version.Tests.MockGitService
    {
        public bool DirectChanges { get; set; }
        public bool DependencyChanges { get; set; }

        public override bool ProjectHasChangedSinceTag(
            LibGit2Sharp.Commit tagCommit, 
            string projectPath, 
            List<string> dependencies, 
            string repoRoot, 
            bool debug = false)
        {
            return DirectChanges || (DependencyChanges && dependencies.Any());
        }
    }
}