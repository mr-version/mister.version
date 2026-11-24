using System;
using System.IO;
using System.Linq;
using LibGit2Sharp;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;
using Xunit;

namespace Mister.Version.Tests
{
    /// <summary>
    /// Git history and branch edge cases found by exploratory testing (release branches without a version, numeric
    /// feature names, conventional-commit corner cases, CalVer tags, tags on non-commit objects, branch name forms).
    /// </summary>
    [Collection("VersionResultCache")]
    public class GitHistoryEdgeCaseTests : IDisposable
    {
        private readonly string _root;
        private readonly Repository _repo;
        private readonly Signature _sig = new Signature("T", "t@e.com", DateTimeOffset.Now);

        public GitHistoryEdgeCaseTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "mrv-history-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            Repository.Init(_root);
            _repo = new Repository(_root);
            Write("src/Core/Core.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Write("src/Core/A.cs", "class A {}");
            Commit("initial");
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

        private void Change(string message)
        {
            Write("src/Core/A.cs", "class A {} // " + Guid.NewGuid().ToString("N"));
            Commit(message);
        }

        private string Version(Action<MonoRepoVersionTask> configure = null, MockBuildEngine engine = null)
        {
            MonoRepoVersionTask.ClearCache();
            var task = new MonoRepoVersionTask
            {
                BuildEngine = engine ?? new MockBuildEngine(),
                ProjectPath = Path.Combine(_root, "src", "Core", "Core.csproj"),
                RepoRoot = _root,
                IsPackable = true,
                EnableCache = false
            };
            configure?.Invoke(task);
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

        // ------------------------------------------------------------------ branches

        [Theory]
        [InlineData("release/foo")]
        [InlineData("release/next")]
        public void ReleaseBranchWithoutVersion_IsBumpedLikeMain(string branch)
        {
            _repo.ApplyTag("v1.0.0");
            Commands.Checkout(_repo, _repo.CreateBranch(branch));
            Change("fix: core");
            Assert.Equal("1.0.1", Version());
        }

        [Fact]
        public void NumericFeatureBranchWithLeadingZeros_GivesValidSemVer()
        {
            _repo.ApplyTag("v1.0.0");
            Commands.Checkout(_repo, _repo.CreateBranch("feature/0042"));
            Change("fix: core");
            var version = Version();
            Assert.Equal("1.0.1-42.1", version);
            Assert.True(SemVer.TryParse(version, out _));
        }

        [Theory]
        [InlineData("refs/heads/main")]
        [InlineData("origin/main")]
        [InlineData("refs/remotes/origin/main")]
        public void ExplicitBranchNameAsARef_IsNormalized(string branchName)
        {
            _repo.ApplyTag("v1.0.0");
            Change("fix: core");
            Commands.Checkout(_repo, _repo.Head.Tip); // detached
            Assert.Equal("1.0.1", Version(t => t.BranchName = branchName));
        }

        [Fact]
        public void FeatureBranchWithoutAnyTag_GetsDistinctVersionsPerCommit()
        {
            Commands.Checkout(_repo, _repo.CreateBranch("feature/x"));
            Change("fix: one");
            var first = Version();
            Change("fix: two");
            var second = Version();
            Assert.NotEqual(first, second);
        }

        // ------------------------------------------------------------------ tags

        [Theory]
        [InlineData("v5.0.0-01")]
        [InlineData("v5.0.0-rc.01")]
        [InlineData("v99999999999.0.0")]
        public void InvalidSemVerTags_AreIgnored(string tag)
        {
            _repo.ApplyTag("v1.0.0");
            Change("fix: core");
            _repo.ApplyTag(tag);
            Assert.Equal("1.0.1", Version());
        }

        [Fact]
        public void TagOnATree_IsIgnored()
        {
            _repo.ApplyTag("v1.0.0");
            Change("fix: core");
            _repo.Tags.Add("v9.0.0", _repo.Head.Tip.Tree);
            Assert.Equal("1.0.1", Version());
        }

        // ------------------------------------------------------------------ conventional commits

        [Theory]
        [InlineData("fix: parser\n\nNot a breaking change: existing callers are unaffected.", "1.0.1")]
        [InlineData("fix: this is not a BREAKING CHANGE: just text", "1.0.1")]
        [InlineData("fix: parser\n\nBREAKING CHANGE: the parser is stricter", "2.0.0")]
        [InlineData("fix: parser\n\nBREAKING-CHANGE: the parser is stricter", "2.0.0")]
        [InlineData("Revert \"feat: add pagination\"", "1.0.1")]
        public void ConventionalCommitCornerCases(string message, string expected)
        {
            _repo.ApplyTag("v1.0.0");
            Change(message);
            Assert.Equal(expected, Version(Conventional));
        }

        [Fact]
        public void MergeCommitOfChoreOnlyBranch_DoesNotBump()
        {
            _repo.ApplyTag("v1.0.0");
            var main = _repo.Head;
            var topic = _repo.CreateBranch("topic");
            Commands.Checkout(_repo, topic);
            Write("src/Core/notes.txt", "chore");
            Commit("chore: tidy");
            Commands.Checkout(_repo, main);
            Write("other.txt", "unrelated");
            Commit("docs: unrelated");
            var result = _repo.Merge(topic, _sig, new MergeOptions { FastForwardStrategy = FastForwardStrategy.NoFastForward, CommitOnSuccess = true });
            Assert.Equal(MergeStatus.NonFastForward, result.Status);

            Assert.Equal("1.0.0", Version(Conventional));
        }

        // ------------------------------------------------------------------ CalVer

        private static string CurrentPeriod => $"{DateTime.UtcNow.Year}.{DateTime.UtcNow.Month}";

        [Fact]
        public void CalVer_HonorsGlobalTags()
        {
            _repo.ApplyTag($"v{CurrentPeriod}.0");
            Change("fix: core");
            Assert.Equal($"{CurrentPeriod}.1", Version(t => t.VersionScheme = "calver"));
        }

        [Fact]
        public void CalVer_TagFromALaterPeriod_DoesNotMakeVersionsGoBackwards()
        {
            _repo.ApplyTag($"Core/v{CurrentPeriod}.0");
            Change("fix: core");
            _repo.ApplyTag($"Core/v{DateTime.UtcNow.Year + 5}.6.0");
            Change("fix: core again");

            var engine = new MockBuildEngine();
            Assert.Equal($"{DateTime.UtcNow.Year + 5}.6.1", Version(t => t.VersionScheme = "calver", engine));
            Assert.Contains(engine.Warnings, w => w.Message.Contains("later CalVer period"));
        }
    }
}
