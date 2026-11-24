using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LibGit2Sharp;
using Mister.Version.Core.Services;
using Xunit;

namespace Mister.Version.Tests
{
    [Collection("VersionResultCache")]
    public class ConfigurationAndPolicyTests : IDisposable
    {
        private readonly string _root;
        private readonly Signature _sig = new Signature("Test", "test@example.com", DateTimeOffset.Now);

        public ConfigurationAndPolicyTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "mrv-config-policy-" + Guid.NewGuid().ToString("N"));
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

        private static string FindRepositoryFile(string name)
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, name);
                if (File.Exists(candidate) && File.Exists(Path.Combine(dir.FullName, "Mister.Version.sln")))
                    return candidate;
            }
            return null;
        }

        private static List<string> LoadWarnings(string path)
        {
            var warnings = new List<string>();
            var config = ConfigurationService.LoadConfigFromYaml(path, (level, message) =>
            {
                if (level == "Warning" || level == "Error") warnings.Add(message);
            });
            if (config == null) warnings.Add("configuration did not load");
            return warnings;
        }

        [Fact]
        public void ShippedExampleConfiguration_LoadsWithoutWarnings()
        {
            var example = FindRepositoryFile("mr-version.example.yml");
            Assert.NotNull(example);
            Assert.Empty(LoadWarnings(example));
        }

        [Fact]
        public void ReadmeYamlExamples_LoadWithoutWarnings()
        {
            var readme = FindRepositoryFile("README.md");
            Assert.NotNull(readme);
            // Normalize line endings: Windows checkouts typically have CRLF (core.autocrlf)
            var text = File.ReadAllText(readme).Replace("\r\n", "\n");
            var blocks = Regex.Matches(text, "```yaml\\n(.*?)```", RegexOptions.Singleline)
                .Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .Where(b => !b.Contains("jobs:") && !b.Contains("uses:") && !b.StartsWith("on:"))
                .ToList();
            Assert.NotEmpty(blocks);

            for (var i = 0; i < blocks.Count; i++)
            {
                var file = Path.Combine(_root, $"readme-{i}.yml");
                File.WriteAllText(file, blocks[i]);
                var warnings = LoadWarnings(file);
                Assert.True(warnings.Count == 0, $"README YAML block {i} failed to load cleanly: {string.Join("; ", warnings)}\n{blocks[i]}");
            }
        }

        // ------------------------------------------------------------------ version policies

        private Repository CreateRepo(string config)
        {
            Repository.Init(_root);
            var repo = new Repository(_root);
            foreach (var name in new[] { "Alpha", "Beta", "Gamma" })
            {
                Directory.CreateDirectory(Path.Combine(_root, "src", name));
                File.WriteAllText(Path.Combine(_root, "src", name, name + ".csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
                File.WriteAllText(Path.Combine(_root, "src", name, "C.cs"), "class C {}");
            }
            File.WriteAllText(Path.Combine(_root, "mr-version.yml"), config);
            Commands.Stage(repo, "*");
            repo.Commit("initial", _sig, _sig);
            repo.ApplyTag("v1.0.0");

            // Only Alpha changes, with a feature
            File.WriteAllText(Path.Combine(_root, "src", "Alpha", "D.cs"), "class D {}");
            Commands.Stage(repo, "*");
            repo.Commit("feat: alpha feature", _sig, _sig);
            return repo;
        }

        private string VersionOf(string project)
        {
            var task = new MonoRepoVersionTask
            {
                BuildEngine = new MockBuildEngine(),
                ProjectPath = Path.Combine(_root, "src", project, project + ".csproj"),
                RepoRoot = _root,
                IsPackable = true,
                EnableCache = false
            };
            Assert.True(task.Execute());
            return task.Version;
        }

        [Fact]
        public void LockStepPolicy_GivesEveryProjectTheHighestVersion()
        {
            using var repo = CreateRepo("commitConventions:\n  enabled: true\nversionPolicy:\n  policy: LockStep\n");
            Assert.Equal("1.1.0", VersionOf("Alpha"));
            Assert.Equal("1.1.0", VersionOf("Beta"));
            Assert.Equal("1.1.0", VersionOf("Gamma"));
        }

        [Fact]
        public void GroupedPolicy_AlignsOnlyGroupMembers()
        {
            using var repo = CreateRepo(
                "commitConventions:\n  enabled: true\n" +
                "versionPolicy:\n  policy: Grouped\n  groups:\n    core:\n      projects: [Alpha, Beta]\n      strategy: LockStep\n");
            Assert.Equal("1.1.0", VersionOf("Alpha"));
            Assert.Equal("1.1.0", VersionOf("Beta"));
            Assert.Equal("1.0.0", VersionOf("Gamma"));
        }

        [Fact]
        public void IndependentPolicy_DoesNotAlign()
        {
            using var repo = CreateRepo("commitConventions:\n  enabled: true\nversionPolicy:\n  policy: Independent\n");
            Assert.Equal("1.1.0", VersionOf("Alpha"));
            Assert.Equal("1.0.0", VersionOf("Beta"));
        }
    }
}
