using System;
using System.IO;
using System.Linq;
using LibGit2Sharp;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;
using Xunit;

namespace Mister.Version.Tests
{
    public class TagCreationTests : IDisposable
    {
        private readonly string _testRepoPath;
        private Repository _repo;

        public TagCreationTests()
        {
            _testRepoPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(_testRepoPath);
            Repository.Init(_testRepoPath);
            _repo = new Repository(_testRepoPath);

            // Create initial commit
            var sig = new Signature("Test User", "test@example.com", DateTimeOffset.Now);
            var testFile = Path.Combine(_testRepoPath, "test.txt");
            File.WriteAllText(testFile, "test content");
            Commands.Stage(_repo, testFile);
            _repo.Commit("Initial commit", sig, sig);
        }

        [Fact]
        public void CreateTag_ShouldCreateNewTag()
        {
            // Arrange
            using var gitService = new GitService(_testRepoPath);
            var tagName = "v1.0.0";
            var tagMessage = "Release version 1.0.0";

            // Act
            var result = gitService.CreateTag(tagName, tagMessage, true);

            // Assert
            Assert.True(result);
            Assert.True(gitService.TagExists(tagName));
            var tag = _repo.Tags[tagName];
            Assert.NotNull(tag);
            Assert.Equal(_repo.Head.Tip, tag.Target);
        }

        [Fact]
        public void CreateTag_ShouldReturnFalseIfTagExists()
        {
            // Arrange
            using var gitService = new GitService(_testRepoPath);
            var tagName = "v1.0.0";
            var tagMessage = "Release version 1.0.0";

            // Create tag first time
            gitService.CreateTag(tagName, tagMessage, true);

            // Act - try to create same tag again
            var result = gitService.CreateTag(tagName, tagMessage, true);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void TagExists_ShouldReturnTrueForExistingTag()
        {
            // Arrange
            using var gitService = new GitService(_testRepoPath);
            var tagName = "v1.0.0";
            gitService.CreateTag(tagName, "Test tag", true);

            // Act
            var exists = gitService.TagExists(tagName);

            // Assert
            Assert.True(exists);
        }

        [Fact]
        public void TagExists_ShouldReturnFalseForNonExistingTag()
        {
            // Arrange
            using var gitService = new GitService(_testRepoPath);

            // Act
            var exists = gitService.TagExists("v999.999.999");

            // Assert
            Assert.False(exists);
        }

        [Fact]
        public void CreateTag_WithEmptyRepository_ShouldFail()
        {
            // Arrange
            var emptyRepoPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(emptyRepoPath);
            Repository.Init(emptyRepoPath);

            try
            {
                using var gitService = new GitService(emptyRepoPath);

                // Act
                var result = gitService.CreateTag("v1.0.0", "Test", true);

                // Assert
                Assert.False(result);
            }
            finally
            {
                Directory.Delete(emptyRepoPath, true);
            }
        }

        public void Dispose()
        {
            _repo?.Dispose();
            if (Directory.Exists(_testRepoPath))
            {
                try
                {
                    Directory.Delete(_testRepoPath, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }
}