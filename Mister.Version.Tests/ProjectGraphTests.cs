using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;
using Xunit;

namespace Mister.Version.Tests
{
    /// <summary>
    /// Report dependency graphs with duplicate project names (same file name in different directories) and large,
    /// diamond-heavy graphs.
    /// </summary>
    public class ProjectGraphTests
    {
        private static readonly string Root = Path.Combine(Path.GetTempPath(), "mrv-graph-repo");

        private static ProjectInfo Project(string relativePath, string version, params string[] dependencyPaths)
        {
            var full = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return new ProjectInfo
            {
                Name = Path.GetFileNameWithoutExtension(relativePath),
                Path = relativePath,
                FullPath = full,
                Version = new VersionResult { Version = version },
                DirectDependencies = dependencyPaths.Select(Path.GetFileNameWithoutExtension).ToList(),
                DirectDependencyPaths = dependencyPaths.Select(d => Path.Combine(Root, d.Replace('/', Path.DirectorySeparatorChar))).ToList(),
                IsPackable = true
            };
        }

        /// <summary>a/Utils (1.0.0) and b/Utils (2.0.0); AppA -> a/Utils, AppB -> b/Utils.</summary>
        private static List<ProjectInfo> DuplicateNames() => new List<ProjectInfo>
        {
            Project("src/a/Utils/Utils.csproj", "1.0.0"),
            Project("src/b/Utils/Utils.csproj", "2.0.0"),
            Project("src/AppA/AppA.csproj", "1.0.0", "src/a/Utils/Utils.csproj"),
            Project("src/AppB/AppB.csproj", "2.0.0", "src/b/Utils/Utils.csproj"),
        };

        private static VersionReport Report(List<ProjectInfo> projects) =>
            new VersionReport { Repository = Root, Branch = "main", BranchType = BranchType.Main, Projects = projects };

        [Fact]
        public void BuildDependencyGraph_AllowsDuplicateProjectNames()
        {
            var projects = DuplicateNames();
            new ProjectAnalyzer(null, null).BuildDependencyGraph(projects);
            Assert.Equal(new[] { "Utils" }, projects[2].AllDependencies);
            Assert.Equal(new[] { "Utils" }, projects[3].AllDependencies);
        }

        [Fact]
        public void JsonReport_ResolvesDependencyVersionsByPath()
        {
            var projects = DuplicateNames();
            new ProjectAnalyzer(null, null).BuildDependencyGraph(projects);
            var json = new ReportGenerator().GenerateJsonReport(Report(projects), new ReportOptions
            {
                IncludeDependencies = true,
                IncludeTestProjects = true,
                IncludeNonPackableProjects = true
            });

            using var doc = JsonDocument.Parse(json);
            string DepVersion(string app) => doc.RootElement.GetProperty("projects").EnumerateArray()
                .Single(p => p.GetProperty("name").GetString() == app)
                .GetProperty("dependencies").GetProperty("direct")[0].GetProperty("version").GetString();

            Assert.Equal("1.0.0", DepVersion("AppA"));
            Assert.Equal("2.0.0", DepVersion("AppB"));
            Assert.Equal(2, doc.RootElement.GetProperty("projects").EnumerateArray().Count(p => p.GetProperty("name").GetString() == "Utils"));
        }

        [Theory]
        [InlineData("mermaid")]
        [InlineData("dot")]
        [InlineData("ascii")]
        public void Graph_KeepsDuplicateNamesAsSeparateNodes(string format)
        {
            var output = new ReportGenerator().GenerateDependencyGraph(Report(DuplicateNames()), new ReportOptions
            {
                GraphFormat = format,
                ShowVersions = true,
                IncludeTestProjects = true,
                IncludeNonPackableProjects = true
            }).Replace("\r\n", "\n");

            Assert.Contains("Utils (src/a/Utils)", output);
            Assert.Contains("Utils (src/b/Utils)", output);
            switch (format)
            {
                case "mermaid":
                    Assert.Contains("Utils__src_a_Utils_ -->|1.0.0| AppA", output);
                    Assert.Contains("Utils__src_b_Utils_ -->|2.0.0| AppB", output);
                    break;
                case "dot":
                    Assert.Contains("Utils__src_a_Utils_ -> AppA [label=\"1.0.0\"];", output);
                    Assert.Contains("Utils__src_b_Utils_ -> AppB [label=\"2.0.0\"];", output);
                    break;
                default:
                    Assert.DoesNotContain("Circular", output);
                    var lines = output.Split('\n');
                    var a = Array.FindIndex(lines, l => l.Contains("Utils (src/a/Utils)"));
                    var b = Array.FindIndex(lines, l => l.Contains("Utils (src/b/Utils)"));
                    Assert.Contains("AppA", lines[a + 1]);
                    Assert.Contains("AppB", lines[b + 1]);
                    break;
            }
        }

        [Fact]
        public void ProjectsWithoutPaths_StillResolveByName()
        {
            var core = new ProjectInfo { Name = "Core", Path = "src/Core/Core.csproj", Version = new VersionResult { Version = "1.0.0" } };
            var lib = new ProjectInfo { Name = "Lib", Path = "src/Lib/Lib.csproj", DirectDependencies = new List<string> { "Core" } };
            var app = new ProjectInfo { Name = "App", Path = "src/App/App.csproj", DirectDependencies = new List<string> { "Lib", "External" } };
            var projects = new List<ProjectInfo> { core, lib, app };
            new ProjectAnalyzer(null, null).BuildDependencyGraph(projects);
            Assert.Equal(new[] { "Lib", "External", "Core" }, app.AllDependencies);
        }

        [Fact]
        public void DependencyCycle_DoesNotHang()
        {
            var projects = new List<ProjectInfo>
            {
                Project("src/A/A.csproj", "1.0.0", "src/B/B.csproj"),
                Project("src/B/B.csproj", "1.0.0", "src/C/C.csproj"),
                Project("src/C/C.csproj", "1.0.0", "src/A/A.csproj"),
            };
            new ProjectAnalyzer(null, null).BuildDependencyGraph(projects);
            Assert.Equal(new[] { "B", "C" }, projects[0].AllDependencies);
            var ascii = new ReportGenerator().GenerateDependencyGraph(Report(projects), new ReportOptions { GraphFormat = "ascii", IncludeTestProjects = true, IncludeNonPackableProjects = true });
            Assert.Contains("Circular", ascii);
        }

        [Fact]
        public void DiamondLadder_IsResolvedInLinearTime()
        {
            // 40 levels where every level has two projects that both reference both projects of the level below:
            // 2^40 paths from the top to the bottom. A path-enumerating walk never finishes.
            var projects = new List<ProjectInfo> { Project("src/L0a/L0a.csproj", "1.0.0"), Project("src/L0b/L0b.csproj", "1.0.0") };
            for (var level = 1; level < 40; level++)
            {
                var below = new[] { $"src/L{level - 1}a/L{level - 1}a.csproj", $"src/L{level - 1}b/L{level - 1}b.csproj" };
                projects.Add(Project($"src/L{level}a/L{level}a.csproj", "1.0.0", below));
                projects.Add(Project($"src/L{level}b/L{level}b.csproj", "1.0.0", below));
            }

            var stopwatch = Stopwatch.StartNew();
            new ProjectAnalyzer(null, null).BuildDependencyGraph(projects);
            var report = new ReportGenerator();
            foreach (var format in new[] { "mermaid", "dot", "ascii" })
                report.GenerateDependencyGraph(Report(projects), new ReportOptions { GraphFormat = format, IncludeTestProjects = true, IncludeNonPackableProjects = true });
            stopwatch.Stop();

            Assert.Equal(78, projects[^1].AllDependencies.Count);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"took {stopwatch.Elapsed}");
        }
    }
}
