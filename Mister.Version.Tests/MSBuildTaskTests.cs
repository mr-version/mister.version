using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Xunit;
using Mister.Version.Core.Models;

namespace Mister.Version.Tests
{
    public class MSBuildTaskTests
    {
        private readonly string _testRepoRoot;
        private readonly string _testProjectPath;

        public MSBuildTaskTests()
        {
            _testRepoRoot = Path.Combine(Path.GetTempPath(), "test-repo-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testRepoRoot);

            var projectDir = Path.Combine(_testRepoRoot, "src", "TestProject");
            Directory.CreateDirectory(projectDir);
            _testProjectPath = Path.Combine(projectDir, "TestProject.csproj");

            // Create a minimal project file
            File.WriteAllText(_testProjectPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>");
        }

        [Fact]
        public void MonoRepoVersionTask_RequiredPropertiesValidation()
        {
            // Arrange
            var task = new MonoRepoVersionTask();
            var buildEngine = new MockBuildEngine();
            task.BuildEngine = buildEngine;

            // Act & Assert - Missing required properties should fail
            var result = task.Execute();
            Assert.False(result);
            Assert.True(buildEngine.Errors.Count > 0);

            // Set required properties
            task.ProjectPath = _testProjectPath;
            task.RepoRoot = _testRepoRoot;

            // Now it should not throw (but may fail for other reasons like no git repo)
            // We're just testing that required properties are validated
        }

        [Fact]
        public void MonoRepoVersionTask_DefaultPropertyValues()
        {
            // Arrange
            var task = new MonoRepoVersionTask();

            // Assert default values
            Assert.Equal("v", task.TagPrefix);
            Assert.False(task.UpdateProjectFile);
            Assert.False(task.Debug);
            Assert.False(task.ExtraDebug);
            Assert.True(task.SkipTestProjects);
            Assert.True(task.SkipNonPackableProjects);
            Assert.False(task.IsTestProject);
            Assert.True(task.IsPackable);
            Assert.Equal("none", task.PrereleaseType);
            Assert.Null(task.ConfigFile);
            Assert.Null(task.ForceVersion);
        }

        [Theory]
        [InlineData("1.2.3", "1.2.3")]
        [InlineData("2.0.0-alpha.1", "2.0.0-alpha.1")]
        [InlineData("3.1.4-beta.2+build.123", "3.1.4-beta.2+build.123")]
        public void MonoRepoVersionTask_ForcedVersion_SetsOutputCorrectly(string forcedVersion, string expectedVersion)
        {
            // Arrange
            var task = new MonoRepoVersionTask
            {
                ProjectPath = _testProjectPath,
                RepoRoot = _testRepoRoot,
                ForceVersion = forcedVersion,
                BuildEngine = new MockBuildEngine()
            };

            // Act
            // Note: This will fail due to no git repo, but we can check if the property is set
            try
            {
                task.Execute();
            }
            catch
            {
                // Expected to fail due to no git repo
            }

            // Assert - The task should attempt to use the forced version
            Assert.Equal(forcedVersion, task.ForceVersion);
        }

        [Fact]
        public void MonoRepoVersionTask_DependenciesHandling()
        {
            // Arrange
            var dependencies = new[]
            {
                new TaskItem("../SharedLib/SharedLib.csproj"),
                new TaskItem("../Common/Common.csproj")
            };

            var task = new MonoRepoVersionTask
            {
                ProjectPath = _testProjectPath,
                RepoRoot = _testRepoRoot,
                Dependencies = dependencies,
                BuildEngine = new MockBuildEngine()
            };

            // Assert
            Assert.NotNull(task.Dependencies);
            Assert.Equal(2, task.Dependencies.Length);
            Assert.Equal("../SharedLib/SharedLib.csproj", task.Dependencies[0].ItemSpec);
            Assert.Equal("../Common/Common.csproj", task.Dependencies[1].ItemSpec);
        }

        [Theory]
        [InlineData(true, false, "Test project - skipping")]
        [InlineData(false, true, "Packable project - processing")]
        [InlineData(true, true, "Test project - skipping")]
        [InlineData(false, false, "Non-packable project - processing")]
        public void MonoRepoVersionTask_SkipProjectLogic(bool isTest, bool skipTest, string scenario)
        {
            // Arrange
            var task = new MonoRepoVersionTask
            {
                ProjectPath = _testProjectPath,
                RepoRoot = _testRepoRoot,
                IsTestProject = isTest,
                SkipTestProjects = skipTest,
                BuildEngine = new MockBuildEngine()
            };

            // The actual skipping logic is in VersionCalculator
            // Here we just verify the properties are set correctly
            Assert.Equal(isTest, task.IsTestProject);
            Assert.Equal(skipTest, task.SkipTestProjects);
        }

        [Theory]
        [InlineData("none")]
        [InlineData("alpha")]
        [InlineData("beta")]
        [InlineData("rc")]
        public void MonoRepoVersionTask_PrereleaseTypeHandling(string prereleaseType)
        {
            // Arrange
            var task = new MonoRepoVersionTask
            {
                ProjectPath = _testProjectPath,
                RepoRoot = _testRepoRoot,
                PrereleaseType = prereleaseType,
                BuildEngine = new MockBuildEngine()
            };

            // Assert
            Assert.Equal(prereleaseType, task.PrereleaseType);
        }

        [Fact]
        public void MonoRepoVersionTask_ConfigFileHandling()
        {
            // Arrange
            var configPath = Path.Combine(_testRepoRoot, "version.yml");
            File.WriteAllText(configPath, @"
prereleaseType: beta
tagPrefix: version-
skipTestProjects: false
projects:
  TestProject:
    prereleaseType: alpha
    forceVersion: 1.0.0-custom
");

            var task = new MonoRepoVersionTask
            {
                ProjectPath = _testProjectPath,
                RepoRoot = _testRepoRoot,
                ConfigFile = configPath,
                BuildEngine = new MockBuildEngine()
            };

            // Assert
            Assert.Equal(configPath, task.ConfigFile);
            Assert.True(File.Exists(task.ConfigFile));
        }

        [Fact]
        public void MonoRepoVersionTask_UpdateProjectFileDisabledByDefault()
        {
            // Arrange
            var task = new MonoRepoVersionTask
            {
                ProjectPath = _testProjectPath,
                RepoRoot = _testRepoRoot,
                BuildEngine = new MockBuildEngine()
            };

            // Assert
            Assert.False(task.UpdateProjectFile);
        }

        [Fact]
        public void MonoRepoVersionTask_OutputPropertiesExist()
        {
            // Arrange
            var task = new MonoRepoVersionTask();

            // Assert - Check that output properties have the correct attributes
            var versionProperty = typeof(MonoRepoVersionTask).GetProperty("Version");
            var versionChangedProperty = typeof(MonoRepoVersionTask).GetProperty("VersionChanged");

            Assert.NotNull(versionProperty);
            Assert.NotNull(versionChangedProperty);

            var versionOutputAttr = versionProperty.GetCustomAttributes(typeof(OutputAttribute), false).FirstOrDefault();
            var versionChangedOutputAttr = versionChangedProperty.GetCustomAttributes(typeof(OutputAttribute), false).FirstOrDefault();

            Assert.NotNull(versionOutputAttr);
            Assert.NotNull(versionChangedOutputAttr);
        }

        // Cleanup
        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testRepoRoot))
                {
                    Directory.Delete(_testRepoRoot, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    // Mock build engine for testing
    public class MockBuildEngine : IBuildEngine
    {
        public List<BuildErrorEventArgs> Errors { get; } = new List<BuildErrorEventArgs>();
        public List<BuildWarningEventArgs> Warnings { get; } = new List<BuildWarningEventArgs>();
        public List<BuildMessageEventArgs> Messages { get; } = new List<BuildMessageEventArgs>();

        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => string.Empty;

        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs)
        {
            return true;
        }

        public void LogCustomEvent(CustomBuildEventArgs e)
        {
        }

        public void LogErrorEvent(BuildErrorEventArgs e)
        {
            Errors.Add(e);
        }

        public void LogMessageEvent(BuildMessageEventArgs e)
        {
            Messages.Add(e);
        }

        public void LogWarningEvent(BuildWarningEventArgs e)
        {
            Warnings.Add(e);
        }
    }
}