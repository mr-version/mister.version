using System.Collections.Generic;
using Xunit;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;

namespace Mister.Version.Tests
{
    public class AdditionalDirectoryMonitoringTests
    {
        [Fact]
        public void ChangeDetectionConfig_AdditionalMonitorPaths_InitializesEmpty()
        {
            // Arrange & Act
            var config = new ChangeDetectionConfig();

            // Assert
            Assert.NotNull(config.AdditionalMonitorPaths);
            Assert.Empty(config.AdditionalMonitorPaths);
        }

        [Fact]
        public void ChangeDetectionConfig_AdditionalMonitorPaths_CanAddPaths()
        {
            // Arrange
            var config = new ChangeDetectionConfig
            {
                AdditionalMonitorPaths = new List<string>
                {
                    "shared/common",
                    "../external-lib"
                }
            };

            // Act & Assert
            Assert.Equal(2, config.AdditionalMonitorPaths.Count);
            Assert.Contains("shared/common", config.AdditionalMonitorPaths);
            Assert.Contains("../external-lib", config.AdditionalMonitorPaths);
        }

        [Fact]
        public void VersionOptions_AdditionalMonitorPaths_InitializesEmpty()
        {
            // Arrange & Act
            var options = new VersionOptions();

            // Assert
            Assert.NotNull(options.AdditionalMonitorPaths);
            Assert.Empty(options.AdditionalMonitorPaths);
        }

        [Fact]
        public void ProjectVersionConfig_AdditionalMonitorPaths_InitializesEmpty()
        {
            // Arrange & Act
            var config = new ProjectVersionConfig();

            // Assert
            Assert.NotNull(config.AdditionalMonitorPaths);
            Assert.Empty(config.AdditionalMonitorPaths);
        }

        [Fact]
        public void ProjectVersionConfig_AdditionalMonitorPaths_SupportsMultiplePaths()
        {
            // Arrange
            var config = new ProjectVersionConfig
            {
                AdditionalMonitorPaths = new List<string>
                {
                    "/absolute/path",
                    "relative/path",
                    "../parent/path"
                }
            };

            // Act & Assert
            Assert.Equal(3, config.AdditionalMonitorPaths.Count);
            Assert.Contains("/absolute/path", config.AdditionalMonitorPaths);
            Assert.Contains("relative/path", config.AdditionalMonitorPaths);
            Assert.Contains("../parent/path", config.AdditionalMonitorPaths);
        }

        [Fact]
        public void VersioningService_ParsePatternString_HandlesAdditionalMonitorPaths()
        {
            // Arrange
            string semicolonSeparated = "path1;path2;path3";

            // Act
            var result = VersioningService.ParsePatternString(semicolonSeparated);

            // Assert
            Assert.Equal(3, result.Count);
            Assert.Contains("path1", result);
            Assert.Contains("path2", result);
            Assert.Contains("path3", result);
        }

        [Fact]
        public void VersioningService_ParsePatternString_HandlesEmptyString()
        {
            // Act
            var result = VersioningService.ParsePatternString("");

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public void VersioningService_ParsePatternString_HandlesNull()
        {
            // Act
            var result = VersioningService.ParsePatternString(null);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public void VersioningService_ParsePatternString_TrimsWhitespace()
        {
            // Arrange
            string withWhitespace = " path1 ; path2 ; path3 ";

            // Act
            var result = VersioningService.ParsePatternString(withWhitespace);

            // Assert
            Assert.Equal(3, result.Count);
            Assert.Contains("path1", result);
            Assert.Contains("path2", result);
            Assert.Contains("path3", result);
        }

        [Theory]
        [InlineData("path1", 1)]
        [InlineData("path1;path2", 2)]
        [InlineData("path1;path2;path3", 3)]
        [InlineData("", 0)]
        [InlineData("path1;;path2", 2)] // empty entries filtered out
        public void VersioningService_ParsePatternString_HandlesVariousFormats(string input, int expectedCount)
        {
            // Act
            var result = VersioningService.ParsePatternString(input);

            // Assert
            Assert.Equal(expectedCount, result.Count);
        }

        [Fact]
        public void ChangeDetectionConfig_AdditionalMonitorPaths_CanBeSetInConfig()
        {
            // Arrange
            var config = new ChangeDetectionConfig
            {
                Enabled = true,
                IgnorePatterns = new List<string> { "**/*.md" },
                AdditionalMonitorPaths = new List<string>
                {
                    "shared/utils",
                    "common/libs"
                }
            };

            // Act & Assert
            Assert.True(config.Enabled);
            Assert.Equal(2, config.AdditionalMonitorPaths.Count);
            Assert.Contains("shared/utils", config.AdditionalMonitorPaths);
            Assert.Contains("common/libs", config.AdditionalMonitorPaths);
        }

        [Fact]
        public void AdditionalMonitorPaths_SupportsAbsoluteAndRelativePaths()
        {
            // Arrange
            var config = new ChangeDetectionConfig
            {
                AdditionalMonitorPaths = new List<string>
                {
                    "/absolute/path/to/shared",
                    "relative/path/to/common",
                    "../parent/directory",
                    "C:\\Windows\\Style\\Path"
                }
            };

            // Act & Assert
            Assert.Equal(4, config.AdditionalMonitorPaths.Count);
            Assert.All(config.AdditionalMonitorPaths, path => Assert.False(string.IsNullOrWhiteSpace(path)));
        }
    }
}
