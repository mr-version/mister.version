using System.Collections.Generic;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;
using Xunit;

namespace Mister.Version.Tests
{
    /// <summary>
    /// Golden output for `mr-version report -o graph`: valid DOT and Mermaid edge syntax, and an ASCII tree rooted
    /// at the projects that have no dependencies.
    /// </summary>
    public class ReportGraphGoldenTests
    {
        private static ProjectInfo Project(string name, string version, bool changed, bool test = false, bool packable = true, params string[] dependencies) =>
            new ProjectInfo
            {
                Name = name,
                Path = $"src/{name}/{name}.csproj",
                Version = new VersionResult { Version = version, VersionChanged = changed },
                DirectDependencies = new List<string>(dependencies),
                IsTestProject = test,
                IsPackable = packable
            };

        /// <summary>Core &lt;- Lib &lt;- App, App -> Core as well (diamond), Lib.Tests -> Lib, Tool non-packable.</summary>
        private static VersionReport CreateReport() => new VersionReport
        {
            Repository = "/repo",
            Branch = "main",
            BranchType = BranchType.Main,
            Projects = new List<ProjectInfo>
            {
                Project("App", "2.1.0", changed: true, dependencies: new[] { "Lib", "Core" }),
                Project("Core", "1.0.0", changed: false),
                Project("Lib", "2.1.0", changed: true, dependencies: new[] { "Core" }),
                Project("Lib.Tests", "1.0.0", changed: false, test: true, packable: false, dependencies: new[] { "Lib" }),
                Project("Tool", "1.0.0", changed: false, packable: false)
            }
        };

        private static string Graph(string format, bool showVersions = true, bool includeTests = true, bool includeNonPackable = true, bool changedOnly = false) =>
            new ReportGenerator().GenerateDependencyGraph(CreateReport(), new ReportOptions
            {
                OutputFormat = "graph",
                GraphFormat = format,
                ShowVersions = showVersions,
                IncludeTestProjects = includeTests,
                IncludeNonPackableProjects = includeNonPackable,
                ShowChangedOnly = changedOnly
            }).Replace("\r\n", "\n");

        [Fact]
        public void Dot_Golden()
        {
            const string expected =
@"digraph MonoRepoDependencies {
    rankdir=TD;
    node [shape=box, style=filled];
    edge [color=gray];

    App [label=""App\n2.1.0"", fillcolor=""#ff9999"", color=""#ff0000""];
    Core [label=""Core\n1.0.0"", fillcolor=""#ccffcc"", color=""#00aa00""];
    Lib [label=""Lib\n2.1.0"", fillcolor=""#ff9999"", color=""#ff0000""];
    Lib_Tests [label=""Lib.Tests\n1.0.0"", fillcolor=""#ccccff"", color=""#0000ff""];
    Tool [label=""Tool\n1.0.0"", fillcolor=""#f9f9f9"", color=""#333""];

    Lib -> App [label=""2.1.0""];
    Core -> App [label=""1.0.0""];
    Core -> Lib [label=""1.0.0""];
    Lib -> Lib_Tests [label=""2.1.0""];
}
";
            Assert.Equal(expected.Replace("\r\n", "\n"), Graph("dot"));
        }

        [Fact]
        public void Dot_WithoutVersions_HasNoEdgeAttributes()
        {
            var dot = Graph("dot", showVersions: false);

            Assert.Contains("    Core -> Lib;\n", dot);
            Assert.Contains("    Lib [label=\"Lib\"", dot);
            Assert.DoesNotContain("[,", dot);
            Assert.DoesNotContain("[]", dot);
        }

        [Fact]
        public void Mermaid_Golden()
        {
            const string expected =
@"```mermaid
graph TD
    %% MonoRepo Dependency Graph with Versions

    App[""App<br/>2.1.0""]
    class App changed;
    Core[""Core<br/>1.0.0""]
    class Core packable;
    Lib[""Lib<br/>2.1.0""]
    class Lib changed;
    Lib_Tests[""Lib.Tests<br/>1.0.0""]
    class Lib_Tests test;
    Tool[""Tool<br/>1.0.0""]
    class Tool normal;

    Lib -->|2.1.0| App
    Core -->|1.0.0| App
    Core -->|1.0.0| Lib
    Lib -->|2.1.0| Lib_Tests

    classDef changed fill:#ff9999,stroke:#ff0000,stroke-width:2px,color:#000;
    classDef test fill:#ccccff,stroke:#0000ff,stroke-width:1px,color:#000;
    classDef packable fill:#ccffcc,stroke:#00aa00,stroke-width:1px,color:#000;
    classDef normal fill:#f9f9f9,stroke:#333,stroke-width:1px,color:#000;
```
";
            Assert.Equal(expected.Replace("\r\n", "\n"), Graph("mermaid"));
        }

        [Fact]
        public void Mermaid_WithoutVersions_HasPlainEdges()
        {
            var mermaid = Graph("mermaid", showVersions: false);

            Assert.Contains("    Core --> Lib\n", mermaid);
            Assert.DoesNotContain("|", mermaid);
        }

        [Fact]
        public void Ascii_Golden_RootsAreProjectsWithoutDependencies()
        {
            const string expected =
@"=== MonoRepo Dependency Graph ===

📦 Core (1.0.0)
  🔄 App (2.1.0) [CHANGED]
  🔄 Lib (2.1.0) [CHANGED]
    🔄 App (2.1.0) [CHANGED] (see above)
    🧪 Lib.Tests (1.0.0)
📁 Tool (1.0.0)
";
            Assert.Equal(expected.Replace("\r\n", "\n"), Graph("ascii"));
        }

        [Fact]
        public void Ascii_ChangedOnly_TreatsProjectsWhoseDependenciesAreHiddenAsRoots()
        {
            const string expected =
@"=== MonoRepo Dependency Graph ===

🔄 Lib (2.1.0) [CHANGED]
  🔄 App (2.1.0) [CHANGED]
";
            Assert.Equal(expected.Replace("\r\n", "\n"), Graph("ascii", changedOnly: true));
        }

        [Fact]
        public void Ascii_Cycle_IsListedSeparately()
        {
            var report = new VersionReport
            {
                Projects = new List<ProjectInfo>
                {
                    Project("Base", "1.0.0", changed: false),
                    Project("A", "1.0.0", changed: false, dependencies: new[] { "B" }),
                    Project("B", "1.0.0", changed: false, dependencies: new[] { "A" })
                }
            };

            var ascii = new ReportGenerator().GenerateDependencyGraph(report, new ReportOptions { GraphFormat = "ascii" }).Replace("\r\n", "\n");

            const string expected =
@"=== MonoRepo Dependency Graph ===

📦 Base (1.0.0)

=== Circular Dependencies ===
📦 A (1.0.0)
  📦 B (1.0.0)
    📦 A (1.0.0) (circular)
";
            Assert.Equal(expected.Replace("\r\n", "\n"), ascii);
        }

        [Fact]
        public void NodeIdsStartingWithADigit_AreValidIdentifiers()
        {
            var report = new VersionReport
            {
                Projects = new List<ProjectInfo>
                {
                    Project("3rdParty.Wrapper", "1.0.0", changed: false),
                    Project("App", "1.0.0", changed: false, dependencies: new[] { "3rdParty.Wrapper" })
                }
            };

            var dot = new ReportGenerator().GenerateDependencyGraph(report, new ReportOptions { GraphFormat = "dot" });

            Assert.Contains("_3rdParty_Wrapper -> App [label=\"1.0.0\"];", dot);
        }

        [Fact]
        public void TestProjects_AreShownWhenIncluded_EvenThoughTheyAreNotPackable()
        {
            var withTests = Graph("ascii", includeTests: true, includeNonPackable: false);
            var withoutTests = Graph("ascii", includeTests: false, includeNonPackable: false);

            Assert.Contains("Lib.Tests", withTests);
            Assert.DoesNotContain("Tool", withTests);
            Assert.DoesNotContain("Lib.Tests", withoutTests);
        }
    }
}
