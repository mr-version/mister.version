using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibGit2Sharp;
using Mister.Version.Core.Services;
using Xunit;

namespace Mister.Version.Tests
{
    /// <summary>
    /// Repository layouts beyond a plain checkout: symbolic links into the repository, submodules, and project
    /// references outside the repository.
    /// </summary>
    [Collection("VersionResultCache")]
    public class RepositoryLayoutEdgeCaseTests : IDisposable
    {
        private readonly string _work;
        private readonly Signature _sig = new Signature("T", "t@e.com", DateTimeOffset.Now);

        public RepositoryLayoutEdgeCaseTests()
        {
            _work = Path.Combine(Path.GetTempPath(), "mrv-layout-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_work);
            MonoRepoVersionTask.ClearCache();
        }

        public void Dispose()
        {
            MonoRepoVersionTask.ClearCache();
            try
            {
                foreach (var f in Directory.GetFiles(_work, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(_work, true);
            }
            catch { }
        }

        private static void Write(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
        }

        private Commit Commit(Repository repo, string message)
        {
            Commands.Stage(repo, "*");
            return repo.Commit(message, _sig, _sig);
        }

        private static string Version(string projectPath, string repoRoot, MockBuildEngine engine = null, params string[] dependencies)
        {
            var task = new MonoRepoVersionTask
            {
                BuildEngine = engine ?? new MockBuildEngine(),
                ProjectPath = projectPath,
                RepoRoot = repoRoot,
                IsPackable = true,
                EnableCache = false,
                Dependencies = dependencies.Select(d => (Microsoft.Build.Framework.ITaskItem)new Microsoft.Build.Utilities.TaskItem(d)).ToArray()
            };
            Assert.True(task.Execute());
            return task.Version;
        }

        [Fact]
        public void ProjectReachedThroughASymlinkIntoTheRepository_IsVersionedLikeTheRealPath()
        {
            var repoRoot = Path.Combine(_work, "repo");
            Repository.Init(repoRoot);
            using var repo = new Repository(repoRoot);
            Write(Path.Combine(repoRoot, "src", "Leaf", "Leaf.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Write(Path.Combine(repoRoot, "src", "Other", "file.txt"), "other");
            Commit(repo, "init");
            repo.ApplyTag("v1.0.0");
            Write(Path.Combine(repoRoot, "src", "Other", "file.txt"), "changed");
            Commit(repo, "change outside Leaf");

            var link = Path.Combine(_work, "leaflink");
            try
            {
                Directory.CreateSymbolicLink(link, Path.Combine(repoRoot, "src", "Leaf"));
            }
            catch (Exception)
            {
                return; // symbolic links need privileges on some Windows machines
            }

            var viaLink = Path.Combine(link, "Leaf.csproj");
            Assert.Equal("src/Leaf", GitService.ToRepoRelativePath(viaLink, repoRoot));
            Assert.Equal("1.0.0", Version(Path.Combine(repoRoot, "src", "Leaf", "Leaf.csproj"), repoRoot));
            MonoRepoVersionTask.ClearCache();
            Assert.Equal("1.0.0", Version(viaLink, link));
        }

        [Fact]
        public void LinkWhoseTargetGoesThroughAnotherLink_IsResolved()
        {
            // Like macOS, where the temp directory /var/... is itself a link to /private/var/...
            var real = Path.Combine(_work, "private");
            var repoRoot = Path.Combine(real, "repo");
            Directory.CreateDirectory(Path.Combine(repoRoot, "src", "Leaf"));
            var alias = Path.Combine(_work, "var");
            var link = Path.Combine(_work, "leaflink");
            try
            {
                Directory.CreateSymbolicLink(alias, real);
                Directory.CreateSymbolicLink(link, Path.Combine(alias, "repo", "src", "Leaf"));
            }
            catch (Exception)
            {
                return; // symbolic links need privileges on some Windows machines
            }

            var viaLink = Path.Combine(link, "Leaf.csproj");
            Assert.Equal("src/Leaf", GitService.ToRepoRelativePath(viaLink, repoRoot));
            Assert.Equal("src/Leaf", GitService.ToRepoRelativePath(viaLink, Path.Combine(alias, "repo")));
        }

        [Fact]
        public void ReferenceIntoASubmodule_BumpsWhenTheSubmodulePointerMoves()
        {
            // Library repository with its own history
            var libRoot = Path.Combine(_work, "lib");
            Repository.Init(libRoot);
            using (var lib = new Repository(libRoot))
            {
                Write(Path.Combine(libRoot, "src", "SubLib", "SubLib.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
                Commit(lib, "lib init");
            }

            // Super repository with the library as a submodule (gitlink + .gitmodules) and an App referencing it
            var superRoot = Path.Combine(_work, "super");
            Repository.Init(superRoot);
            using var super = new Repository(superRoot);
            Write(Path.Combine(superRoot, "src", "App", "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Write(Path.Combine(superRoot, ".gitmodules"), "[submodule \"ext/sub\"]\n\tpath = ext/sub\n\turl = ../lib\n");
            Commands.Stage(super, "*");

            string firstLibCommit;
            using (var lib = new Repository(libRoot))
                firstLibCommit = lib.Head.Tip.Sha;
            var definition = TreeDefinition.From(super.Lookup<Tree>(super.Index.WriteToTree().Sha));
            definition.AddGitLink("ext/sub", new ObjectId(firstLibCommit));
            var tree = super.ObjectDatabase.CreateTree(definition);
            var first = super.ObjectDatabase.CreateCommit(_sig, _sig, "init with submodule", tree, Array.Empty<Commit>(), false);
            var branch = super.Refs.Head.TargetIdentifier; // unborn branch HEAD points to
            super.Refs.Add(branch, first.Id, allowOverwrite: true);
            super.ApplyTag("v1.0.0");

            // New library commit, then the super repository moves the pointer
            string secondLibCommit;
            using (var lib = new Repository(libRoot))
            {
                Write(Path.Combine(libRoot, "src", "SubLib", "Class.cs"), "class C {}");
                secondLibCommit = Commit(lib, "lib change").Sha;
            }
            var bumped = TreeDefinition.From(first.Tree);
            bumped.AddGitLink("ext/sub", new ObjectId(secondLibCommit));
            var second = super.ObjectDatabase.CreateCommit(_sig, _sig, "bump submodule", super.ObjectDatabase.CreateTree(bumped), new[] { first }, false);
            super.Refs.Add(branch, second.Id, allowOverwrite: true);

            var subLibPath = Path.Combine(superRoot, "ext", "sub", "src", "SubLib", "SubLib.csproj");
            var appPath = Path.Combine(superRoot, "src", "App", "App.csproj");
            Assert.Equal("1.0.1", Version(appPath, superRoot, null, subLibPath));

            // Change detection with file patterns sees the pointer move too
            using var git = new GitService(superRoot);
            var classification = git.ClassifyProjectChanges(first, appPath, new List<string> { subLibPath }, superRoot,
                new Core.Models.ChangeDetectionConfig { Enabled = true });
            Assert.False(classification.ShouldIgnore);
        }

        [Fact]
        public void ReferenceOutsideTheRepository_IsReportedInsteadOfSilentlyIgnored()
        {
            var repoRoot = Path.Combine(_work, "repo");
            Repository.Init(repoRoot);
            using var repo = new Repository(repoRoot);
            Write(Path.Combine(repoRoot, "src", "App", "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Commit(repo, "init");
            repo.ApplyTag("v1.0.0");

            var outside = Path.Combine(_work, "outside", "Ext", "Ext.csproj");
            Write(outside, "<Project Sdk=\"Microsoft.NET.Sdk\" />");

            var engine = new MockBuildEngine();
            Version(Path.Combine(repoRoot, "src", "App", "App.csproj"), repoRoot, engine, outside);
            Assert.Contains(engine.Warnings, w => w.Message.Contains("outside the repository") && w.Message.Contains("Ext.csproj"));
        }
    }
}
