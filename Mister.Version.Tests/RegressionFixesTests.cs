using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using LibGit2Sharp;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;
using Xunit;

namespace Mister.Version.Tests
{
    /// <summary>
    /// Regression tests for fixes found by the cross-platform / correctness audit.
    /// </summary>
    [Collection("VersionResultCache")]
    public class RegressionFixesTests : IDisposable
    {
        private readonly string _root;
        private readonly Signature _sig = new Signature("Test", "test@example.com", DateTimeOffset.Now);

        public RegressionFixesTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "mrv-regressions-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            MonoRepoVersionTask.ClearCache();
        }

        public void Dispose()
        {
            MonoRepoVersionTask.ClearCache();
            try
            {
                foreach (var f in Directory.GetFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(_root, true);
            }
            catch { }
        }

        private void Write(string relative, string content)
        {
            var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
        }

        private Repository InitRepo()
        {
            Repository.Init(_root);
            return new Repository(_root);
        }

        private void Commit(Repository repo, string message)
        {
            Commands.Stage(repo, "*");
            repo.Commit(message, _sig, _sig);
        }

        // ------------------------------------------------------------------ CalVer ISO weeks

        [Theory]
        [InlineData("2024-12-23", 2024, 52)]
        [InlineData("2024-12-30", 2025, 1)]   // Monday of ISO week 1 of 2025
        [InlineData("2026-12-31", 2026, 53)]
        [InlineData("2027-01-01", 2026, 53)]  // still ISO week 53 of 2026
        [InlineData("2027-01-04", 2027, 1)]
        [InlineData("2025-06-15", 2025, 24)]
        public void CalVer_WeekFormat_UsesIsoWeekNumberingYear(string date, int expectedYear, int expectedWeek)
        {
            var version = new CalVerCalculator().CalculateVersion(
                new CalVerConfig { Format = "YYYY.WW.PATCH" },
                DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture));

            Assert.Equal(expectedYear, version.Major);
            Assert.Equal(expectedWeek, version.Minor);
        }

        [Fact]
        public void CalVer_WeekFormat_NeverGoesBackwardsAcrossNewYear()
        {
            var calculator = new CalVerCalculator();
            var previous = (0, 0);
            for (var day = new DateTime(2024, 12, 1); day < new DateTime(2027, 2, 1); day = day.AddDays(1))
            {
                var v = calculator.CalculateVersion(new CalVerConfig { Format = "YYYY.WW.PATCH" }, day);
                var current = (v.Major, v.Minor);
                Assert.True(current.CompareTo(previous) >= 0, $"{day:yyyy-MM-dd}: {current} < {previous}");
                previous = current;
            }
        }

        // ------------------------------------------------------------------ glob patterns

        [Theory]
        [InlineData("docs/a.md", "**/docs/**", true)]
        [InlineData("docs/sub/a.txt", "**/docs/**", true)]
        [InlineData("src/Lib1/docs/api/x.cs", "**/docs/**", true)]
        [InlineData("src/Lib1/doc/x.cs", "**/docs/**", false)]
        [InlineData("src/PublicApi/Deep/IThing.cs", "**/PublicApi/**", true)]
        [InlineData("README.md", "**/*.md", true)]
        [InlineData("src/a/b/README.md", "**/*.md", true)]
        [InlineData("src/a.cs", "src/*.cs", true)]
        [InlineData("src/a/b.cs", "src/*.cs", false)]
        [InlineData("src/a/b.cs", "src/**", true)]
        [InlineData("src/a/b.cs", "src/**/*.cs", true)]
        [InlineData("src/b.cs", "src/**/*.cs", true)]
        [InlineData("file1.txt", "file?.txt", true)]
        public void Glob_Matches(string path, string pattern, bool expected)
        {
            Assert.Equal(expected, new FilePatternMatcher().Matches(path, pattern));
        }

        [Fact]
        public void Glob_IsCultureInvariant()
        {
            var original = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("tr-TR");
                Assert.True(new FilePatternMatcher().Matches("src/Api/MIGRATIONS/x.cs", "**/Migrations/*"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        // ------------------------------------------------------------------ path handling

        [Theory]
        [InlineData("src/Lib1/a.cs", "src/Lib1", true)]
        [InlineData("src/Lib10/a.cs", "src/Lib1", false)]
        [InlineData("src/Lib1.Tests/a.cs", "src/Lib1", false)]
        [InlineData("src/Lib1", "src/Lib1", true)]
        [InlineData("anything/at/all.cs", "", true)]
        [InlineData("SRC/lib1/a.cs", "src/Lib1", true)]
        public void IsUnderDirectory_RespectsDirectoryBoundaries(string file, string directory, bool expected)
        {
            Assert.Equal(expected, GitService.IsUnderDirectory(file, directory));
        }

        [Fact]
        public void ToRepoRelativePath_HandlesRootProjectsFilesAndOutsidePaths()
        {
            var repo = Path.Combine(_root, "repo");
            Assert.Equal("", GitService.ToRepoRelativePath(Path.Combine(repo, "Lib.csproj"), repo));
            Assert.Equal("", GitService.ToRepoRelativePath(".", repo));
            Assert.Equal("src/Lib", GitService.ToRepoRelativePath(Path.Combine(repo, "src", "Lib", "Lib.csproj"), repo));
            Assert.Equal("src/Lib", GitService.ToRepoRelativePath("src\\Lib\\", repo));
            Assert.Null(GitService.ToRepoRelativePath(Path.Combine(_root, "elsewhere", "X.csproj"), repo));
            Assert.Equal("shared/common", GitService.ToRepoRelativePath("../../shared/common", repo,
                projectFileToDirectory: false, baseDirectory: Path.Combine(repo, "src", "Api")));
            Assert.Equal("Directory.Build.props", GitService.ToRepoRelativePath("Directory.Build.props", repo, projectFileToDirectory: false));
        }

        [Fact]
        public void ProjectAtRepositoryRoot_DetectsItsOwnChanges()
        {
            using var repo = InitRepo();
            Write("MyLib.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Write("A.cs", "class A {}");
            Commit(repo, "initial");
            repo.ApplyTag("v1.0.0");
            Write("B.cs", "class B {}");
            Commit(repo, "fix: add B");

            var task = new MonoRepoVersionTask
            {
                BuildEngine = new MockBuildEngine(),
                ProjectPath = Path.Combine(_root, "MyLib.csproj"),
                RepoRoot = _root,
                IsPackable = true
            };
            Assert.True(task.Execute());
            Assert.Equal("1.0.1", task.Version);
        }

        [Fact]
        public void MovingFilesOutOfAProject_CountsAsAChange()
        {
            using var repo = InitRepo();
            Write("src/A/A.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Write("src/A/Moved.cs", "class M {}");
            Write("src/B/B.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Commit(repo, "initial");
            repo.ApplyTag("v1.0.0");
            Commands.Move(repo, "src/A/Moved.cs", "src/B/Moved.cs");
            repo.Commit("refactor: move", _sig, _sig);

            using var git = new GitService(_root);
            var tag = (Commit)repo.Tags["v1.0.0"].PeeledTarget;
            Assert.True(git.ProjectHasChangedSinceTag(tag, "src/A", null, _root));
            Assert.True(git.ProjectHasChangedSinceTag(tag, "src/B", null, _root));
        }

        // ------------------------------------------------------------------ task behaviour

        [Fact]
        public void Task_OutsideGitRepository_WarnsAndKeepsProjectVersion()
        {
            Write("nogit/Lib.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            var engine = new MockBuildEngine();
            var task = new MonoRepoVersionTask
            {
                BuildEngine = engine,
                ProjectPath = Path.Combine(_root, "nogit", "Lib.csproj"),
                RepoRoot = Path.Combine(_root, "nogit"),
                IsPackable = true
            };

            // The temp directory itself may live inside a git checkout on some machines; only assert when it doesn't.
            if (GitRepositoryHelper.DiscoverRepositoryRoot(task.RepoRoot) != null)
                return;

            Assert.True(task.Execute());
            Assert.Equal(string.Empty, task.Version);
            Assert.Contains(engine.Warnings, w => w.Code == "MRV003");
            Assert.Empty(engine.Errors);
        }

        [Fact]
        public void Task_OutsideGitRepository_HonorsForceVersion_AndRequireGitIsAnError()
        {
            Write("nogit2/Lib.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            var start = Path.Combine(_root, "nogit2");
            if (GitRepositoryHelper.DiscoverRepositoryRoot(start) != null)
                return;

            var forced = new MonoRepoVersionTask
            {
                BuildEngine = new MockBuildEngine(),
                ProjectPath = Path.Combine(start, "Lib.csproj"),
                RepoRoot = start,
                ForceVersion = "4.5.6"
            };
            Assert.True(forced.Execute());
            Assert.Equal("4.5.6", forced.Version);

            var strictEngine = new MockBuildEngine();
            var strict = new MonoRepoVersionTask
            {
                BuildEngine = strictEngine,
                ProjectPath = Path.Combine(start, "Lib.csproj"),
                RepoRoot = start,
                RequireGitRepository = true
            };
            Assert.False(strict.Execute());
            Assert.NotEmpty(strictEngine.Errors);
        }

        [Fact]
        public void Task_NonPackableProject_KeepsItsOwnVersion()
        {
            using var repo = InitRepo();
            Write("src/Svc/Svc.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Commit(repo, "initial");
            repo.ApplyTag("v3.0.0");

            var task = new MonoRepoVersionTask
            {
                BuildEngine = new MockBuildEngine(),
                ProjectPath = Path.Combine(_root, "src", "Svc", "Svc.csproj"),
                RepoRoot = _root,
                IsPackable = false,
                SkipNonPackableProjects = true
            };
            Assert.True(task.Execute());
            Assert.Equal(string.Empty, task.Version);
        }

        [Fact]
        public void Task_NonPackableProject_IsVersionedWhenSkippingIsDisabled()
        {
            using var repo = InitRepo();
            Write("src/Svc/Svc.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Commit(repo, "initial");
            repo.ApplyTag("v3.0.0");

            var task = new MonoRepoVersionTask
            {
                BuildEngine = new MockBuildEngine(),
                ProjectPath = Path.Combine(_root, "src", "Svc", "Svc.csproj"),
                RepoRoot = _root,
                IsPackable = false,
                SkipNonPackableProjects = false
            };
            Assert.True(task.Execute());
            Assert.Equal("3.0.0", task.Version);
        }

        [Fact]
        public void Task_MalformedConfig_IsReportedAsMSBuildWarning()
        {
            using var repo = InitRepo();
            Write("src/Lib/Lib.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Write("mr-version.yml", "this: is: not: valid: yaml: [");
            Commit(repo, "initial");
            repo.ApplyTag("v1.0.0");

            var engine = new MockBuildEngine();
            var task = new MonoRepoVersionTask
            {
                BuildEngine = engine,
                ProjectPath = Path.Combine(_root, "src", "Lib", "Lib.csproj"),
                RepoRoot = _root,
                IsPackable = true
            };
            Assert.True(task.Execute());
            Assert.Contains(engine.Warnings, w => w.Code == "MRV001" && w.Message.Contains("configuration"));
        }
    }
}
