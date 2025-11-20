using System;
using System.Collections.Generic;
using System.IO;
using LibGit2Sharp;
using Xunit;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;

namespace Mister.Version.Tests
{
    public class CalVerIntegrationTests : IDisposable
    {
        private readonly string _tempRepoPath;
        private readonly Repository _repo;
        private readonly Signature _signature;

        public CalVerIntegrationTests()
        {
            // Create a temporary repository for testing
            _tempRepoPath = Path.Combine(Path.GetTempPath(), "mister-version-calver-test-" + Guid.NewGuid());
            Directory.CreateDirectory(_tempRepoPath);
            Repository.Init(_tempRepoPath);
            _repo = new Repository(_tempRepoPath);
            _signature = new Signature("Test User", "test@example.com", DateTimeOffset.Now);

            // Create initial commit
            var readmePath = Path.Combine(_tempRepoPath, "README.md");
            File.WriteAllText(readmePath, "# Test Repository");
            Commands.Stage(_repo, "README.md");
            _repo.Commit("Initial commit", _signature, _signature);
        }

        public void Dispose()
        {
            _repo?.Dispose();
            if (Directory.Exists(_tempRepoPath))
            {
                try
                {
                    Directory.Delete(_tempRepoPath, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }

        #region CalVer Integration Tests

        [Fact]
        public void VersionCalculator_CalVer_YYYY_MM_Format_CalculatesCorrectly()
        {
            // Arrange
            var projectDir = Path.Combine(_tempRepoPath, "TestProject");
            Directory.CreateDirectory(projectDir);
            var projectFile = Path.Combine(projectDir, "TestProject.csproj");
            File.WriteAllText(projectFile, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            Commands.Stage(_repo, "TestProject/TestProject.csproj");
            _repo.Commit("Add test project", _signature, _signature);

            var gitService = new GitService(_repo);
            var calculator = new VersionCalculator(gitService);

            var options = new VersionOptions
            {
                RepoRoot = _tempRepoPath,
                ProjectPath = projectFile,
                ProjectName = "TestProject",
                TagPrefix = "v",
                Scheme = VersionScheme.CalVer,
                CalVer = new CalVerConfig
                {
                    Format = "YYYY.MM.PATCH",
                    ResetPatchPeriodically = true
                }
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(VersionScheme.CalVer, result.Scheme);
            Assert.NotNull(result.CalVerConfig);
            Assert.Equal("YYYY.MM.PATCH", result.CalVerConfig.Format);
            Assert.True(result.VersionChanged);
            Assert.Contains("CalVer", result.ChangeReason);

            // Verify version format: YYYY.MM.PATCH
            var versionParts = result.Version.Split('.');
            Assert.Equal(3, versionParts.Length);
            Assert.True(int.Parse(versionParts[0]) >= 2020); // Year should be reasonable
            Assert.True(int.Parse(versionParts[1]) >= 1 && int.Parse(versionParts[1]) <= 12); // Month 1-12
        }

        [Fact]
        public void VersionCalculator_CalVer_WithExistingTag_IncrementsPatc()
        {
            // Arrange
            var projectDir = Path.Combine(_tempRepoPath, "TestProject");
            Directory.CreateDirectory(projectDir);
            var projectFile = Path.Combine(projectDir, "TestProject.csproj");
            File.WriteAllText(projectFile, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            Commands.Stage(_repo, "TestProject/TestProject.csproj");
            var commit = _repo.Commit("Add test project", _signature, _signature);

            // Create a tag for the current month
            var now = DateTime.UtcNow;
            var tagName = $"v/TestProject/{now.Year}.{now.Month:D2}.0";
            _repo.Tags.Add(tagName, commit);

            // Make a change
            File.AppendAllText(projectFile, "<!-- Change -->");
            Commands.Stage(_repo, "TestProject/TestProject.csproj");
            _repo.Commit("Update project", _signature, _signature);

            var gitService = new GitService(_repo);
            var calculator = new VersionCalculator(gitService);

            var options = new VersionOptions
            {
                RepoRoot = _tempRepoPath,
                ProjectPath = projectFile,
                ProjectName = "TestProject",
                TagPrefix = "v",
                Scheme = VersionScheme.CalVer,
                CalVer = new CalVerConfig
                {
                    Format = "YYYY.MM.PATCH",
                    ResetPatchPeriodically = true
                }
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.VersionChanged);
            Assert.Contains("CalVer", result.ChangeReason);

            // Verify patch was incremented
            var versionParts = result.Version.Split('.', '-', '+')[0..3];
            Assert.Equal($"{now.Year}", versionParts[0]);
            Assert.Equal($"{now.Month:D2}", versionParts[1]);
            Assert.Equal("1", versionParts[2]); // Patch should be 1 (incremented from 0)
        }

        [Fact]
        public void VersionCalculator_CalVer_YY_0M_Format_CalculatesCorrectly()
        {
            // Arrange
            var projectDir = Path.Combine(_tempRepoPath, "TestProject");
            Directory.CreateDirectory(projectDir);
            var projectFile = Path.Combine(projectDir, "TestProject.csproj");
            File.WriteAllText(projectFile, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            Commands.Stage(_repo, "TestProject/TestProject.csproj");
            _repo.Commit("Add test project", _signature, _signature);

            var gitService = new GitService(_repo);
            var calculator = new VersionCalculator(gitService);

            var options = new VersionOptions
            {
                RepoRoot = _tempRepoPath,
                ProjectPath = projectFile,
                ProjectName = "TestProject",
                TagPrefix = "v",
                Scheme = VersionScheme.CalVer,
                CalVer = new CalVerConfig
                {
                    Format = "YY.0M.PATCH",
                    ResetPatchPeriodically = true
                }
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(VersionScheme.CalVer, result.Scheme);
            Assert.True(result.VersionChanged);

            // Verify version format: YY.0M.PATCH (2-digit year)
            var versionParts = result.Version.Split('.');
            Assert.Equal(3, versionParts.Length);
            Assert.True(int.Parse(versionParts[0]) >= 20 && int.Parse(versionParts[0]) <= 99); // 2-digit year
            Assert.True(int.Parse(versionParts[1]) >= 1 && int.Parse(versionParts[1]) <= 12); // Month 1-12
        }

        [Fact]
        public void VersionCalculator_CalVer_WithPrerelease_AppliesCorrectly()
        {
            // Arrange
            var projectDir = Path.Combine(_tempRepoPath, "TestProject");
            Directory.CreateDirectory(projectDir);
            var projectFile = Path.Combine(projectDir, "TestProject.csproj");
            File.WriteAllText(projectFile, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            Commands.Stage(_repo, "TestProject/TestProject.csproj");
            _repo.Commit("Add test project", _signature, _signature);

            var gitService = new GitService(_repo);
            var calculator = new VersionCalculator(gitService);

            var options = new VersionOptions
            {
                RepoRoot = _tempRepoPath,
                ProjectPath = projectFile,
                ProjectName = "TestProject",
                TagPrefix = "v",
                PrereleaseType = "alpha",
                Scheme = VersionScheme.CalVer,
                CalVer = new CalVerConfig
                {
                    Format = "YYYY.MM.PATCH"
                }
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.NotNull(result);
            Assert.Contains("-alpha", result.Version);
        }

        [Fact]
        public void VersionCalculator_CalVer_WithBranchMetadata_AppliesCorrectly()
        {
            // Arrange
            var projectDir = Path.Combine(_tempRepoPath, "TestProject");
            Directory.CreateDirectory(projectDir);
            var projectFile = Path.Combine(projectDir, "TestProject.csproj");
            File.WriteAllText(projectFile, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            Commands.Stage(_repo, "TestProject/TestProject.csproj");
            _repo.Commit("Add test project", _signature, _signature);

            // Create a feature branch
            var featureBranch = _repo.CreateBranch("feature/calver-test");
            Commands.Checkout(_repo, featureBranch);

            var gitService = new GitService(_repo);
            var calculator = new VersionCalculator(gitService);

            var options = new VersionOptions
            {
                RepoRoot = _tempRepoPath,
                ProjectPath = projectFile,
                ProjectName = "TestProject",
                TagPrefix = "v",
                Scheme = VersionScheme.CalVer,
                CalVer = new CalVerConfig
                {
                    Format = "YYYY.MM.PATCH"
                },
                GitIntegration = new GitIntegrationConfig
                {
                    IncludeBranchInMetadata = true
                }
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.NotNull(result);
            Assert.Contains("+", result.Version); // Should have build metadata
            Assert.Contains("calver", result.Version.ToLower()); // Branch name in metadata
        }

        [Fact]
        public void VersionCalculator_CalVer_NoChanges_SameMonth_NoVersionChange()
        {
            // Arrange
            var projectDir = Path.Combine(_tempRepoPath, "TestProject");
            Directory.CreateDirectory(projectDir);
            var projectFile = Path.Combine(projectDir, "TestProject.csproj");
            File.WriteAllText(projectFile, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            Commands.Stage(_repo, "TestProject/TestProject.csproj");
            var commit = _repo.Commit("Add test project", _signature, _signature);

            // Create a tag for the current month
            var now = DateTime.UtcNow;
            var tagName = $"v/TestProject/{now.Year}.{now.Month:D2}.0";
            _repo.Tags.Add(tagName, commit);

            // No changes made

            var gitService = new GitService(_repo);
            var calculator = new VersionCalculator(gitService);

            var options = new VersionOptions
            {
                RepoRoot = _tempRepoPath,
                ProjectPath = projectFile,
                ProjectName = "TestProject",
                TagPrefix = "v",
                Scheme = VersionScheme.CalVer,
                CalVer = new CalVerConfig
                {
                    Format = "YYYY.MM.PATCH",
                    ResetPatchPeriodically = true
                }
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.NotNull(result);
            Assert.False(result.VersionChanged);
            Assert.Contains("No changes", result.ChangeReason);
        }

        [Fact]
        public void VersionCalculator_CalVer_InvalidFormat_UsesDefault()
        {
            // Arrange
            var projectDir = Path.Combine(_tempRepoPath, "TestProject");
            Directory.CreateDirectory(projectDir);
            var projectFile = Path.Combine(projectDir, "TestProject.csproj");
            File.WriteAllText(projectFile, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            Commands.Stage(_repo, "TestProject/TestProject.csproj");
            _repo.Commit("Add test project", _signature, _signature);

            var gitService = new GitService(_repo);
            var calculator = new VersionCalculator(gitService);

            var options = new VersionOptions
            {
                RepoRoot = _tempRepoPath,
                ProjectPath = projectFile,
                ProjectName = "TestProject",
                TagPrefix = "v",
                Scheme = VersionScheme.CalVer,
                CalVer = new CalVerConfig
                {
                    Format = "INVALID.FORMAT",
                    ResetPatchPeriodically = true
                }
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.NotNull(result);
            // Should still calculate version using default format
            Assert.NotNull(result.Version);
            Assert.True(result.VersionChanged);
        }

        [Fact]
        public void VersionCalculator_CalVer_WeekFormat_CalculatesCorrectly()
        {
            // Arrange
            var projectDir = Path.Combine(_tempRepoPath, "TestProject");
            Directory.CreateDirectory(projectDir);
            var projectFile = Path.Combine(projectDir, "TestProject.csproj");
            File.WriteAllText(projectFile, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            Commands.Stage(_repo, "TestProject/TestProject.csproj");
            _repo.Commit("Add test project", _signature, _signature);

            var gitService = new GitService(_repo);
            var calculator = new VersionCalculator(gitService);

            var options = new VersionOptions
            {
                RepoRoot = _tempRepoPath,
                ProjectPath = projectFile,
                ProjectName = "TestProject",
                TagPrefix = "v",
                Scheme = VersionScheme.CalVer,
                CalVer = new CalVerConfig
                {
                    Format = "YYYY.WW.PATCH",
                    ResetPatchPeriodically = true
                }
            };

            // Act
            var result = calculator.CalculateVersion(options);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(VersionScheme.CalVer, result.Scheme);
            Assert.True(result.VersionChanged);

            // Verify version format: YYYY.WW.PATCH (week-based)
            var versionParts = result.Version.Split('.');
            Assert.Equal(3, versionParts.Length);
            Assert.True(int.Parse(versionParts[0]) >= 2020); // Year should be reasonable
            Assert.True(int.Parse(versionParts[1]) >= 1 && int.Parse(versionParts[1]) <= 53); // Week 1-53
        }

        #endregion
    }
}
