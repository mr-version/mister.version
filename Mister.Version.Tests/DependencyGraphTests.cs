using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;

namespace Mister.Version.Tests
{
    /// <summary>
    /// Tests for dependency graph generation functionality
    /// </summary>
    public class DependencyGraphTests
    {
        private readonly ReportGenerator _reportGenerator;

        public DependencyGraphTests()
        {
            _reportGenerator = new ReportGenerator();
        }

        [Fact]
        public void GenerateDependencyGraph_MermaidFormat_GeneratesValidMermaidSyntax()
        {
            // Arrange
            var report = CreateTestReport();
            var options = new ReportOptions
            {
                OutputFormat = "graph",
                GraphFormat = "mermaid",
                ShowVersions = true,
                ShowChangedOnly = false
            };

            // Act
            var result = _reportGenerator.GenerateDependencyGraph(report, options);

            // Assert
            Assert.StartsWith("```mermaid", result);
            Assert.EndsWith("```", result.TrimEnd());
            Assert.Contains("graph TD", result);
            Assert.Contains("MonoRepo Dependency Graph", result);
            Assert.Contains("classDef changed", result);
            Assert.Contains("classDef test", result);
            Assert.Contains("classDef packable", result);
        }

        [Fact]
        public void GenerateDependencyGraph_DotFormat_GeneratesValidDotSyntax()
        {
            // Arrange
            var report = CreateTestReport();
            var options = new ReportOptions
            {
                OutputFormat = "graph",
                GraphFormat = "dot",
                ShowVersions = true,
                ShowChangedOnly = false
            };

            // Act
            var result = _reportGenerator.GenerateDependencyGraph(report, options);

            // Assert
            Assert.StartsWith("digraph MonoRepoDependencies {", result);
            Assert.EndsWith("}", result.TrimEnd());
            Assert.Contains("rankdir=TD;", result);
            Assert.Contains("node [shape=box, style=filled];", result);
            Assert.Contains("edge [color=gray];", result);
        }

        [Fact]
        public void GenerateDependencyGraph_AsciiFormat_GeneratesTextTree()
        {
            // Arrange
            var report = CreateTestReport();
            var options = new ReportOptions
            {
                OutputFormat = "graph",
                GraphFormat = "ascii",
                ShowVersions = true,
                ShowChangedOnly = false,
                IncludeTestProjects = true,
                IncludeNonPackableProjects = true
            };

            // Act
            var result = _reportGenerator.GenerateDependencyGraph(report, options);

            // Assert
            Assert.StartsWith("=== MonoRepo Dependency Graph ===", result);
            Assert.Contains("🔄", result); // Changed project symbol (ProjectA)
            Assert.Contains("🧪", result); // Test project symbol (ProjectB)
        }

        [Fact]
        public void GenerateDependencyGraph_WithVersions_IncludesVersionInformation()
        {
            // Arrange
            var report = CreateTestReport();
            var options = new ReportOptions
            {
                OutputFormat = "graph",
                GraphFormat = "mermaid",
                ShowVersions = true,
                ShowChangedOnly = false,
                IncludeTestProjects = true,
                IncludeNonPackableProjects = true
            };

            // Act
            var result = _reportGenerator.GenerateDependencyGraph(report, options);

            // Assert
            Assert.Contains("ProjectA<br/>1.2.3", result);
            Assert.Contains("ProjectB<br/>2.0.1", result);
        }

        [Fact]
        public void GenerateDependencyGraph_WithoutVersions_ExcludesVersionInformation()
        {
            // Arrange
            var report = CreateTestReport();
            var options = new ReportOptions
            {
                OutputFormat = "graph",
                GraphFormat = "mermaid",
                ShowVersions = false,
                ShowChangedOnly = false,
                IncludeTestProjects = true,
                IncludeNonPackableProjects = true
            };

            // Act
            var result = _reportGenerator.GenerateDependencyGraph(report, options);

            // Assert
            Assert.DoesNotContain("1.2.3", result);
            Assert.DoesNotContain("2.0.1", result);
            Assert.Contains("ProjectA", result);
            Assert.Contains("ProjectB", result);
        }

        [Fact]
        public void GenerateDependencyGraph_ChangedOnly_ShowsOnlyChangedProjects()
        {
            // Arrange
            var report = CreateTestReport();
            var options = new ReportOptions
            {
                OutputFormat = "graph",
                GraphFormat = "mermaid",
                ShowVersions = true,
                ShowChangedOnly = true,
                IncludeTestProjects = true,
                IncludeNonPackableProjects = true
            };

            // Act
            var result = _reportGenerator.GenerateDependencyGraph(report, options);

            // Assert
            Assert.Contains("ProjectA", result); // Changed project
            Assert.DoesNotContain("ProjectB", result); // Unchanged project
        }

        [Fact]
        public void GenerateDependencyGraph_MermaidDependencies_CreatesCorrectEdges()
        {
            // Arrange
            var report = CreateTestReportWithDependencies();
            var options = new ReportOptions
            {
                OutputFormat = "graph",
                GraphFormat = "mermaid",
                ShowVersions = false,
                ShowChangedOnly = false,
                IncludeTestProjects = true,
                IncludeNonPackableProjects = true
            };

            // Act
            var result = _reportGenerator.GenerateDependencyGraph(report, options);

            // Assert
            Assert.Contains("Core --> ProjectA", result);
            Assert.Contains("ProjectA --> ProjectB", result);
        }

        [Fact]
        public void GenerateDependencyGraph_DotDependencies_CreatesCorrectEdges()
        {
            // Arrange
            var report = CreateTestReportWithDependencies();
            var options = new ReportOptions
            {
                OutputFormat = "graph",
                GraphFormat = "dot",
                ShowVersions = true,
                ShowChangedOnly = false,
                IncludeTestProjects = true,
                IncludeNonPackableProjects = true
            };

            // Act
            var result = _reportGenerator.GenerateDependencyGraph(report, options);

            // Assert
            Assert.Contains("Core -> ProjectA", result);
            Assert.Contains("ProjectA -> ProjectB", result);
            Assert.Contains("label=\"1.0.0\"", result); // Version labels on edges
        }

        [Fact]
        public void GenerateDependencyGraph_ProjectTypeStyles_AppliesCorrectStyling()
        {
            // Arrange
            var report = CreateTestReportWithVariousProjectTypes();
            var options = new ReportOptions
            {
                OutputFormat = "graph",
                GraphFormat = "mermaid",
                ShowVersions = false,
                ShowChangedOnly = false,
                IncludeTestProjects = true,
                IncludeNonPackableProjects = true
            };

            // Act
            var result = _reportGenerator.GenerateDependencyGraph(report, options);

            // Assert
            Assert.Contains("class TestProject test;", result);
            Assert.Contains("class PackableProject packable;", result);
            Assert.Contains("class ChangedProject changed;", result);
        }

        [Fact]
        public void GenerateDependencyGraph_EmptyReport_HandlesGracefully()
        {
            // Arrange
            var report = new VersionReport
            {
                Repository = "test-repo",
                Branch = "main",
                BranchType = BranchType.Main,
                GeneratedAt = DateTime.UtcNow,
                Projects = new List<ProjectInfo>()
            };
            var options = new ReportOptions
            {
                OutputFormat = "graph",
                GraphFormat = "mermaid",
                ShowVersions = true,
                ShowChangedOnly = false,
                IncludeTestProjects = true,
                IncludeNonPackableProjects = true
            };

            // Act
            var result = _reportGenerator.GenerateDependencyGraph(report, options);

            // Assert
            Assert.StartsWith("```mermaid", result);
            Assert.Contains("graph TD", result);
            Assert.EndsWith("```", result.TrimEnd());
        }

        [Theory]
        [InlineData("mermaid")]
        [InlineData("dot")]
        [InlineData("ascii")]
        public void GenerateDependencyGraph_AllFormats_ProducesNonEmptyOutput(string format)
        {
            // Arrange
            var report = CreateTestReport();
            var options = new ReportOptions
            {
                OutputFormat = "graph",
                GraphFormat = format,
                ShowVersions = true,
                ShowChangedOnly = false,
                IncludeTestProjects = true,
                IncludeNonPackableProjects = true
            };

            // Act
            var result = _reportGenerator.GenerateDependencyGraph(report, options);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
            Assert.True(result.Length > 50); // Should have substantial content
        }

        [Fact]
        public void GenerateDependencyGraph_SpecialCharactersInProjectNames_SanitizesCorrectly()
        {
            // Arrange
            var report = new VersionReport
            {
                Repository = "test-repo",
                Branch = "main",
                BranchType = BranchType.Main,
                GeneratedAt = DateTime.UtcNow,
                Projects = new List<ProjectInfo>
                {
                    new ProjectInfo
                    {
                        Name = "Project-With.Special@Characters",
                        Path = "src/special",
                        Version = new VersionResult { Version = "1.0.0", VersionChanged = false },
                        DirectDependencies = new List<string>(),
                        AllDependencies = new List<string>(),
                        IsTestProject = false,
                        IsPackable = true
                    }
                }
            };
            var options = new ReportOptions
            {
                OutputFormat = "graph",
                GraphFormat = "mermaid",
                ShowVersions = false,
                ShowChangedOnly = false,
                IncludeTestProjects = true,
                IncludeNonPackableProjects = true
            };

            // Act
            var result = _reportGenerator.GenerateDependencyGraph(report, options);

            // Assert
            Assert.Contains("Project_With_Special_Characters", result); // Sanitized node ID
            Assert.Contains("Project-With.Special@Characters", result); // Original name in label
        }

        #region Helper Methods

        private VersionReport CreateTestReport()
        {
            return new VersionReport
            {
                Repository = "test-repo",
                Branch = "main",
                BranchType = BranchType.Main,
                GeneratedAt = DateTime.UtcNow,
                Projects = new List<ProjectInfo>
                {
                    new ProjectInfo
                    {
                        Name = "ProjectA",
                        Path = "src/ProjectA",
                        Version = new VersionResult { Version = "1.2.3", VersionChanged = true },
                        DirectDependencies = new List<string>(),
                        AllDependencies = new List<string>(),
                        IsTestProject = false,
                        IsPackable = true
                    },
                    new ProjectInfo
                    {
                        Name = "ProjectB",
                        Path = "src/ProjectB",
                        Version = new VersionResult { Version = "2.0.1", VersionChanged = false },
                        DirectDependencies = new List<string>(),
                        AllDependencies = new List<string>(),
                        IsTestProject = true,
                        IsPackable = false
                    }
                }
            };
        }

        private VersionReport CreateTestReportWithDependencies()
        {
            return new VersionReport
            {
                Repository = "test-repo",
                Branch = "main",
                BranchType = BranchType.Main,
                GeneratedAt = DateTime.UtcNow,
                Projects = new List<ProjectInfo>
                {
                    new ProjectInfo
                    {
                        Name = "Core",
                        Path = "src/Core",
                        Version = new VersionResult { Version = "1.0.0", VersionChanged = false },
                        DirectDependencies = new List<string>(),
                        AllDependencies = new List<string>(),
                        IsTestProject = false,
                        IsPackable = true
                    },
                    new ProjectInfo
                    {
                        Name = "ProjectA",
                        Path = "src/ProjectA",
                        Version = new VersionResult { Version = "1.2.3", VersionChanged = true },
                        DirectDependencies = new List<string> { "Core" },
                        AllDependencies = new List<string> { "Core" },
                        IsTestProject = false,
                        IsPackable = true
                    },
                    new ProjectInfo
                    {
                        Name = "ProjectB",
                        Path = "src/ProjectB",
                        Version = new VersionResult { Version = "2.0.1", VersionChanged = false },
                        DirectDependencies = new List<string> { "ProjectA" },
                        AllDependencies = new List<string> { "ProjectA", "Core" },
                        IsTestProject = false,
                        IsPackable = true
                    }
                }
            };
        }

        private VersionReport CreateTestReportWithVariousProjectTypes()
        {
            return new VersionReport
            {
                Repository = "test-repo",
                Branch = "main",
                BranchType = BranchType.Main,
                GeneratedAt = DateTime.UtcNow,
                Projects = new List<ProjectInfo>
                {
                    new ProjectInfo
                    {
                        Name = "TestProject",
                        Path = "test/TestProject",
                        Version = new VersionResult { Version = "1.0.0", VersionChanged = false },
                        DirectDependencies = new List<string>(),
                        AllDependencies = new List<string>(),
                        IsTestProject = true,
                        IsPackable = false
                    },
                    new ProjectInfo
                    {
                        Name = "PackableProject",
                        Path = "src/PackableProject",
                        Version = new VersionResult { Version = "2.0.0", VersionChanged = false },
                        DirectDependencies = new List<string>(),
                        AllDependencies = new List<string>(),
                        IsTestProject = false,
                        IsPackable = true
                    },
                    new ProjectInfo
                    {
                        Name = "ChangedProject",
                        Path = "src/ChangedProject",
                        Version = new VersionResult { Version = "3.0.0", VersionChanged = true },
                        DirectDependencies = new List<string>(),
                        AllDependencies = new List<string>(),
                        IsTestProject = false,
                        IsPackable = false
                    }
                }
            };
        }

        #endregion
    }
}