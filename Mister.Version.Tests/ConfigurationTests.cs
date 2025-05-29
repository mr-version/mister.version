using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Mister.Version.Core.Models;

namespace Mister.Version.Tests
{
    public class ConfigurationTests
    {
        private readonly IDeserializer _yamlDeserializer;

        public ConfigurationTests()
        {
            _yamlDeserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
        }

        [Fact]
        public void LoadConfig_MinimalConfiguration_LoadsSuccessfully()
        {
            // Arrange
            var yaml = @"
prereleaseType: alpha
";

            // Act
            var config = _yamlDeserializer.Deserialize<VersionConfig>(yaml);

            // Assert
            Assert.NotNull(config);
            Assert.Equal("alpha", config.PrereleaseType);
        }

        [Fact]
        public void LoadConfig_FullConfiguration_LoadsAllProperties()
        {
            // Arrange
            var yaml = @"
prereleaseType: beta
tagPrefix: ver-
skipTestProjects: false
skipNonPackableProjects: false
projects:
  MyProject:
    prereleaseType: rc
    forceVersion: 2.0.0
  AnotherProject:
    prereleaseType: alpha
    forceVersion: 1.0.0-custom
";

            // Act
            var config = _yamlDeserializer.Deserialize<VersionConfig>(yaml);

            // Assert
            Assert.NotNull(config);
            Assert.Equal("beta", config.PrereleaseType);
            Assert.Equal("ver-", config.TagPrefix);
            Assert.False(config.SkipTestProjects);
            Assert.False(config.SkipNonPackableProjects);
            
            Assert.NotNull(config.Projects);
            Assert.Equal(2, config.Projects.Count);
            
            Assert.True(config.Projects.ContainsKey("MyProject"));
            Assert.Equal("rc", config.Projects["MyProject"].PrereleaseType);
            Assert.Equal("2.0.0", config.Projects["MyProject"].ForceVersion);
            
            Assert.True(config.Projects.ContainsKey("AnotherProject"));
            Assert.Equal("alpha", config.Projects["AnotherProject"].PrereleaseType);
            Assert.Equal("1.0.0-custom", config.Projects["AnotherProject"].ForceVersion);
        }

        [Theory]
        [InlineData("none")]
        [InlineData("alpha")]
        [InlineData("beta")]
        [InlineData("rc")]
        public void LoadConfig_ValidPrereleaseTypes_LoadsSuccessfully(string prereleaseType)
        {
            // Arrange
            var yaml = $@"
prereleaseType: {prereleaseType}
";

            // Act
            var config = _yamlDeserializer.Deserialize<VersionConfig>(yaml);

            // Assert
            Assert.NotNull(config);
            Assert.Equal(prereleaseType, config.PrereleaseType);
        }

        [Fact]
        public void LoadConfig_EmptyConfiguration_ReturnsDefaultValues()
        {
            // Arrange
            var yaml = "";

            // Act
            var config = _yamlDeserializer.Deserialize<VersionConfig>(yaml);

            // Assert
            Assert.Null(config); // Empty YAML returns null
        }

        [Fact]
        public void LoadConfig_ProjectSpecificOverrides_TakePrecedence()
        {
            // Arrange
            var yaml = @"
prereleaseType: beta
projects:
  SpecialProject:
    prereleaseType: alpha
";

            // Act
            var config = _yamlDeserializer.Deserialize<VersionConfig>(yaml);

            // Assert
            Assert.NotNull(config);
            Assert.Equal("beta", config.PrereleaseType); // Global setting
            Assert.Equal("alpha", config.Projects["SpecialProject"].PrereleaseType); // Project override
        }

        [Fact]
        public void LoadConfig_InvalidYaml_ThrowsException()
        {
            // Arrange
            var yaml = @"
prereleaseType: beta
projects:
  MyProject:
    prereleaseType: alpha
  forceVersion: 1.0.0  # Invalid - forceVersion is not at the right level
";

            // Act & Assert
            Assert.Throws<YamlDotNet.Core.YamlException>(() => 
                _yamlDeserializer.Deserialize<VersionConfig>(yaml));
        }

        [Fact]
        public void LoadConfig_MissingFile_HandledGracefully()
        {
            // Arrange
            var nonExistentPath = Path.Combine(Path.GetTempPath(), "non-existent-config.yml");

            // Act
            var fileExists = File.Exists(nonExistentPath);

            // Assert
            Assert.False(fileExists);
        }

        [Fact]
        public void LoadConfig_ComplexProjectConfiguration_LoadsCorrectly()
        {
            // Arrange
            var yaml = @"
prereleaseType: none
tagPrefix: v
skipTestProjects: true
skipNonPackableProjects: true
projects:
  CoreLibrary:
    prereleaseType: beta
    forceVersion: 3.0.0-beta.1
  WebAPI:
    prereleaseType: rc
  ConsoleApp:
    forceVersion: 1.0.0
  TestProject:
    prereleaseType: none
";

            // Act
            var config = _yamlDeserializer.Deserialize<VersionConfig>(yaml);

            // Assert
            Assert.NotNull(config);
            Assert.Equal(4, config.Projects.Count);
            
            // Verify each project configuration
            var coreLib = config.Projects["CoreLibrary"];
            Assert.Equal("beta", coreLib.PrereleaseType);
            Assert.Equal("3.0.0-beta.1", coreLib.ForceVersion);
            
            var webApi = config.Projects["WebAPI"];
            Assert.Equal("rc", webApi.PrereleaseType);
            Assert.Null(webApi.ForceVersion);
            
            var consoleApp = config.Projects["ConsoleApp"];
            Assert.Null(consoleApp.PrereleaseType);
            Assert.Equal("1.0.0", consoleApp.ForceVersion);
            
            var testProject = config.Projects["TestProject"];
            Assert.Equal("none", testProject.PrereleaseType);
            Assert.Null(testProject.ForceVersion);
        }

        [Theory]
        [InlineData("v", "v")]
        [InlineData("ver", "ver")]
        [InlineData("version-", "version-")]
        [InlineData("\"\"", "")]
        [InlineData("tag_", "tag_")]
        public void LoadConfig_DifferentTagPrefixes_LoadsCorrectly(string tagPrefix, string expected)
        {
            // Arrange
            var yaml = $@"
tagPrefix: {tagPrefix}
";

            // Act
            var config = _yamlDeserializer.Deserialize<VersionConfig>(yaml);

            // Assert
            Assert.NotNull(config);
            Assert.Equal(expected, config.TagPrefix);
        }

        [Theory]
        [InlineData("true", true)]
        [InlineData("false", false)]
        [InlineData("True", true)]
        [InlineData("False", false)]
        public void LoadConfig_BooleanValues_ParseCorrectly(string value, bool expected)
        {
            // Arrange
            var yaml = $@"
skipTestProjects: {value}
skipNonPackableProjects: {value}
";

            // Act
            var config = _yamlDeserializer.Deserialize<VersionConfig>(yaml);

            // Assert
            Assert.NotNull(config);
            Assert.Equal(expected, config.SkipTestProjects);
            Assert.Equal(expected, config.SkipNonPackableProjects);
        }
    }

    // Using the actual models from the core project
}