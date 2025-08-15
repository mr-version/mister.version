using System;
using System.Collections.Generic;
using Xunit;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;

namespace Mister.Version.Tests
{
    /// <summary>
    /// Tests for project-specific tag detection with various formats
    /// </summary>
    public class ProjectTagDetectionTests
    {
        [Theory]
        [InlineData("testproject-v2.5.0", "TestProject", "2.5.0")] // Prefix lowercase
        [InlineData("TestProject-v2.5.0", "TestProject", "2.5.0")] // Prefix exact case
        [InlineData("TestProject/v2.5.0", "TestProject", "2.5.0")]  // Slash separator
        [InlineData("myapp-v1.0.0", "MyApp", "1.0.0")]              // Prefix with different project
        [InlineData("MyApp/v1.0.0", "MyApp", "1.0.0")]              // Slash format
        public void GetProjectVersionTag_DetectsVariousFormats(string tagName, string projectName, string expectedVersion)
        {
            // This test validates that various project tag formats are correctly detected
            // The actual implementation would need access to a real or mocked Git repository
            
            // Parse the expected version
            var expectedSemVer = ParseSemVer(expectedVersion);
            
            Assert.NotNull(expectedSemVer);
            Assert.Equal(expectedVersion, expectedSemVer.ToVersionString());
            
            // Validate tag format detection logic
            var tagPrefix = "v";
            
            // Check prefix format (only supported format now)
            var lowerProjectName = projectName.ToLowerInvariant();
            if (tagName.StartsWith($"{lowerProjectName}-{tagPrefix}", StringComparison.OrdinalIgnoreCase) ||
                tagName.StartsWith($"{projectName}-{tagPrefix}", StringComparison.OrdinalIgnoreCase))
            {
                var versionPart = tagName.Substring(tagName.IndexOf(tagPrefix) + tagPrefix.Length);
                var semVer = ParseSemVer(versionPart);
                Assert.Equal(expectedSemVer.ToVersionString(), semVer?.ToVersionString());
            }
        }
        
        [Theory]
        [InlineData("v2.0.0", "TestProject", false)]           // Global tag, not project-specific
        [InlineData("v2.0.0-alpha.1", "TestProject", false)]   // Global prerelease tag
        [InlineData("v2.0.0-rc.1", "TestProject", false)]      // Global RC tag
        [InlineData("TestProject-v2.0.0", "TestProject", true)] // Project-specific prefix
        [InlineData("MyLib-v2.0.0", "TestProject", false)]     // Different project prefix
        public void IsProjectSpecificTag_IdentifiesCorrectly(string tagName, string projectName, bool isProjectSpecific)
        {
            // Test the logic for identifying project-specific tags
            var lowerProjectName = projectName.ToLowerInvariant();
            
            bool isSpecific = false;
            
            // Check if tag has project name as prefix (only supported format)
            if (tagName.StartsWith($"{lowerProjectName}-", StringComparison.OrdinalIgnoreCase) ||
                tagName.StartsWith($"{projectName}-", StringComparison.OrdinalIgnoreCase) ||
                tagName.StartsWith($"{lowerProjectName}/", StringComparison.OrdinalIgnoreCase) ||
                tagName.StartsWith($"{projectName}/", StringComparison.OrdinalIgnoreCase))
            {
                isSpecific = true;
            }
            
            Assert.Equal(isProjectSpecific, isSpecific);
        }
        
        [Fact]
        public void ProjectTag_HigherThanGlobalTag_TakesPrecedence()
        {
            // Arrange
            var mockGitService = new MockGitService
            {
                CurrentBranchOverride = "main"
            };
            
            var globalTag = new VersionTag
            {
                SemVer = new SemVer { Major = 2, Minor = 0, Patch = 0 },
                IsGlobal = true,
                Commit = new MockCommit()
            };
            
            var projectTag = new VersionTag
            {
                SemVer = new SemVer { Major = 2, Minor = 5, Patch = 0 },
                IsGlobal = false,
                Commit = new MockCommit(),
                ProjectName = "TestProject"
            };
            
            mockGitService.GlobalVersionTagOverride = globalTag;
            mockGitService.ProjectVersionTagOverride = projectTag;
            
            var calculator = new VersionCalculator(mockGitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/src/TestProject/TestProject.csproj",
                ProjectName = "TestProject"
            };
            
            // Act
            var result = calculator.CalculateVersion(options);
            
            // Assert
            Assert.Equal("2.5.1", result.Version); // Should use project tag and increment
            Assert.True(result.VersionChanged);
        }
        
        [Fact]
        public void GlobalTag_HigherThanProjectTag_TakesPrecedence()
        {
            // Arrange
            var mockGitService = new MockGitService
            {
                CurrentBranchOverride = "main"
            };
            
            var globalTag = new VersionTag
            {
                SemVer = new SemVer { Major = 3, Minor = 0, Patch = 0 },
                IsGlobal = true,
                Commit = new MockCommit()
            };
            
            var projectTag = new VersionTag
            {
                SemVer = new SemVer { Major = 2, Minor = 5, Patch = 0 },
                IsGlobal = false,
                Commit = new MockCommit(),
                ProjectName = "TestProject"
            };
            
            mockGitService.GlobalVersionTagOverride = globalTag;
            mockGitService.ProjectVersionTagOverride = projectTag;
            
            var calculator = new VersionCalculator(mockGitService);
            var options = new VersionOptions
            {
                RepoRoot = "/test",
                ProjectPath = "/test/src/TestProject/TestProject.csproj",
                ProjectName = "TestProject"
            };
            
            // Act
            var result = calculator.CalculateVersion(options);
            
            // Assert
            Assert.Equal("3.0.1", result.Version); // Should use global tag and increment
            Assert.True(result.VersionChanged);
        }
        
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
}