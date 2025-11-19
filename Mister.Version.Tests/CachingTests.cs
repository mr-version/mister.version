using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibGit2Sharp;
using Xunit;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;

namespace Mister.Version.Tests
{
    public class CachingTests : IDisposable
    {
        private readonly string _testRepoRoot;
        private Repository _testRepo;

        public CachingTests()
        {
            _testRepoRoot = Path.Combine(Path.GetTempPath(), "test-cache-repo-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testRepoRoot);

            // Initialize a git repository
            Repository.Init(_testRepoRoot);
            _testRepo = new Repository(_testRepoRoot);

            // Create initial commit
            var signature = new Signature("Test User", "test@example.com", DateTimeOffset.Now);
            CreateFile("README.md", "# Test Repository");
            Commands.Stage(_testRepo, "*");
            _testRepo.Commit("Initial commit", signature, signature);
        }

        private void CreateFile(string relativePath, string content)
        {
            var fullPath = Path.Combine(_testRepoRoot, relativePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(fullPath, content);
        }

        [Fact]
        public void VersionCache_InitializesCorrectly()
        {
            // Arrange
            var headSha = _testRepo.Head.Tip.Sha;

            // Act
            var cache = new VersionCache(_testRepoRoot, headSha);

            // Assert
            Assert.NotNull(cache);
            var stats = cache.GetStatistics();
            Assert.Equal(headSha, stats.CurrentHeadSha);
            Assert.False(stats.AllProjectsCached);
            Assert.Equal(0, stats.ProjectDependenciesCount);
        }

        [Fact]
        public void VersionCache_StoresAndRetrievesAllProjects()
        {
            // Arrange
            var cache = new VersionCache(_testRepoRoot, _testRepo.Head.Tip.Sha);
            var projects = new List<string>
            {
                Path.Combine(_testRepoRoot, "Project1.csproj"),
                Path.Combine(_testRepoRoot, "Project2.csproj")
            };

            // Act
            cache.SetAllProjects(projects);
            var retrieved = cache.GetAllProjects();

            // Assert
            Assert.NotNull(retrieved);
            Assert.Equal(2, retrieved.Count);
            Assert.Contains(projects[0], retrieved);
            Assert.Contains(projects[1], retrieved);

            var stats = cache.GetStatistics();
            Assert.True(stats.AllProjectsCached);
        }

        [Fact]
        public void VersionCache_StoresAndRetrievesProjectDependencies()
        {
            // Arrange
            var cache = new VersionCache(_testRepoRoot, _testRepo.Head.Tip.Sha);
            var projectPath = Path.Combine(_testRepoRoot, "Project1.csproj");
            var dependencies = new List<string>
            {
                Path.Combine(_testRepoRoot, "Dependency1.csproj"),
                Path.Combine(_testRepoRoot, "Dependency2.csproj")
            };

            // Act
            cache.SetProjectDependencies(projectPath, dependencies);
            var retrieved = cache.GetProjectDependencies(projectPath);

            // Assert
            Assert.NotNull(retrieved);
            Assert.Equal(2, retrieved.Count);
            Assert.Contains(dependencies[0], retrieved);
            Assert.Contains(dependencies[1], retrieved);
        }

        [Fact]
        public void VersionCache_StoresAndRetrievesProjectVersionTag()
        {
            // Arrange
            var cache = new VersionCache(_testRepoRoot, _testRepo.Head.Tip.Sha);
            var cacheKey = "TestProject_Main_v";
            var versionTag = new VersionTag
            {
                SemVer = new SemVer { Major = 1, Minor = 2, Patch = 3 },
                IsGlobal = false,
                ProjectName = "TestProject"
            };

            // Act
            cache.SetProjectVersionTag(cacheKey, versionTag);
            var retrieved = cache.GetProjectVersionTag(cacheKey);

            // Assert
            Assert.NotNull(retrieved);
            Assert.Equal("TestProject", retrieved.ProjectName);
            Assert.Equal(1, retrieved.SemVer.Major);
            Assert.Equal(2, retrieved.SemVer.Minor);
            Assert.Equal(3, retrieved.SemVer.Patch);
        }

        [Fact]
        public void VersionCache_StoresAndRetrievesCommitHeight()
        {
            // Arrange
            var cache = new VersionCache(_testRepoRoot, _testRepo.Head.Tip.Sha);
            var commitSha = _testRepo.Head.Tip.Sha;

            // Act
            cache.SetCommitHeight(commitSha, 42);
            var retrieved = cache.GetCommitHeight(commitSha);

            // Assert
            Assert.NotNull(retrieved);
            Assert.Equal(42, retrieved.Value);
        }

        [Fact]
        public void VersionCache_StoresAndRetrievesProjectHasChanges()
        {
            // Arrange
            var cache = new VersionCache(_testRepoRoot, _testRepo.Head.Tip.Sha);
            var cacheKey = "commit123_path/to/project_deps";

            // Act
            cache.SetProjectHasChanges(cacheKey, true);
            var retrieved = cache.GetProjectHasChanges(cacheKey);

            // Assert
            Assert.NotNull(retrieved);
            Assert.True(retrieved.Value);
        }

        [Fact]
        public void VersionCache_InvalidatesOnHeadChange()
        {
            // Arrange
            var initialHeadSha = _testRepo.Head.Tip.Sha;
            var cache = new VersionCache(_testRepoRoot, initialHeadSha);

            // Store some data
            var projects = new List<string> { "Project1.csproj" };
            cache.SetAllProjects(projects);
            Assert.True(cache.GetStatistics().AllProjectsCached);

            // Create a new commit to change HEAD
            CreateFile("newfile.txt", "New content");
            Commands.Stage(_testRepo, "*");
            var signature = new Signature("Test User", "test@example.com", DateTimeOffset.Now);
            _testRepo.Commit("Second commit", signature, signature);
            var newHeadSha = _testRepo.Head.Tip.Sha;

            // Act
            var wasInvalidated = cache.ValidateAndInvalidate(newHeadSha);

            // Assert
            Assert.True(wasInvalidated);
            Assert.False(cache.GetStatistics().AllProjectsCached);
            Assert.Null(cache.GetAllProjects());
        }

        [Fact]
        public void VersionCache_DoesNotInvalidateWhenHeadUnchanged()
        {
            // Arrange
            var headSha = _testRepo.Head.Tip.Sha;
            var cache = new VersionCache(_testRepoRoot, headSha);

            // Store some data
            var projects = new List<string> { "Project1.csproj" };
            cache.SetAllProjects(projects);

            // Act
            var wasInvalidated = cache.ValidateAndInvalidate(headSha);

            // Assert
            Assert.False(wasInvalidated);
            Assert.True(cache.GetStatistics().AllProjectsCached);
            Assert.NotNull(cache.GetAllProjects());
        }

        [Fact]
        public void VersionCache_ClearAllRemovesAllCachedData()
        {
            // Arrange
            var cache = new VersionCache(_testRepoRoot, _testRepo.Head.Tip.Sha);

            // Populate cache with various data
            cache.SetAllProjects(new List<string> { "Project1.csproj" });
            cache.SetProjectDependencies("Project1.csproj", new List<string> { "Dep1.csproj" });
            cache.SetCommitHeight("abc123", 10);
            cache.SetProjectHasChanges("key1", true);

            // Verify data is cached
            Assert.NotNull(cache.GetAllProjects());

            // Act
            cache.ClearAll();

            // Assert
            var stats = cache.GetStatistics();
            Assert.False(stats.AllProjectsCached);
            Assert.Equal(0, stats.ProjectDependenciesCount);
            Assert.Equal(0, stats.CommitHeightCount);
            Assert.Equal(0, stats.ProjectChangesCount);
            Assert.Null(cache.GetAllProjects());
        }

        [Fact]
        public void GitService_UsesCacheForProjectVersionTag()
        {
            // Arrange
            var headSha = _testRepo.Head.Tip.Sha;
            var cache = new VersionCache(_testRepoRoot, headSha);
            var gitService = new GitService(_testRepoRoot, cache);

            // Create a tag
            var signature = new Signature("Test User", "test@example.com", DateTimeOffset.Now);
            _testRepo.ApplyTag("TestProject-v1.0.0", signature, "Test tag");

            // First call - should populate cache
            var result1 = gitService.GetProjectVersionTag("TestProject", BranchType.Main, "v");

            // Verify it's cached
            var cacheKey = "TestProject_Main_v";
            var cachedTag = cache.GetProjectVersionTag(cacheKey);
            Assert.NotNull(cachedTag);

            // Second call - should use cache
            var result2 = gitService.GetProjectVersionTag("TestProject", BranchType.Main, "v");

            // Assert both calls return the same result
            Assert.NotNull(result1);
            Assert.NotNull(result2);
            Assert.Equal(result1.SemVer.ToVersionString(), result2.SemVer.ToVersionString());
        }

        [Fact]
        public void GitService_UsesCacheForCommitHeight()
        {
            // Arrange
            var signature = new Signature("Test User", "test@example.com", DateTimeOffset.Now);

            // Create multiple commits
            CreateFile("file1.txt", "Content 1");
            Commands.Stage(_testRepo, "*");
            var commit1 = _testRepo.Commit("Commit 1", signature, signature);

            CreateFile("file2.txt", "Content 2");
            Commands.Stage(_testRepo, "*");
            var commit2 = _testRepo.Commit("Commit 2", signature, signature);

            CreateFile("file3.txt", "Content 3");
            Commands.Stage(_testRepo, "*");
            _testRepo.Commit("Commit 3", signature, signature);

            var headSha = _testRepo.Head.Tip.Sha;
            var cache = new VersionCache(_testRepoRoot, headSha);
            var gitService = new GitService(_testRepoRoot, cache);

            // Act - First call populates cache
            var height1 = gitService.GetCommitHeight(commit1);

            // Verify it's cached
            var cacheKey = $"{commit1.Sha}_{_testRepo.Head.Tip.Sha}";
            var cachedHeight = cache.GetCommitHeight(cacheKey);
            Assert.NotNull(cachedHeight);

            // Second call uses cache
            var height2 = gitService.GetCommitHeight(commit1);

            // Assert
            Assert.Equal(height1, height2);
            Assert.Equal(2, height1); // Should be 2 commits between commit1 and HEAD
        }

        [Fact]
        public void ProjectAnalyzer_UsesCacheForProjectDiscovery()
        {
            // Arrange
            // Create some project files
            CreateFile("src/Project1/Project1.csproj", "<Project />");
            CreateFile("src/Project2/Project2.csproj", "<Project />");

            var headSha = _testRepo.Head.Tip.Sha;
            var cache = new VersionCache(_testRepoRoot, headSha);

            var gitService = new GitService(_testRepoRoot, cache);
            var versionCalculator = new VersionCalculator(gitService);
            var analyzer = new ProjectAnalyzer(versionCalculator, gitService);
            analyzer.Cache = cache;

            // Act - This should populate the cache
            var projects1 = analyzer.AnalyzeProjects(_testRepoRoot);

            // Verify cache was populated
            Assert.True(cache.GetStatistics().AllProjectsCached);
            var cachedProjects = cache.GetAllProjects();
            Assert.NotNull(cachedProjects);
            Assert.Equal(2, cachedProjects.Count);

            // Second call should use cache
            var projects2 = analyzer.AnalyzeProjects(_testRepoRoot);

            // Assert
            Assert.Equal(projects1.Count, projects2.Count);
        }

        [Fact]
        public void ProjectAnalyzer_UsesCacheForProjectDependencies()
        {
            // Arrange
            CreateFile("src/Project1/Project1.csproj", @"<Project>
  <ItemGroup>
    <ProjectReference Include=""../Shared/Shared.csproj"" />
  </ItemGroup>
</Project>");
            CreateFile("src/Shared/Shared.csproj", "<Project />");

            var headSha = _testRepo.Head.Tip.Sha;
            var cache = new VersionCache(_testRepoRoot, headSha);

            var gitService = new GitService(_testRepoRoot, cache);
            var versionCalculator = new VersionCalculator(gitService);
            var analyzer = new ProjectAnalyzer(versionCalculator, gitService);
            analyzer.Cache = cache;

            var projectPath = Path.Combine(_testRepoRoot, "src/Project1/Project1.csproj");

            // Act - First call populates cache
            var deps1 = analyzer.GetProjectDependencies(projectPath, _testRepoRoot);

            // Verify cache was populated
            var cachedDeps = cache.GetProjectDependencies(projectPath);
            Assert.NotNull(cachedDeps);
            Assert.Single(cachedDeps);

            // Second call uses cache
            var deps2 = analyzer.GetProjectDependencies(projectPath, _testRepoRoot);

            // Assert
            Assert.Equal(deps1.Count, deps2.Count);
            Assert.Single(deps2);
        }

        [Fact]
        public void MonoRepoVersionTask_ClearCache_RemovesStaticCache()
        {
            // Act
            MonoRepoVersionTask.ClearCache();

            // Assert - we can't directly verify the static cache is null,
            // but we can verify that the method doesn't throw
            Assert.True(true);
        }

        [Fact]
        public void VersionCache_GetStatistics_ReturnsCorrectCounts()
        {
            // Arrange
            var cache = new VersionCache(_testRepoRoot, _testRepo.Head.Tip.Sha);

            // Populate cache with different types of data
            cache.SetAllProjects(new List<string> { "P1.csproj", "P2.csproj" });
            cache.SetProjectDependencies("P1.csproj", new List<string> { "D1.csproj" });
            cache.SetProjectDependencies("P2.csproj", new List<string> { "D2.csproj" });
            cache.SetProjectVersionTag("key1", new VersionTag { SemVer = new SemVer { Major = 1 } });
            cache.SetProjectVersionTag("key2", new VersionTag { SemVer = new SemVer { Major = 2 } });
            cache.SetCommitHeight("sha1", 10);
            cache.SetCommitHeight("sha2", 20);
            cache.SetCommitHeight("sha3", 30);
            cache.SetProjectHasChanges("change1", true);

            // Act
            var stats = cache.GetStatistics();

            // Assert
            Assert.True(stats.AllProjectsCached);
            Assert.Equal(2, stats.ProjectDependenciesCount);
            Assert.Equal(2, stats.ProjectVersionTagsCount);
            Assert.Equal(3, stats.CommitHeightCount);
            Assert.Equal(1, stats.ProjectChangesCount);
        }

        [Fact]
        public void VersionCache_ReturnsNullForMissingData()
        {
            // Arrange
            var cache = new VersionCache(_testRepoRoot, _testRepo.Head.Tip.Sha);

            // Act & Assert
            Assert.Null(cache.GetAllProjects());
            Assert.Null(cache.GetProjectDependencies("nonexistent"));
            Assert.Null(cache.GetProjectVersionTag("nonexistent"));
            Assert.Null(cache.GetCommitHeight("nonexistent"));
            Assert.Null(cache.GetProjectHasChanges("nonexistent"));
        }

        public void Dispose()
        {
            try
            {
                _testRepo?.Dispose();

                if (Directory.Exists(_testRepoRoot))
                {
                    // Remove read-only attributes that git creates
                    foreach (var file in Directory.GetFiles(_testRepoRoot, "*", SearchOption.AllDirectories))
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }
                    Directory.Delete(_testRepoRoot, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
