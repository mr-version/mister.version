using System;
using System.IO;
using System.Linq;
using LibGit2Sharp;
using Mister.Version.Core.Services;
using Xunit;

namespace Mister.Version.Tests
{
    public class ChangelogRangeTests : IDisposable
    {
        private readonly string _root;
        private readonly Repository _repo;
        private readonly Signature _sig = new Signature("Test", "test@example.com", DateTimeOffset.Now);

        public ChangelogRangeTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "mrv-changelog-range-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            Repository.Init(_root);
            _repo = new Repository(_root);
        }

        public void Dispose()
        {
            _repo.Dispose();
            try
            {
                foreach (var f in Directory.GetFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(_root, true);
            }
            catch { }
        }

        private Commit Commit(string file, string message)
        {
            var path = Path.Combine(_root, file.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.AppendAllText(path, message + "\n");
            Commands.Stage(_repo, "*");
            return _repo.Commit(message, _sig, _sig);
        }

        [Fact]
        public void Resolve_UsesAnnotatedTag_AsStartOfRange()
        {
            Commit("src/A/a.txt", "feat: initial");
            _repo.ApplyTag("v1.0.0", _sig, "release 1.0.0"); // annotated
            var c1 = Commit("src/A/a.txt", "fix: one");
            var c2 = Commit("src/A/a.txt", "fix: two");

            var range = ChangelogRange.Resolve(_repo, _repo.Head.Tip, "v", null, null, excludeTagsOnTarget: true);

            Assert.Equal("1.0.0", range.FromVersion);
            Assert.Equal(new[] { c2.Sha, c1.Sha }, range.Commits.Select(c => c.Sha));
        }

        [Fact]
        public void Resolve_IgnoresNewerTagOnUnrelatedBranch()
        {
            Commit("src/A/a.txt", "feat: initial");
            _repo.ApplyTag("v1.0.0");
            var main = _repo.Head;
            Commands.Checkout(_repo, _repo.CreateBranch("other"));
            Commit("src/A/a.txt", "chore: other branch");
            _repo.ApplyTag("v9.0.0");
            Commands.Checkout(_repo, main);
            var fix = Commit("src/A/a.txt", "fix: on main");

            var range = ChangelogRange.Resolve(_repo, _repo.Head.Tip, "v", null, null, excludeTagsOnTarget: true);

            Assert.Equal("1.0.0", range.FromVersion);
            Assert.Equal(new[] { fix.Sha }, range.Commits.Select(c => c.Sha));
        }

        [Fact]
        public void Resolve_TagOnTarget_DescribesChangesSincePreviousRelease()
        {
            Commit("src/A/a.txt", "feat: initial");
            _repo.ApplyTag("v1.0.0");
            var fix = Commit("src/A/a.txt", "fix: one");
            _repo.ApplyTag("v1.0.1");

            var range = ChangelogRange.Resolve(_repo, _repo.Head.Tip, "v", null, null, excludeTagsOnTarget: true);

            Assert.Equal("1.0.0", range.FromVersion);
            Assert.Equal(new[] { fix.Sha }, range.Commits.Select(c => c.Sha));
        }

        [Fact]
        public void Resolve_FiltersByProjectDirectory_WithoutPrefixFalsePositives()
        {
            Commit("src/Lib1/a.txt", "feat: initial");
            Commit("src/Lib10/a.txt", "feat: initial 10");
            _repo.ApplyTag("v1.0.0");
            var lib1 = Commit("src/Lib1/a.txt", "fix: lib1");
            Commit("src/Lib10/a.txt", "fix: lib10 only");

            var range = ChangelogRange.Resolve(_repo, _repo.Head.Tip, "v", "Lib1", "src/Lib1", excludeTagsOnTarget: true);

            Assert.Equal(new[] { lib1.Sha }, range.Commits.Select(c => c.Sha));
        }

        [Fact]
        public void Resolve_UsesProjectSpecificTags()
        {
            Commit("src/Lib1/a.txt", "feat: initial");
            _repo.ApplyTag("Lib1/v2.3.0");
            var fix = Commit("src/Lib1/a.txt", "fix: lib1");

            var range = ChangelogRange.Resolve(_repo, _repo.Head.Tip, "v", "Lib1", "src/Lib1", excludeTagsOnTarget: true);

            Assert.Equal("2.3.0", range.FromVersion);
            Assert.Equal(new[] { fix.Sha }, range.Commits.Select(c => c.Sha));
        }

        [Fact]
        public void Resolve_NoTags_StartsAtRoot()
        {
            var a = Commit("src/A/a.txt", "feat: initial");
            var b = Commit("src/A/a.txt", "fix: one");

            var range = ChangelogRange.Resolve(_repo, _repo.Head.Tip, "v", null, null, excludeTagsOnTarget: true);

            Assert.Null(range.FromCommit);
            Assert.Equal(new[] { b.Sha, a.Sha }, range.Commits.Select(c => c.Sha));
        }
    }
}
