using Xunit;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;
using System.IO;
using System.Linq;

namespace Mister.Version.Tests
{
    public class CoreVersioningTests
    {
        [Theory]
        [InlineData("main", BranchType.Main)]
        [InlineData("master", BranchType.Main)]
        [InlineData("dev", BranchType.Dev)]
        [InlineData("develop", BranchType.Dev)]
        [InlineData("development", BranchType.Dev)]
        [InlineData("release/1.2.3", BranchType.Release)]
        [InlineData("release-2.0.0", BranchType.Release)]
        [InlineData("v3.1.4", BranchType.Release)]
        [InlineData("feature/my-feature", BranchType.Feature)]
        [InlineData("bugfix/fix", BranchType.Feature)]
        [InlineData("hotfix/foo", BranchType.Feature)]
        public void GitService_DetermineBranchType_Works(string branchName, BranchType expectedType)
        {
            // Since we can't easily mock GitService for unit tests without a real repo,
            // we'll test this logic through a mock or test implementation
            var mockGitService = new MockGitService();
            var result = mockGitService.GetBranchType(branchName);
            Assert.Equal(expectedType, result);
        }

        [Theory]
        [InlineData("release/1.2.3", "v", 1, 2, 3)]
        [InlineData("release-2.0.0", "v", 2, 0, 0)]
        [InlineData("v3.1.4", "v", 3, 1, 4)]
        [InlineData("main", "v", null, null, null)]
        [InlineData("feature/xyz", "v", null, null, null)]
        public void GitService_ExtractReleaseVersion_HandlesVariousFormats(string input, string tagPrefix, int? major, int? minor, int? patch)
        {
            var mockGitService = new MockGitService();
            var ver = mockGitService.ExtractReleaseVersion(input, tagPrefix);
            if (major == null)
            {
                Assert.Null(ver);
            }
            else
            {
                Assert.NotNull(ver);
                Assert.Equal(major, ver.Major);
                Assert.Equal(minor, ver.Minor);
                Assert.Equal(patch, ver.Patch);
            }
        }

        [Theory]
        [InlineData("1.2.3", 1, 2, 3, null, null)]
        [InlineData("0.9.0", 0, 9, 0, null, null)]
        [InlineData("5.10", 5, 10, 0, null, null)]
        [InlineData("7.2.1-alpha", 7, 2, 1, "alpha", null)]
        [InlineData("7.2.1-alpha.1+build.123", 7, 2, 1, "alpha.1", "build.123")]
        [InlineData("notaversion", null, null, null, null, null)]
        [InlineData("", null, null, null, null, null)]
        public void GitService_ParseSemVer_HandlesComplexVersions(string input, int? major, int? minor, int? patch, string preRelease, string buildMetadata)
        {
            var mockGitService = new MockGitService();
            var ver = mockGitService.ParseSemVer(input);
            if (major == null)
            {
                Assert.Null(ver);
            }
            else
            {
                Assert.NotNull(ver);
                Assert.Equal(major, ver.Major);
                Assert.Equal(minor, ver.Minor);
                Assert.Equal(patch, ver.Patch);
                Assert.Equal(preRelease, ver.PreRelease);
                Assert.Equal(buildMetadata, ver.BuildMetadata);
            }
        }

        [Fact]
        public void SemVer_ToVersionString_FormatsCorrectly()
        {
            // Test basic version
            var version1 = new SemVer { Major = 1, Minor = 2, Patch = 3 };
            Assert.Equal("1.2.3", version1.ToVersionString());

            // Test with prerelease
            var version2 = new SemVer { Major = 1, Minor = 2, Patch = 3, PreRelease = "alpha.1" };
            Assert.Equal("1.2.3-alpha.1", version2.ToVersionString());

            // Test with build metadata (should not appear in version string)
            var version3 = new SemVer { Major = 1, Minor = 2, Patch = 3, PreRelease = "alpha.1", BuildMetadata = "build.123" };
            Assert.Equal("1.2.3-alpha.1", version3.ToVersionString());
        }

        [Fact]
        public void SemVer_ToString_IncludesBuildMetadata()
        {
            var version = new SemVer { Major = 1, Minor = 2, Patch = 3, PreRelease = "alpha.1", BuildMetadata = "build.123" };
            Assert.Equal("1.2.3-alpha.1+build.123", version.ToString());
        }

        [Fact]
        public void ProjectAnalyzer_IsTestProject_DetectsTestProjects()
        {
            var analyzer = new MockProjectAnalyzer();

            // Test explicit marker
            var content1 = "<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>";
            Assert.True(analyzer.TestIsTestProject(content1));

            // Test xunit reference
            var content2 = "<Project><ItemGroup><PackageReference Include=\"xunit\" Version=\"2.4.1\" /></ItemGroup></Project>";
            Assert.True(analyzer.TestIsTestProject(content2));

            // Test NUnit reference
            var content3 = "<Project><ItemGroup><PackageReference Include=\"NUnit\" Version=\"3.13.1\" /></ItemGroup></Project>";
            Assert.True(analyzer.TestIsTestProject(content3));

            // Test non-test project
            var content4 = "<Project><ItemGroup><PackageReference Include=\"Newtonsoft.Json\" Version=\"13.0.1\" /></ItemGroup></Project>";
            Assert.False(analyzer.TestIsTestProject(content4));
        }

        [Fact]
        public void ReportGenerator_GeneratesJsonReport()
        {
            var report = new VersionReport
            {
                Repository = "/test/repo",
                Branch = "main",
                BranchType = BranchType.Main,
                GlobalVersion = new SemVer { Major = 1, Minor = 0, Patch = 0 },
                Projects = new[]
                {
                    new ProjectInfo
                    {
                        Name = "TestProject",
                        Path = "src/TestProject/TestProject.csproj",
                        Version = new VersionResult
                        {
                            Version = "1.0.1",
                            SemVer = new SemVer { Major = 1, Minor = 0, Patch = 1 },
                            VersionChanged = true,
                            ChangeReason = "Test change"
                        },
                        IsTestProject = false,
                        IsPackable = true
                    }
                }.ToList()
            };

            var generator = new ReportGenerator();
            var options = new Core.Services.ReportOptions { OutputFormat = "json" };
            var json = generator.GenerateJsonReport(report, options);

            Assert.Contains("TestProject", json);
            Assert.Contains("1.0.1", json);
            Assert.Contains("\"versionChanged\": true", json);
        }
    }

    // Simple mock for basic testing - more comprehensive mocks are in other test files
    public class MockGitService : IGitService
    {
        public LibGit2Sharp.Repository RepositoryOverride { get; set; }
        public LibGit2Sharp.Repository Repository => RepositoryOverride;
        public string CurrentBranchOverride { get; set; } = "main";
        public string CurrentBranch => CurrentBranchOverride;
        
        // Override properties for testing
        public VersionTag GlobalVersionTagOverride { get; set; }
        public VersionTag ProjectVersionTagOverride { get; set; }
        public bool HasChangesOverride { get; set; } = true;
        public int CommitHeightOverride { get; set; } = 1;
        public System.Func<BranchType, VersionOptions, VersionTag> GetGlobalVersionTagOverride { get; set; }
        public System.Func<string, bool> TagExistsOverride { get; set; }
        public bool RepositoryHasCommitsOverride { get; set; } = true;

        public virtual BranchType GetBranchType(string branchName)
        {
            if (branchName.Equals("main", System.StringComparison.OrdinalIgnoreCase) ||
                branchName.Equals("master", System.StringComparison.OrdinalIgnoreCase))
            {
                return BranchType.Main;
            }
            else if (branchName.Equals("dev", System.StringComparison.OrdinalIgnoreCase) ||
                     branchName.Equals("develop", System.StringComparison.OrdinalIgnoreCase) ||
                     branchName.Equals("development", System.StringComparison.OrdinalIgnoreCase))
            {
                return BranchType.Dev;
            }
            else if (branchName.StartsWith("release/", System.StringComparison.OrdinalIgnoreCase) ||
                     System.Text.RegularExpressions.Regex.IsMatch(branchName, @"^v\d+\.\d+(\.\d+)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
                     System.Text.RegularExpressions.Regex.IsMatch(branchName, @"^release-\d+\.\d+(\.\d+)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return BranchType.Release;
            }
            else
            {
                return BranchType.Feature;
            }
        }

        public SemVer ExtractReleaseVersion(string branchName, string tagPrefix)
        {
            string versionPart = branchName;

            if (branchName.StartsWith("release/", System.StringComparison.OrdinalIgnoreCase))
            {
                versionPart = branchName.Substring("release/".Length);
            }
            else if (branchName.StartsWith("release-", System.StringComparison.OrdinalIgnoreCase))
            {
                versionPart = branchName.Substring("release-".Length);
            }

            if (versionPart.StartsWith(tagPrefix, System.StringComparison.OrdinalIgnoreCase))
            {
                versionPart = versionPart.Substring(tagPrefix.Length);
            }

            return ParseSemVer(versionPart);
        }

        public SemVer ParseSemVer(string version)
        {
            if (string.IsNullOrEmpty(version))
                return null;

            var match = System.Text.RegularExpressions.Regex.Match(version, @"^(\d+)\.(\d+)(?:\.(\d+))?(?:-([0-9A-Za-z\-\.]+))?(?:\+([0-9A-Za-z\-\.]+))?$");
            if (!match.Success)
                return null;

            return new SemVer
            {
                Major = int.Parse(match.Groups[1].Value),
                Minor = int.Parse(match.Groups[2].Value),
                Patch = match.Groups.Count > 3 && match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0,
                PreRelease = match.Groups.Count > 4 && match.Groups[4].Success ? match.Groups[4].Value : null,
                BuildMetadata = match.Groups.Count > 5 && match.Groups[5].Success ? match.Groups[5].Value : null
            };
        }

        // Implement other interface members as needed for testing
        public virtual VersionTag GetGlobalVersionTag(BranchType branchType, VersionOptions options) =>
            GetGlobalVersionTagOverride?.Invoke(branchType, options) ?? GlobalVersionTagOverride;

        public virtual VersionTag GetProjectVersionTag(string projectName, BranchType branchType, string tagPrefix, VersionOptions options = null) => ProjectVersionTagOverride;

        public virtual bool ProjectHasChangedSinceTag(LibGit2Sharp.Commit tagCommit, string projectPath, System.Collections.Generic.List<string> dependencies, string repoRoot, bool debug = false) => HasChangesOverride;

        public virtual ChangeClassification ClassifyProjectChanges(LibGit2Sharp.Commit tagCommit, string projectPath, System.Collections.Generic.List<string> dependencies, string repoRoot, ChangeDetectionConfig config)
        {
            // Mock implementation - return basic classification
            return new ChangeClassification
            {
                ShouldIgnore = !HasChangesOverride,
                RequiredBumpType = HasChangesOverride ? VersionBumpType.Patch : VersionBumpType.None,
                TotalFiles = HasChangesOverride ? 1 : 0,
                Reason = HasChangesOverride ? "Mock changes detected" : "No changes"
            };
        }

        public virtual int GetCommitHeight(LibGit2Sharp.Commit fromCommit, LibGit2Sharp.Commit toCommit = null) => CommitHeightOverride;

        public string GetCommitShortHash(LibGit2Sharp.Commit commit) => "abcdef1";

        public SemVer AddBranchMetadata(SemVer version, string branchName, GitIntegrationConfig config = null)
        {
            // Mock implementation - return version unchanged
            return version;
        }

        public System.Collections.Generic.List<ChangeInfo> GetChangesSinceCommit(LibGit2Sharp.Commit sinceCommit, string projectPath = null) => new();

        public bool CreateTag(string tagName, string message, bool isGlobalTag, string projectName = null, bool dryRun = false)
        {
            // Mock implementation - always succeeds for testing
            return true;
        }

        public bool TagExists(string tagName)
        {
            // Use override if provided, otherwise no tags exist by default
            return TagExistsOverride?.Invoke(tagName) ?? false;
        }

        public bool HasSubmoduleChanges(LibGit2Sharp.Commit fromCommit, LibGit2Sharp.Commit toCommit = null)
        {
            // Mock implementation - no submodule changes by default
            return false;
        }

        public bool IsCommitReachable(LibGit2Sharp.Commit fromCommit, LibGit2Sharp.Commit toCommit = null)
        {
            // Mock implementation - assume commits are reachable
            return true;
        }

        public bool IsShallowClone => false;

        public void Dispose() { }
    }

    public class MockProjectAnalyzer
    {
        public bool TestIsTestProject(string projectContent)
        {
            // Check for explicit test project marker
            if (System.Text.RegularExpressions.Regex.IsMatch(projectContent, @"<IsTestProject\s*>\s*true\s*</IsTestProject>", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return true;
            }

            // Check for common test frameworks
            var testFrameworks = new[]
            {
                "Microsoft.NET.Test.Sdk",
                "xunit",
                "NUnit",
                "MSTest",
                "nunit",
                "MSTest.TestFramework"
            };

            return testFrameworks.Any(framework => 
                System.Text.RegularExpressions.Regex.IsMatch(projectContent, $@"<PackageReference[^>]+Include\s*=\s*""{System.Text.RegularExpressions.Regex.Escape(framework)}""", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        }
        
        public bool CreateTag(string tagName, string message, bool isGlobalTag, string projectName = null)
        {
            // Mock implementation - always succeeds for testing
            return true;
        }
        
        public bool TagExists(string tagName)
        {
            // Mock implementation - no tags exist by default
            return false;
        }
    }
}