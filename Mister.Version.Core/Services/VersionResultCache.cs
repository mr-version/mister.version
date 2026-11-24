using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using LibGit2Sharp;

namespace Mister.Version.Core.Services
{
    /// <summary>
    /// A calculated version as stored in <see cref="VersionResultCache"/>.
    /// </summary>
    public sealed class CachedVersionResult
    {
        public string Version { get; set; }
        public bool VersionChanged { get; set; }
        public string ChangeReason { get; set; }
    }

    /// <summary>
    /// Computes a fingerprint of every piece of repository state a calculated version depends on:
    /// HEAD (ref name and commit), all tags (name and target), shallow-clone boundary, the configuration
    /// file contents, the current UTC date (CalVer) and the identity of this tool build.
    /// Commits are immutable, so together with the calculation inputs this fully determines the result.
    /// </summary>
    public static class RepositoryFingerprint
    {
        private static readonly string ToolIdentity =
            typeof(RepositoryFingerprint).Assembly.ManifestModule.ModuleVersionId.ToString("N");

        /// <summary>
        /// Computes the fingerprint for the repository at <paramref name="repoRoot"/>.
        /// </summary>
        /// <param name="repoRoot">Repository working directory (or .git directory).</param>
        /// <param name="configFile">Explicit configuration file, or null/empty to fingerprint the auto-discovered ones.</param>
        public static string Compute(string repoRoot, string configFile)
        {
            using (var repo = new Repository(repoRoot))
            {
                return Compute(repo, repoRoot, configFile);
            }
        }

        public static string Compute(Repository repo, string repoRoot, string configFile)
        {
            var sb = new StringBuilder(4096);
            sb.Append("tool=").Append(ToolIdentity).Append('\n');
            sb.Append("gitdir=").Append(repo.Info.Path).Append('\n');
            sb.Append("date=").Append(DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');

            var head = repo.Head;
            sb.Append("head=").Append(repo.Info.IsHeadDetached ? "(detached)" : head?.CanonicalName)
              .Append('@').Append(head?.Tip?.Sha ?? "(unborn)").Append('\n');

            // Tags always matter; with a detached HEAD the branch is resolved from branch tips and CI variables
            var detached = repo.Info.IsHeadDetached;
            var tags = new List<string>();
            foreach (var reference in repo.Refs)
            {
                if (reference.CanonicalName.StartsWith("refs/tags/", StringComparison.Ordinal) ||
                    (detached && (reference.CanonicalName.StartsWith("refs/heads/", StringComparison.Ordinal) ||
                                  reference.CanonicalName.StartsWith("refs/remotes/", StringComparison.Ordinal))))
                {
                    tags.Add(reference.CanonicalName + "=" + reference.TargetIdentifier);
                }
            }
            if (detached)
            {
                foreach (var variable in GitService.CiBranchVariables.Concat(GitService.CiCommitVariables).Concat(new[] { "GITHUB_REF_TYPE" }))
                {
                    sb.Append("env:").Append(variable).Append('=').Append(Environment.GetEnvironmentVariable(variable)).Append('\n');
                }
            }
            tags.Sort(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                sb.Append(tag).Append('\n');
            }

            sb.Append("shallow=").Append(repo.Info.IsShallow).Append('\n');
            if (repo.Info.IsShallow)
            {
                AppendFile(sb, Path.Combine(repo.Info.Path, "shallow"));
            }

            if (!string.IsNullOrEmpty(configFile))
            {
                AppendFile(sb, Path.GetFullPath(configFile));
            }
            else
            {
                foreach (var candidate in ConfigurationService.DefaultConfigFileNames)
                {
                    AppendFile(sb, Path.Combine(repoRoot, candidate));
                }
            }

            return Hash(sb.ToString());
        }

        private static void AppendFile(StringBuilder sb, string path)
        {
            sb.Append("file=").Append(path).Append('=');
            try
            {
                if (File.Exists(path))
                {
                    using (var sha = SHA256.Create())
                    using (var stream = File.OpenRead(path))
                    {
                        sb.Append(ToHex(sha.ComputeHash(stream)));
                    }
                }
                else
                {
                    sb.Append("(missing)");
                }
            }
            catch (IOException)
            {
                sb.Append("(unreadable)");
            }
            catch (UnauthorizedAccessException)
            {
                sb.Append("(unreadable)");
            }
            sb.Append('\n');
        }

        internal static string Hash(string value)
        {
            using (var sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
            {
                sb.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Two-level cache for calculated versions: an in-process dictionary (shared by every task invocation in
    /// an MSBuild node, including across builds when nodes are reused) and an optional per-project file
    /// (shared across processes: parallel nodes, separate build/pack/test invocations, CI steps).
    /// Entries are only ever looked up by an exact key that includes the <see cref="RepositoryFingerprint"/>,
    /// so new commits, tags, branch switches or configuration edits can never produce a stale version.
    /// </summary>
    public static class VersionResultCache
    {
        private const string FileHeader = "mister-version-cache/1";
        private const int MaxMemoryEntries = 4096;

        private static readonly ConcurrentDictionary<string, CachedVersionResult> Memory =
            new ConcurrentDictionary<string, CachedVersionResult>(StringComparer.Ordinal);

        /// <summary>
        /// Combines a repository fingerprint and a description of all calculation inputs into a cache key.
        /// </summary>
        public static string CreateKey(string repositoryFingerprint, string inputs)
        {
            return RepositoryFingerprint.Hash(repositoryFingerprint + "\n" + inputs);
        }

        public static bool TryGet(string key, string cacheFile, out CachedVersionResult result)
        {
            if (Memory.TryGetValue(key, out result))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(cacheFile) && TryRead(cacheFile, key, out result))
            {
                AddToMemory(key, result);
                return true;
            }

            result = null;
            return false;
        }

        public static void Store(string key, string cacheFile, CachedVersionResult result)
        {
            if (result == null || string.IsNullOrEmpty(result.Version))
            {
                return;
            }

            AddToMemory(key, result);

            if (!string.IsNullOrEmpty(cacheFile))
            {
                TryWrite(cacheFile, key, result);
            }
        }

        /// <summary>Clears the in-process cache (for tests).</summary>
        public static void ClearMemory()
        {
            Memory.Clear();
        }

        private static void AddToMemory(string key, CachedVersionResult result)
        {
            if (Memory.Count >= MaxMemoryEntries)
            {
                Memory.Clear();
            }
            Memory[key] = result;
        }

        private static bool TryRead(string cacheFile, string key, out CachedVersionResult result)
        {
            result = null;
            try
            {
                if (!File.Exists(cacheFile))
                {
                    return false;
                }

                var lines = File.ReadAllLines(cacheFile);
                if (lines.Length < 5 || lines[0] != FileHeader || lines[1] != key || string.IsNullOrEmpty(lines[2]))
                {
                    return false;
                }

                result = new CachedVersionResult
                {
                    Version = lines[2],
                    VersionChanged = string.Equals(lines[3], "true", StringComparison.OrdinalIgnoreCase),
                    ChangeReason = lines[4]
                };
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static void TryWrite(string cacheFile, string key, CachedVersionResult result)
        {
            string temp = null;
            try
            {
                var directory = Path.GetDirectoryName(cacheFile);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var content = string.Join("\n", new[]
                {
                    FileHeader,
                    key,
                    result.Version,
                    result.VersionChanged ? "true" : "false",
                    (result.ChangeReason ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ')
                }) + "\n";

                // Write-then-rename so concurrent readers (other TFMs / nodes) never see a partial file.
                temp = cacheFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temp, content, new UTF8Encoding(false));
                if (File.Exists(cacheFile))
                {
                    File.Replace(temp, cacheFile, null);
                }
                else
                {
                    File.Move(temp, cacheFile);
                }
                temp = null;
            }
            catch (IOException)
            {
                // Another node won the race or the file is locked; the cache is best-effort.
            }
            catch (UnauthorizedAccessException)
            {
            }
            finally
            {
                if (temp != null)
                {
                    try { File.Delete(temp); } catch { }
                }
            }
        }
    }
}
