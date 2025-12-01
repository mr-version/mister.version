using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibGit2Sharp;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Xunit;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;

namespace Mister.Version.Tests
{
    public class HasChangesTaskTests : IDisposable
    {
        private readonly string _testRepoRoot;
        private readonly string _testProjectPath;
        private Repository _repository;

        public HasChangesTaskTests()
        {
            _testRepoRoot = Path.Combine(Path.GetTempPath(), "test-haschanges-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testRepoRoot);

            // Initialize git repository
            Repository.Init(_testRepoRoot);
            _repository = new Repository(_testRepoRoot);

            var projectDir = Path.Combine(_testRepoRoot, "src", "TestProject");
            Directory.CreateDirectory(projectDir);
            _testProjectPath = Path.Combine(projectDir, "TestProject.csproj");

            // Create a minimal project file
            File.WriteAllText(_testProjectPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>");

            // Create initial commit
            Commands.Stage(_repository, "*");
            var signature = new Signature("Test", "test@test.com", DateTimeOffset.Now);
            _repository.Commit("Initial commit", signature, signature);
        }

        [Fact]
        public void HasChangesTask_RequiredPropertiesValidation()
        {
            // Arrange
            var task = new HasChangesTask();
            var buildEngine = new MockBuildEngine();
            task.BuildEngine = buildEngine;

            // Act & Assert - Missing required properties should fail
            var result = task.Execute();
            Assert.False(result);
            Assert.True(buildEngine.Errors.Count > 0);
        }

        [Fact]
        public void HasChangesTask_DefaultPropertyValues()
        {
            // Arrange
            var task = new HasChangesTask();

            // Assert default values
            Assert.Equal("v", task.TagPrefix);
            Assert.False(task.Debug);
            Assert.False(task.ChangeDetectionEnabled);
            Assert.Null(task.SinceTag);
            Assert.Null(task.SinceCommit);
        }

        [Fact]
        public void HasChangesTask_DetectsChangesInNewProject()
        {
            // Arrange
            var task = new HasChangesTask
            {
                ProjectPath = _testProjectPath,
                RepoRoot = _testRepoRoot,
                BuildEngine = new MockBuildEngine()
            };

            // Act
            var result = task.Execute();

            // Assert
            Assert.True(result);
            Assert.True(task.HasChanges); // New project should have changes
            Assert.NotNull(task.ChangeType);
            Assert.NotEmpty(task.ChangeReason);
        }

        [Fact]
        public void HasChangesTask_NoChangesAfterTag()
        {
            // Arrange - Create a tag at current commit
            var signature = new Signature("Test", "test@test.com", DateTimeOffset.Now);
            _repository.ApplyTag("v1.0.0");

            var task = new HasChangesTask
            {
                ProjectPath = _testProjectPath,
                RepoRoot = _testRepoRoot,
                SinceTag = "v1.0.0",
                BuildEngine = new MockBuildEngine()
            };

            // Act
            var result = task.Execute();

            // Assert
            Assert.True(result);
            Assert.False(task.HasChanges); // No changes since tag
            Assert.Equal("None", task.ChangeType);
            Assert.Equal("v1.0.0", task.ComparedAgainst);
        }

        [Fact]
        public void HasChangesTask_DetectsChangesAfterTag()
        {
            // Arrange - Create a tag, then make changes
            var signature = new Signature("Test", "test@test.com", DateTimeOffset.Now);
            _repository.ApplyTag("v1.0.0");

            // Make a change
            var newFile = Path.Combine(Path.GetDirectoryName(_testProjectPath), "NewClass.cs");
            File.WriteAllText(newFile, "public class NewClass { }");
            Commands.Stage(_repository, "*");
            _repository.Commit("Add new class", signature, signature);

            var task = new HasChangesTask
            {
                ProjectPath = _testProjectPath,
                RepoRoot = _testRepoRoot,
                SinceTag = "v1.0.0",
                BuildEngine = new MockBuildEngine()
            };

            // Act
            var result = task.Execute();

            // Assert
            Assert.True(result);
            Assert.True(task.HasChanges);
            Assert.NotEqual("None", task.ChangeType);
            Assert.NotNull(task.ChangedFiles);
            Assert.True(task.ChangedFiles.Length > 0);
        }

        [Fact]
        public void HasChangesTask_DependenciesHandling()
        {
            // Arrange
            var dependencies = new[]
            {
                new TaskItem("../SharedLib/SharedLib.csproj"),
                new TaskItem("../Common/Common.csproj")
            };

            var task = new HasChangesTask
            {
                ProjectPath = _testProjectPath,
                RepoRoot = _testRepoRoot,
                Dependencies = dependencies,
                BuildEngine = new MockBuildEngine()
            };

            // Assert
            Assert.NotNull(task.Dependencies);
            Assert.Equal(2, task.Dependencies.Length);
        }

        [Fact]
        public void HasChangesTask_DetectsDependencyChanges()
        {
            // Arrange - Create dependency project
            var sharedLibDir = Path.Combine(_testRepoRoot, "src", "SharedLib");
            Directory.CreateDirectory(sharedLibDir);
            var sharedLibPath = Path.Combine(sharedLibDir, "SharedLib.csproj");
            File.WriteAllText(sharedLibPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>");

            var signature = new Signature("Test", "test@test.com", DateTimeOffset.Now);
            Commands.Stage(_repository, "*");
            _repository.Commit("Add shared lib", signature, signature);

            // Create a tag
            _repository.ApplyTag("v1.0.0");

            // Make a change to the dependency
            var depFile = Path.Combine(sharedLibDir, "Helper.cs");
            File.WriteAllText(depFile, "public class Helper { }");
            Commands.Stage(_repository, "*");
            _repository.Commit("Add helper to shared lib", signature, signature);

            var task = new HasChangesTask
            {
                ProjectPath = _testProjectPath,
                RepoRoot = _testRepoRoot,
                SinceTag = "v1.0.0",
                Dependencies = new[] { new TaskItem(sharedLibPath) as ITaskItem },
                BuildEngine = new MockBuildEngine()
            };

            // Act
            var result = task.Execute();

            // Assert
            Assert.True(result);
            Assert.True(task.HasChanges); // Dependency changed
        }

        [Fact]
        public void HasChangesTask_SinceCommitWorks()
        {
            // Arrange - Get the current commit SHA
            var signature = new Signature("Test", "test@test.com", DateTimeOffset.Now);
            var initialCommit = _repository.Head.Tip.Sha;

            // Make a change
            var newFile = Path.Combine(Path.GetDirectoryName(_testProjectPath), "Change.cs");
            File.WriteAllText(newFile, "public class Change { }");
            Commands.Stage(_repository, "*");
            _repository.Commit("Add change", signature, signature);

            var task = new HasChangesTask
            {
                ProjectPath = _testProjectPath,
                RepoRoot = _testRepoRoot,
                SinceCommit = initialCommit,
                BuildEngine = new MockBuildEngine()
            };

            // Act
            var result = task.Execute();

            // Assert
            Assert.True(result);
            Assert.True(task.HasChanges);
            Assert.NotNull(task.ComparedAgainst);
        }

        [Fact]
        public void HasChangesTask_ChangeDetectionClassification()
        {
            // Arrange - Create a tag, then make changes
            var signature = new Signature("Test", "test@test.com", DateTimeOffset.Now);
            _repository.ApplyTag("v1.0.0");

            // Make a change
            var newFile = Path.Combine(Path.GetDirectoryName(_testProjectPath), "Feature.cs");
            File.WriteAllText(newFile, "public class Feature { }");
            Commands.Stage(_repository, "*");
            _repository.Commit("Add feature", signature, signature);

            var task = new HasChangesTask
            {
                ProjectPath = _testProjectPath,
                RepoRoot = _testRepoRoot,
                SinceTag = "v1.0.0",
                ChangeDetectionEnabled = true,
                MinorFilePatterns = "**/*.cs",
                BuildEngine = new MockBuildEngine()
            };

            // Act
            var result = task.Execute();

            // Assert
            Assert.True(result);
            Assert.True(task.HasChanges);
            // With MinorFilePatterns matching *.cs, the change type should be Minor
            Assert.Equal("Minor", task.ChangeType);
        }

        [Fact]
        public void HasChangesTask_IgnorePatterns()
        {
            // Arrange - Create a tag, then make changes to ignored files
            var signature = new Signature("Test", "test@test.com", DateTimeOffset.Now);
            _repository.ApplyTag("v1.0.0");

            // Make a change to a file that will be ignored
            var docsDir = Path.Combine(Path.GetDirectoryName(_testProjectPath), "docs");
            Directory.CreateDirectory(docsDir);
            var docFile = Path.Combine(docsDir, "README.md");
            File.WriteAllText(docFile, "# Documentation");
            Commands.Stage(_repository, "*");
            _repository.Commit("Add docs", signature, signature);

            var task = new HasChangesTask
            {
                ProjectPath = _testProjectPath,
                RepoRoot = _testRepoRoot,
                SinceTag = "v1.0.0",
                ChangeDetectionEnabled = true,
                IgnoreFilePatterns = "**/docs/**;**/*.md",
                BuildEngine = new MockBuildEngine()
            };

            // Act
            var result = task.Execute();

            // Assert
            Assert.True(result);
            // Changes to docs should be ignored
            Assert.False(task.HasChanges);
            Assert.Equal("None", task.ChangeType);
        }

        [Fact]
        public void HasChangesTask_OutputPropertiesExist()
        {
            // Assert - Check that output properties have the correct attributes
            var hasChangesProperty = typeof(HasChangesTask).GetProperty("HasChanges");
            var changeTypeProperty = typeof(HasChangesTask).GetProperty("ChangeType");
            var changeReasonProperty = typeof(HasChangesTask).GetProperty("ChangeReason");
            var changedFilesProperty = typeof(HasChangesTask).GetProperty("ChangedFiles");
            var comparedAgainstProperty = typeof(HasChangesTask).GetProperty("ComparedAgainst");

            Assert.NotNull(hasChangesProperty);
            Assert.NotNull(changeTypeProperty);
            Assert.NotNull(changeReasonProperty);
            Assert.NotNull(changedFilesProperty);
            Assert.NotNull(comparedAgainstProperty);

            Assert.NotNull(hasChangesProperty.GetCustomAttributes(typeof(OutputAttribute), false).FirstOrDefault());
            Assert.NotNull(changeTypeProperty.GetCustomAttributes(typeof(OutputAttribute), false).FirstOrDefault());
            Assert.NotNull(changeReasonProperty.GetCustomAttributes(typeof(OutputAttribute), false).FirstOrDefault());
            Assert.NotNull(changedFilesProperty.GetCustomAttributes(typeof(OutputAttribute), false).FirstOrDefault());
            Assert.NotNull(comparedAgainstProperty.GetCustomAttributes(typeof(OutputAttribute), false).FirstOrDefault());
        }

        [Fact]
        public void HasChangesTask_ProjectSpecificTag()
        {
            // Arrange - Create a project-specific tag
            var projectName = Path.GetFileNameWithoutExtension(_testProjectPath);
            var tagName = $"{projectName.ToLowerInvariant()}-v1.0.0";
            var signature = new Signature("Test", "test@test.com", DateTimeOffset.Now);
            _repository.ApplyTag(tagName);

            var task = new HasChangesTask
            {
                ProjectPath = _testProjectPath,
                RepoRoot = _testRepoRoot,
                BuildEngine = new MockBuildEngine()
            };

            // Act
            var result = task.Execute();

            // Assert
            Assert.True(result);
            // Should find the project-specific tag and report no changes
            Assert.False(task.HasChanges);
        }

        public void Dispose()
        {
            try
            {
                _repository?.Dispose();
                if (Directory.Exists(_testRepoRoot))
                {
                    // Need to handle read-only files in .git directory
                    SetAttributesNormal(new DirectoryInfo(_testRepoRoot));
                    Directory.Delete(_testRepoRoot, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        private void SetAttributesNormal(DirectoryInfo dir)
        {
            foreach (var subDir in dir.GetDirectories())
            {
                SetAttributesNormal(subDir);
            }
            foreach (var file in dir.GetFiles())
            {
                file.Attributes = FileAttributes.Normal;
            }
        }
    }

    public class HasChangesServiceTests : IDisposable
    {
        private readonly string _testRepoRoot;
        private readonly string _testProjectPath;
        private Repository _repository;

        public HasChangesServiceTests()
        {
            _testRepoRoot = Path.Combine(Path.GetTempPath(), "test-haschanges-svc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testRepoRoot);

            // Initialize git repository
            Repository.Init(_testRepoRoot);
            _repository = new Repository(_testRepoRoot);

            var projectDir = Path.Combine(_testRepoRoot, "src", "TestProject");
            Directory.CreateDirectory(projectDir);
            _testProjectPath = Path.Combine(projectDir, "TestProject.csproj");

            // Create a minimal project file
            File.WriteAllText(_testProjectPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>");

            // Create initial commit
            Commands.Stage(_repository, "*");
            var signature = new Signature("Test", "test@test.com", DateTimeOffset.Now);
            _repository.Commit("Initial commit", signature, signature);
        }

        [Fact]
        public void HasChangesService_DetectChanges_NewProject()
        {
            // Arrange
            using var service = new HasChangesService(_testRepoRoot, (level, msg) => { });
            var request = new HasChangesRequest
            {
                ProjectPath = _testProjectPath,
                RepoRoot = _testRepoRoot,
                TagPrefix = "v"
            };

            // Act
            var result = service.DetectChanges(request);

            // Assert
            Assert.True(result.HasChanges);
            Assert.NotNull(result.Reason);
        }

        [Fact]
        public void HasChangesService_DetectChanges_NoChangesSinceTag()
        {
            // Arrange
            var signature = new Signature("Test", "test@test.com", DateTimeOffset.Now);
            _repository.ApplyTag("v1.0.0");

            using var service = new HasChangesService(_testRepoRoot, (level, msg) => { });
            var request = new HasChangesRequest
            {
                ProjectPath = _testProjectPath,
                RepoRoot = _testRepoRoot,
                TagPrefix = "v",
                SinceTag = "v1.0.0"
            };

            // Act
            var result = service.DetectChanges(request);

            // Assert
            Assert.False(result.HasChanges);
            Assert.Equal(VersionBumpType.None, result.ChangeType);
        }

        [Fact]
        public void HasChangesService_DetectChanges_WithClassification()
        {
            // Arrange
            var signature = new Signature("Test", "test@test.com", DateTimeOffset.Now);
            _repository.ApplyTag("v1.0.0");

            // Make a change
            var newFile = Path.Combine(Path.GetDirectoryName(_testProjectPath), "Service.cs");
            File.WriteAllText(newFile, "public class Service { }");
            Commands.Stage(_repository, "*");
            _repository.Commit("Add service", signature, signature);

            using var service = new HasChangesService(_testRepoRoot, (level, msg) => { });
            var request = new HasChangesRequest
            {
                ProjectPath = _testProjectPath,
                RepoRoot = _testRepoRoot,
                TagPrefix = "v",
                SinceTag = "v1.0.0",
                ChangeDetectionEnabled = true,
                MajorPatterns = new List<string> { "**/breaking/**" },
                MinorPatterns = new List<string> { "**/*.cs" },
                PatchPatterns = new List<string> { "**/*.txt" }
            };

            // Act
            var result = service.DetectChanges(request);

            // Assert
            Assert.True(result.HasChanges);
            Assert.Equal(VersionBumpType.Minor, result.ChangeType);
            Assert.NotEmpty(result.MinorFiles);
        }

        public void Dispose()
        {
            try
            {
                _repository?.Dispose();
                if (Directory.Exists(_testRepoRoot))
                {
                    SetAttributesNormal(new DirectoryInfo(_testRepoRoot));
                    Directory.Delete(_testRepoRoot, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        private void SetAttributesNormal(DirectoryInfo dir)
        {
            foreach (var subDir in dir.GetDirectories())
            {
                SetAttributesNormal(subDir);
            }
            foreach (var file in dir.GetFiles())
            {
                file.Attributes = FileAttributes.Normal;
            }
        }
    }
}
