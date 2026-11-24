using System;
using System.IO;
using System.Linq;
using LibGit2Sharp;
using Xunit;

namespace Mister.Version.Tests
{
    /// <summary>
    /// End-to-end semantics through MonoRepoVersionTask (the MSBuild entry point).
    /// </summary>
    [Collection("VersionResultCache")]
    public class VersioningSemanticsTests : IDisposable
    {
        private readonly string _root;
        private readonly Repository _repo;
        private readonly Signature _sig = new Signature("Test", "test@example.com", DateTimeOffset.Now);

        public VersioningSemanticsTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "mrv-semantics-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            Repository.Init(_root);
            _repo = new Repository(_root);
            Write("src/Api/Api.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Write("src/Api/A.cs", "class A {}");
            Write("src/Other/Other.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Commit("initial");
            _repo.ApplyTag("v1.0.0");
            MonoRepoVersionTask.ClearCache();
        }

        public void Dispose()
        {
            MonoRepoVersionTask.ClearCache();
            _repo.Dispose();
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

        private Commit Commit(string message)
        {
            Commands.Stage(_repo, "*");
            return _repo.Commit(message, _sig, _sig);
        }

        private MonoRepoVersionTask Task(MockBuildEngine engine = null, Action<MonoRepoVersionTask> configure = null)
        {
            var task = new MonoRepoVersionTask
            {
                BuildEngine = engine ?? new MockBuildEngine(),
                ProjectPath = Path.Combine(_root, "src", "Api", "Api.csproj"),
                RepoRoot = _root,
                IsPackable = true,
                EnableCache = false
            };
            configure?.Invoke(task);
            return task;
        }

        private string Version(Action<MonoRepoVersionTask> configure = null)
        {
            var task = Task(configure: configure);
            Assert.True(task.Execute());
            return task.Version;
        }

        private static void Conventional(MonoRepoVersionTask t)
        {
            t.ConventionalCommitsEnabled = true;
            t.MajorPatterns = "BREAKING CHANGE:;!:";
            t.MinorPatterns = "feat:;feature:";
            t.PatchPatterns = "fix:;bugfix:;perf:;refactor:";
            t.IgnorePatterns = "chore:;docs:;style:;test:;ci:";
        }

        // ------------------------------------------------------------------ conventional commits

        [Fact]
        public void ScopedFeatureCommit_IsAMinorBump()
        {
            Write("src/Api/B.cs", "class B {}");
            Commit("feat(api): add pagination support");
            Assert.Equal("1.1.0", Version(Conventional));
        }

        [Fact]
        public void IgnoreTypeMentionedInsideAFixDescription_DoesNotIgnoreTheFix()
        {
            Write("src/Api/B.cs", "class B {}");
            Commit("fix: handle docs: links in the parser");
            Assert.Equal("1.0.1", Version(Conventional));
        }

        [Fact]
        public void FeatureInAnotherProject_DoesNotRaiseThisProjectsBump()
        {
            Write("src/Other/X.cs", "class X {}");
            Commit("feat!: remove old Other API");
            Write("src/Api/B.cs", "class B {}");
            Commit("fix: api bug");
            Assert.Equal("1.0.1", Version(Conventional));
        }

        [Fact]
        public void OnlyIgnoredCommitTypes_DoNotBump()
        {
            Write("src/Api/B.cs", "class B {}");
            Commit("chore: tidy up");
            Assert.Equal("1.0.0", Version(Conventional));
        }

        // ------------------------------------------------------------------ file patterns

        [Fact]
        public void DocsOnlyChange_WithIgnorePatterns_DoesNotBump()
        {
            Write("src/Api/README.md", "docs");
            Commit("update readme");
            Assert.Equal("1.0.0", Version(t =>
            {
                t.ChangeDetectionEnabled = true;
                t.IgnoreFilePatterns = "**/*.md";
            }));
        }

        [Fact]
        public void MajorFilePattern_OnTheProjectsOwnFiles_IsAMajorBump()
        {
            Write("src/Api/PublicApi/IThing.cs", "interface IThing {}");
            Commit("fix: change public api");
            Assert.Equal("2.0.0", Version(t =>
            {
                t.ChangeDetectionEnabled = true;
                t.MajorFilePatterns = "**/PublicApi/**";
            }));
        }

        [Fact]
        public void ChangeInAdditionalMonitorPath_Bumps()
        {
            Write("shared/common/Utils.cs", "class U {}");
            Commit("fix: shared");
            Assert.Equal("1.0.1", Version(t => t.AdditionalMonitorPaths = "shared/common"));
            MonoRepoVersionTask.ClearCache();
            Assert.Equal("1.0.1", Version(t => t.AdditionalMonitorPaths = "../../shared/common"));
        }

        // ------------------------------------------------------------------ configuration

        [Fact]
        public void YamlWithUnknownKeys_StillAppliesKnownSettings_AndWarns()
        {
            Write("mr-version.yml", "baseVersion: \"5.0.0\"\nsomethingUnknown: 1\n");
            Write("src/Api/B.cs", "class B {}");
            Commit("fix: change");

            var engine = new MockBuildEngine();
            var task = Task(engine);
            Assert.True(task.Execute());
            Assert.StartsWith("5.0.", task.Version);
            Assert.Contains(engine.Warnings, w => w.Code == "MRV001" && w.Message.Contains("somethingUnknown"));
        }

        [Fact]
        public void YamlVersionSchemeCalver_UsingDocumentedKeys_ProducesCalVer()
        {
            Write("mr-version.yml", "versionScheme: calver\ncalver:\n  format: YYYY.MM.PATCH\n");
            Write("src/Api/B.cs", "class B {}");
            Commit("fix: change");

            var version = Version();
            Assert.StartsWith(DateTime.UtcNow.Year + ".", version);
        }

        [Fact]
        public void MSBuildVersionSchemeCalver_ProducesCalVer()
        {
            Write("src/Api/B.cs", "class B {}");
            Commit("fix: change");
            Assert.StartsWith(DateTime.UtcNow.Year + ".", Version(t => t.VersionScheme = "calver"));
        }

        [Fact]
        public void ValidationViolation_FailsTheBuild()
        {
            Write("src/Api/B.cs", "class B {}");
            Commit("fix: change");

            var engine = new MockBuildEngine();
            var task = Task(engine, t =>
            {
                t.ValidationEnabled = true;
                t.BlockedVersions = "1.0.1";
            });
            Assert.False(task.Execute());
            Assert.Contains(engine.Errors, e => e.Code == "MRV004");
        }

        // ------------------------------------------------------------------ branches

        [Fact]
        public void DetachedHead_AtMainTip_IsVersionedAsMain()
        {
            Write("src/Api/B.cs", "class B {}");
            var head = Commit("fix: change");
            var mainName = _repo.Head.FriendlyName;
            Commands.Checkout(_repo, head); // detach

            Assert.True(_repo.Info.IsHeadDetached);
            Assert.Equal("1.0.1", Version());
            Assert.Contains(mainName, new[] { "main", "master" });
        }

        [Fact]
        public void DetachedHead_NotAtAnyBranchTip_KeepsNoBranchVersion()
        {
            Write("src/Api/B.cs", "class B {}");
            var between = Commit("fix: change");
            Write("src/Api/C.cs", "class C {}");
            Commit("fix: change 2");
            Commands.Checkout(_repo, between);

            Assert.Contains("-no-branch.", Version());
        }

        [Fact]
        public void DetachedHead_CiBranchVariable_IsUsedOnlyForTheCommitTheCiBuilds()
        {
            Write("src/Api/B.cs", "class B {}");
            var between = Commit("fix: change");
            Write("src/Api/C.cs", "class C {}");
            Commit("fix: change 2");
            Commands.Checkout(_repo, between);

            var saved = new[] { "GITHUB_HEAD_REF", "GITHUB_SHA" }.ToDictionary(v => v, Environment.GetEnvironmentVariable);
            try
            {
                Environment.SetEnvironmentVariable("GITHUB_HEAD_REF", "feature/login");
                Environment.SetEnvironmentVariable("GITHUB_SHA", "0000000000000000000000000000000000000000");
                Assert.Contains("-no-branch.", Version());

                MonoRepoVersionTask.ClearCache();
                Environment.SetEnvironmentVariable("GITHUB_SHA", between.Sha);
                Assert.Contains("-login.", Version());
            }
            finally
            {
                foreach (var kv in saved) Environment.SetEnvironmentVariable(kv.Key, kv.Value);
            }
        }

        [Fact]
        public void ExplicitBranchName_OverridesTheCheckedOutBranch()
        {
            Write("src/Api/B.cs", "class B {}");
            Commit("fix: change");
            Assert.Contains("-awesome.", Version(t => t.BranchName = "feature/awesome"));
        }

        [Fact]
        public void IncludeBranchInMetadata_KeepsBuildMetadata()
        {
            Write("src/Api/B.cs", "class B {}");
            Commit("fix: change");
            Commands.Checkout(_repo, _repo.CreateBranch("feature/auth"));
            var version = Version(t => t.IncludeBranchInMetadata = true);
            Assert.Contains("+", version);
        }
    }
}
