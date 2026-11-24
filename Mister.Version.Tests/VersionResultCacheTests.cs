using System;
using System.IO;
using System.Linq;
using LibGit2Sharp;
using Mister.Version.Core.Services;
using Xunit;

namespace Mister.Version.Tests
{
    /// <summary>
    /// Regression tests for the version result cache: it must never return a stale version when
    /// the repository state the version depends on changes (new tag without a new commit,
    /// branch switch, configuration edit), including across MSBuild node reuse (static caches).
    /// </summary>
    [Collection("VersionResultCache")]
    public class VersionResultCacheTests : IDisposable
    {
        private readonly string _repoRoot;
        private readonly Repository _repo;
        private readonly Signature _sig = new Signature("Test", "test@example.com", DateTimeOffset.Now);

        public VersionResultCacheTests()
        {
            _repoRoot = Path.Combine(Path.GetTempPath(), "mrv-cache-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_repoRoot);
            Repository.Init(_repoRoot);
            _repo = new Repository(_repoRoot);

            Write("src/Lib/Lib.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            Write("src/Lib/Class1.cs", "class C {}");
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

        private string ProjectPath => Path.Combine(_repoRoot, "src", "Lib", "Lib.csproj");

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

        private MonoRepoVersionTask CreateTask(string cacheFile = null, MockBuildEngine engine = null) => new MonoRepoVersionTask
        {
            BuildEngine = engine ?? new MockBuildEngine(),
            ProjectPath = ProjectPath,
            RepoRoot = _repoRoot,
            TagPrefix = "v",
            IsPackable = true,
            CacheFile = cacheFile
        };

        private string Fingerprint() => RepositoryFingerprint.Compute(_repoRoot, null);

        [Fact]
        public void Fingerprint_IsStable_WhenNothingChanges()
        {
            Assert.Equal(Fingerprint(), Fingerprint());
        }

        [Fact]
        public void Fingerprint_Changes_WhenTagIsAddedWithoutNewCommit()
        {
            var before = Fingerprint();
            _repo.ApplyTag("v2.0.0");
            Assert.NotEqual(before, Fingerprint());
        }

        [Fact]
        public void Fingerprint_Changes_WhenBranchIsSwitchedAtSameCommit()
        {
            var before = Fingerprint();
            Commands.Checkout(_repo, _repo.CreateBranch("feature/x"));
            Assert.NotEqual(before, Fingerprint());
        }

        [Fact]
        public void Fingerprint_Changes_WhenAutoDiscoveredConfigIsEdited()
        {
            Write("mr-version.yml", "prereleaseType: none\n");
            var before = Fingerprint();
            Write("mr-version.yml", "prereleaseType: beta\n");
            Assert.NotEqual(before, Fingerprint());
        }

        [Fact]
        public void Fingerprint_Changes_WhenHeadMoves()
        {
            var before = Fingerprint();
            Write("src/Lib/Class2.cs", "class D {}");
            Commit("fix: change");
            Assert.NotEqual(before, Fingerprint());
        }

        [Fact]
        public void CacheFile_RoundTrips_AndMissesOnDifferentKey()
        {
            var file = Path.Combine(_repoRoot, "obj", "Lib.mrversion.cache");
            var result = new CachedVersionResult { Version = "1.2.3-beta.4", VersionChanged = true, ChangeReason = "line1\nline2" };

            VersionResultCache.Store("key-a", file, result);
            VersionResultCache.ClearMemory();

            Assert.True(VersionResultCache.TryGet("key-a", file, out var hit));
            Assert.Equal("1.2.3-beta.4", hit.Version);
            Assert.True(hit.VersionChanged);
            Assert.Equal("line1 line2", hit.ChangeReason);

            VersionResultCache.ClearMemory();
            Assert.False(VersionResultCache.TryGet("key-b", file, out _));
        }

        [Fact]
        public void CacheFile_IgnoresCorruptContent()
        {
            var file = Path.Combine(_repoRoot, "obj", "corrupt.mrversion.cache");
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, "garbage");
            VersionResultCache.ClearMemory();
            Assert.False(VersionResultCache.TryGet("any", file, out _));
        }

        [Fact]
        public void Task_PicksUpNewTagOnHead_WithoutNewCommit()
        {
            Write("src/Lib/Class2.cs", "class D {}");
            Commit("fix: change");

            var first = CreateTask();
            Assert.True(first.Execute());
            Assert.Equal("1.0.1", first.Version);

            // Same process (static caches survive, like a reused MSBuild node), HEAD unchanged, new tag.
            _repo.ApplyTag("v2.0.0");

            var second = CreateTask();
            Assert.True(second.Execute());
            Assert.Equal("2.0.0", second.Version);
        }

        [Fact]
        public void Task_PicksUpNewTagOnHead_FromCacheFile()
        {
            var cacheFile = Path.Combine(_repoRoot, "src", "Lib", "obj", "Lib.mrversion.cache");
            Write("src/Lib/Class2.cs", "class D {}");
            Commit("fix: change");

            var first = CreateTask(cacheFile);
            Assert.True(first.Execute());
            Assert.Equal("1.0.1", first.Version);
            Assert.True(File.Exists(cacheFile));

            _repo.ApplyTag("v2.0.0");
            MonoRepoVersionTask.ClearCache(); // simulate a fresh process that only has the obj/ cache file

            var second = CreateTask(cacheFile);
            Assert.True(second.Execute());
            Assert.Equal("2.0.0", second.Version);
        }

        [Fact]
        public void Task_ReusesCachedResult_InFreshProcess()
        {
            var cacheFile = Path.Combine(_repoRoot, "src", "Lib", "obj", "Lib.mrversion.cache");
            Write("src/Lib/Class2.cs", "class D {}");
            Commit("fix: change");

            Assert.True(CreateTask(cacheFile).Execute());
            MonoRepoVersionTask.ClearCache();

            var engine = new MockBuildEngine();
            var task = CreateTask(cacheFile, engine);
            Assert.True(task.Execute());
            Assert.Equal("1.0.1", task.Version);
            Assert.Contains(engine.Messages, m => m.Message.Contains("(cached)"));
        }

        [Fact]
        public void Task_DoesNotReuseResult_WhenSettingsDiffer()
        {
            var cacheFile = Path.Combine(_repoRoot, "src", "Lib", "obj", "Lib.mrversion.cache");
            Write("src/Lib/Class2.cs", "class D {}");
            Commit("fix: change");

            Assert.True(CreateTask(cacheFile).Execute());

            var forced = CreateTask(cacheFile);
            forced.ForceVersion = "9.9.9";
            Assert.True(forced.Execute());
            Assert.Equal("9.9.9", forced.Version);
        }
    }
}
