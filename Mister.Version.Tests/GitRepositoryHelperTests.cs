using System;
using System.IO;
using Xunit;
using Mister.Version.Core.Services;

namespace Mister.Version.Tests
{
    public class GitRepositoryHelperTests
    {
        [Fact]
        public void DiscoverRepositoryRoot_ReturnsNullForNullInput()
        {
            // Act
            var result = GitRepositoryHelper.DiscoverRepositoryRoot(null);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void DiscoverRepositoryRoot_ReturnsNullForEmptyInput()
        {
            // Act
            var result = GitRepositoryHelper.DiscoverRepositoryRoot(string.Empty);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void DiscoverRepositoryRoot_ReturnsNullForNonExistentPath()
        {
            // Act
            var result = GitRepositoryHelper.DiscoverRepositoryRoot("/definitely/does/not/exist/path");

            // Assert
            Assert.Null(result);
        }

        // Note: Testing with actual Git repositories requires integration tests
        // These unit tests verify the basic error handling and edge cases
    }
}