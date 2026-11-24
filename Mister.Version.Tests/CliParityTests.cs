using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using LibGit2Sharp;
using Xunit;

namespace Mister.Version.Tests
{
    /// <summary>
    /// Runs the built mr-version CLI (Mister.Version.CLI/bin/&lt;Configuration&gt;/net8.0/mr-version.dll, built with the
    /// solution) against a temporary repository. Skipped when the CLI has not been built.
    /// </summary>
    public class CliParityTests : IDisposable
    {
        private readonly string _repoRoot;
        private readonly Repository _repo;
        private readonly Signature _sig = new Signature("Tester", "tester@example.com", DateTimeOffset.Now);

        public CliParityTests()
        {
            _repoRoot = Path.Combine(Path.GetTempPath(), "mrv-cli-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_repoRoot);
            Repository.Init(_repoRoot);
            _repo = new Repository(_repoRoot);

            Write("src/Lib/Lib.csproj", Project(""));
            Write("src/App/App.csproj", Project("<PropertyGroup><OutputType>Exe</OutputType></PropertyGroup><ItemGroup><ProjectReference Include=\"..\\Lib\\Lib.csproj\" /></ItemGroup>"));
            Write("src/TestHelpers/TestHelpers.csproj", Project("<ItemGroup><PackageReference Include=\"xunit\" Version=\"2.9.0\" /></ItemGroup>"));
            Write("tests/Lib.Tests/Lib.Tests.csproj", Project("<ItemGroup><PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.0.0\" /><ProjectReference Include=\"../../src/Lib/Lib.csproj\" /></ItemGroup>"));
            Write("src/Tool/Tool.csproj", Project(""));
            Write("src/Tool/Directory.Build.props", "<Project><PropertyGroup><IsPackable>false</IsPackable></PropertyGroup></Project>");
            Write("src/Lib/a.cs", "a");
            Write("src/App/b.cs", "b");
            Write("src/TestHelpers/t.cs", "t");
            Write("src/Tool/y.cs", "y");
            Write("mr-version.yml",
                "tagPrefix: rel-\n" +
                "commitConventions:\n" +
                "  enabled: true\n" +
                "changelog:\n" +
                "  includeAuthors: true\n" +
                "  includeCommitLinks: false\n");
            Commit("chore: initial");
            _repo.ApplyTag("rel-1.0.0");

            Write("src/Lib/a.cs", "a2");
            Commit("feat: add lib feature");
            Write("src/App/b.cs", "b2");
            Commit("fix: app bug");
            Write("shared/s.txt", "s");
            Commit("fix: shared helper");
        }

        public void Dispose()
        {
            _repo.Dispose();
            try
            {
                foreach (var file in Directory.GetFiles(_repoRoot, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                Directory.Delete(_repoRoot, true);
            }
            catch
            {
                // best effort
            }
        }

        private static string Project(string body) =>
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>" + body + "</Project>";

        private void Write(string relative, string content)
        {
            var path = Path.Combine(_repoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
        }

        private void Commit(string message)
        {
            Commands.Stage(_repo, "*");
            _repo.Commit(message, _sig, _sig);
        }

        private sealed class CliResult
        {
            public int ExitCode { get; set; }
            public string StdOut { get; set; }
            public string StdErr { get; set; }
        }

        private static string FindCli()
        {
            var baseDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            // .../Mister.Version.Tests/bin/<Configuration>/<tfm>
            var configuration = Path.GetFileName(Path.GetDirectoryName(baseDirectory));
            var directory = new DirectoryInfo(baseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Mister.Version.sln")))
            {
                directory = directory.Parent;
            }
            if (directory == null)
            {
                return null;
            }

            foreach (var config in new[] { configuration, "Release", "Debug" }.Distinct())
            {
                var candidate = Path.Combine(directory.FullName, "Mister.Version.CLI", "bin", config, "net8.0", "mr-version.dll");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            return null;
        }

        private static string FindDotnet()
        {
            var hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (!string.IsNullOrEmpty(hostPath) && File.Exists(hostPath))
            {
                return hostPath;
            }

            // <dotnet root>/shared/Microsoft.NETCore.App/<version>/
            var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(runtimeDirectory)));
            var exe = Path.Combine(root ?? "", RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "dotnet.exe" : "dotnet");
            return File.Exists(exe) ? exe : "dotnet";
        }

        private CliResult Run(params string[] args)
        {
            var cli = FindCli();
            Assert.SkipWhen(cli == null, "mr-version CLI has not been built (build the solution first)");

            var startInfo = new ProcessStartInfo(FindDotnet())
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = _repoRoot
            };
            startInfo.ArgumentList.Add(cli);
            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            using var process = Process.Start(startInfo);
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            Assert.True(process.WaitForExit(120_000), "mr-version did not exit");
            return new CliResult { ExitCode = process.ExitCode, StdOut = stdout.Result, StdErr = stderr.Result };
        }

        private JsonElement VersionJson(string project, params string[] extraArgs)
        {
            var result = Run(new[] { "version", "-r", _repoRoot, "-p", project, "-j" }.Concat(extraArgs).ToArray());
            Assert.True(result.ExitCode == 0, result.StdErr + result.StdOut);
            return JsonDocument.Parse(result.StdOut).RootElement;
        }

        private Dictionary<string, JsonElement> ReportProjects(params string[] extraArgs)
        {
            var result = Run(new[] { "report", "-r", _repoRoot, "-o", "json" }.Concat(extraArgs).ToArray());
            Assert.True(result.ExitCode == 0, result.StdErr + result.StdOut);
            return JsonDocument.Parse(result.StdOut).RootElement.GetProperty("projects").EnumerateArray()
                .ToDictionary(p => p.GetProperty("name").GetString(), p => p);
        }

        [Fact]
        public void VersionJson_CarriesTheFullCalculationDetails()
        {
            var json = VersionJson("src/Lib/Lib.csproj");

            Assert.Equal("1.1.0", json.GetProperty("version").GetString());
            Assert.Equal("Main", json.GetProperty("branchType").GetString());
            Assert.False(string.IsNullOrEmpty(json.GetProperty("branchName").GetString()));
            Assert.Equal(_repo.Head.Tip.Sha.Substring(0, 7), json.GetProperty("commitSha").GetString());
            Assert.Equal("1.0.0", json.GetProperty("previousVersion").GetString());

            var conventional = json.GetProperty("conventionalCommits");
            Assert.Equal(JsonValueKind.Object, conventional.ValueKind);
            Assert.Equal("Minor", conventional.GetProperty("bumpType").GetString());

            // Existing field names stay
            foreach (var name in new[] { "project", "path", "versionChanged", "changeReason", "commitDate", "commitMessage", "commitHeight", "isTestProject", "isPackable", "dependencies", "scheme", "calver", "validation" })
            {
                Assert.True(json.TryGetProperty(name, out _), name);
            }
        }

        [Fact]
        public void VersionDetailed_ShowsBranchAndPreviousVersion()
        {
            var result = Run("version", "-r", _repoRoot, "-p", "src/Lib/Lib.csproj", "-d");

            Assert.Equal(0, result.ExitCode);
            Assert.Matches(@"Branch: \S+ \(Main\)", result.StdOut);
            Assert.Contains("Previous Version: 1.0.0", result.StdOut);
            Assert.Contains("Conventional Commits Analysis:", result.StdOut);
        }

        [Fact]
        public void Report_UsesTheSameCalculationAsVersion_ForEveryProject()
        {
            var report = ReportProjects("--include-test-projects", "--include-non-packable");

            foreach (var project in new[] { "src/Lib/Lib.csproj", "src/App/App.csproj", "src/TestHelpers/TestHelpers.csproj", "tests/Lib.Tests/Lib.Tests.csproj", "src/Tool/Tool.csproj" })
            {
                var version = VersionJson(project);
                var name = version.GetProperty("project").GetString();
                Assert.True(report.ContainsKey(name), name);
                Assert.Equal(version.GetProperty("version").GetString(), report[name].GetProperty("version").GetProperty("version").GetString());
                Assert.Equal(version.GetProperty("isPackable").GetBoolean(), report[name].GetProperty("isPackable").GetBoolean());
                Assert.Equal(version.GetProperty("isTestProject").GetBoolean(), report[name].GetProperty("isTestProject").GetBoolean());
            }

            // mr-version.yml tagPrefix (rel-) is honoured: Lib changed since rel-1.0.0 with a feat commit
            Assert.Equal("1.1.0", report["Lib"].GetProperty("version").GetProperty("version").GetString());
            Assert.Equal("1.0.0", report["Lib"].GetProperty("version").GetProperty("previousVersion").GetString());

            // Heuristics as MSBuild evaluates them
            Assert.True(report["App"].GetProperty("isPackable").GetBoolean());          // console app
            Assert.False(report["TestHelpers"].GetProperty("isTestProject").GetBoolean()); // references xunit only
            Assert.True(report["Lib.Tests"].GetProperty("isTestProject").GetBoolean());   // Microsoft.NET.Test.Sdk
            Assert.False(report["Tool"].GetProperty("isPackable").GetBoolean());          // Directory.Build.props
        }

        [Fact]
        public void Report_BooleanOptions_AcceptExplicitValues()
        {
            var withCommits = ReportProjects("--include-commits", "true");
            var withoutCommits = ReportProjects("--include-commits", "false", "--include-dependencies", "false");

            Assert.Equal(JsonValueKind.String, withCommits["Lib"].GetProperty("version").GetProperty("commitSha").ValueKind);
            Assert.Equal(JsonValueKind.Null, withoutCommits["Lib"].GetProperty("version").GetProperty("commitSha").ValueKind);
            Assert.Equal(JsonValueKind.Null, withoutCommits["Lib"].GetProperty("dependencies").ValueKind);

            Assert.False(ReportProjects().ContainsKey("Tool"));
            Assert.True(ReportProjects("--include-non-packable").ContainsKey("Tool"));
            Assert.True(ReportProjects("--include-non-packable", "true").ContainsKey("Tool"));
            Assert.False(ReportProjects("--include-non-packable", "false").ContainsKey("Tool"));

            var changedOnlyFalse = Run("report", "-r", _repoRoot, "-o", "graph", "--graph-format", "ascii", "--include-non-packable", "--changed-only", "false");
            var changedOnly = Run("report", "-r", _repoRoot, "-o", "graph", "--graph-format", "ascii", "--include-non-packable", "--changed-only");
            Assert.Contains("Tool", changedOnlyFalse.StdOut);
            Assert.DoesNotContain("Tool", changedOnly.StdOut);
        }

        [Fact]
        public void TagPrefixOption_IsUsedWhenTheConfigurationSetsNone()
        {
            Write("no-prefix.yml", "commitConventions:\n  enabled: true\n");
            var config = Path.Combine(_repoRoot, "no-prefix.yml");

            // Default prefix "v": no version tag yet, so the initial base version
            Assert.Equal("0.1.0", ReportProjects("--config-file", config)["Lib"].GetProperty("version").GetProperty("version").GetString());
            Assert.Equal("0.1.0", VersionJson("src/Lib/Lib.csproj", "--config-file", config).GetProperty("version").GetString());

            Assert.Equal("1.0.0", ReportProjects("--config-file", config, "-t", "rel-")["Lib"].GetProperty("version").GetProperty("previousVersion").GetString());
            Assert.Equal("1.0.0", VersionJson("src/Lib/Lib.csproj", "--config-file", config, "-t", "rel-").GetProperty("previousVersion").GetString());
        }

        [Fact]
        public void Version_AcceptsDependenciesAndDryRun_FromTheCalculateAction()
        {
            var plain = VersionJson("src/Tool/Tool.csproj");
            Assert.False(plain.GetProperty("isPackable").GetBoolean());

            // TestHelpers did not change since the tag; a change in 'shared' counts once it is a dependency
            var before = VersionJson("src/TestHelpers/TestHelpers.csproj");
            Assert.Equal("1.0.0", before.GetProperty("version").GetString());

            var withDependency = VersionJson("src/TestHelpers/TestHelpers.csproj", "--dependencies", "shared;src/Missing/Missing.csproj", "--dry-run");
            Assert.Equal("1.0.1", withDependency.GetProperty("version").GetString());
            var dependencies = withDependency.GetProperty("dependencies").EnumerateArray().Select(d => d.GetString()).ToList();
            Assert.Contains(Path.Combine(_repoRoot, "shared"), dependencies);

            var commaSeparated = VersionJson("src/TestHelpers/TestHelpers.csproj", "--dependencies", "src/Nothing, shared");
            Assert.Equal("1.0.1", commaSeparated.GetProperty("version").GetString());
        }

        [Fact]
        public void Version_BranchOption_VersionsAsThatBranch()
        {
            var json = VersionJson("src/Lib/Lib.csproj", "-b", "feature/login");

            Assert.Equal("Feature", json.GetProperty("branchType").GetString());
            Assert.Equal("feature/login", json.GetProperty("branchName").GetString());
            Assert.Contains("-login.", json.GetProperty("version").GetString());
        }

        [Fact]
        public void Changelog_ProjectFiltersCommits_AndUsesTheCalculatedVersion()
        {
            var byName = Run("changelog", "-r", _repoRoot, "--project", "App");
            Assert.Equal(0, byName.ExitCode);
            Assert.Contains("## App v1.1.0", byName.StdOut);
            Assert.Contains("app bug", byName.StdOut);
            Assert.DoesNotContain("add lib feature", byName.StdOut);
            Assert.DoesNotContain("vHEAD", byName.StdOut);

            var byPath = Run("changelog", "-r", _repoRoot, "--project", "src/Lib/Lib.csproj");
            Assert.Equal(0, byPath.ExitCode);
            Assert.Contains("## Lib v1.1.0", byPath.StdOut);
            Assert.Contains("add lib feature", byPath.StdOut);
            Assert.DoesNotContain("app bug", byPath.StdOut);

            var repoWide = Run("changelog", "-r", _repoRoot);
            Assert.Contains("## Unreleased", repoWide.StdOut);
            Assert.Contains("add lib feature", repoWide.StdOut);
            Assert.Contains("app bug", repoWide.StdOut);
        }

        [Fact]
        public void Changelog_YamlSettings_ApplyUnlessAFlagIsGiven()
        {
            var sha = _repo.Commits.First(c => c.MessageShort == "fix: app bug").Sha.Substring(0, 7);

            // mr-version.yml: includeAuthors true, includeCommitLinks false
            var fromYaml = Run("changelog", "-r", _repoRoot, "--project", "App");
            Assert.Contains("@Tester", fromYaml.StdOut);
            Assert.DoesNotContain(sha, fromYaml.StdOut);

            var flags = Run("changelog", "-r", _repoRoot, "--project", "App", "--include-authors", "false", "--include-commits");
            Assert.DoesNotContain("@Tester", flags.StdOut);
            Assert.Contains(sha, flags.StdOut);
        }

        [Fact]
        public void ParseErrors_GoToStderr_HelpToStdout()
        {
            var error = Run("version", "--no-such-option");
            Assert.Equal(1, error.ExitCode);
            Assert.Equal("", error.StdOut.Trim());
            Assert.Contains("no-such-option", error.StdErr);

            var help = Run("version", "--help");
            Assert.Equal(0, help.ExitCode);
            Assert.Contains("--dependencies", help.StdOut);
            Assert.Equal("", help.StdErr.Trim());
        }
    }
}
