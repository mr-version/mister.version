using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LibGit2Sharp;

namespace Mister.Version.Core.Services
{
    /// <summary>
    /// Resolves the commit range a changelog covers: from the nearest version tag that is an ancestor of
    /// the target commit (by history, not by date; annotated tags are peeled) up to the target commit,
    /// optionally limited to commits that touch a project directory.
    /// </summary>
    public static class ChangelogRange
    {
        private static readonly Regex VersionPattern = new Regex(@"^\d+\.\d+\.\d+([-+].*)?$", RegexOptions.Compiled);

        public sealed class Result
        {
            /// <summary>Commit of the previous version tag, or null if there is none (range starts at the root).</summary>
            public Commit FromCommit { get; set; }

            /// <summary>Version of the previous tag (without prefix), or null.</summary>
            public string FromVersion { get; set; }

            /// <summary>Commits in the range, newest first.</summary>
            public List<Commit> Commits { get; set; } = new List<Commit>();
        }

        /// <summary>
        /// Maps commit SHA to the version tags pointing at it. Project tags ("{project}/{prefix}1.2.3",
        /// "{project}-{prefix}1.2.3") are included when <paramref name="projectName"/> is given.
        /// </summary>
        public static Dictionary<string, List<string>> GetVersionTagsByCommit(Repository repo, string tagPrefix, string projectName)
        {
            tagPrefix = tagPrefix ?? string.Empty;
            var prefixes = new List<string>();
            if (!string.IsNullOrEmpty(projectName))
            {
                prefixes.Add(projectName + "/" + tagPrefix);
                prefixes.Add(projectName + "-" + tagPrefix);
            }
            prefixes.Add(tagPrefix);

            var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var tag in repo.Tags)
            {
                var name = tag.FriendlyName;
                string version = null;
                foreach (var prefix in prefixes)
                {
                    if (name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        var candidate = name.Substring(prefix.Length);
                        if (VersionPattern.IsMatch(candidate))
                        {
                            version = candidate;
                            break;
                        }
                    }
                }

                if (version == null || !(tag.PeeledTarget is Commit commit))
                {
                    continue;
                }

                if (!map.TryGetValue(commit.Sha, out var versions))
                {
                    map[commit.Sha] = versions = new List<string>();
                }
                versions.Add(version);
            }
            return map;
        }

        /// <summary>
        /// Resolves the range ending at <paramref name="to"/>.
        /// </summary>
        /// <param name="repo">Repository.</param>
        /// <param name="to">Last commit of the range (usually HEAD).</param>
        /// <param name="tagPrefix">Version tag prefix, e.g. "v".</param>
        /// <param name="projectName">Project name for project-specific tags (optional).</param>
        /// <param name="projectDirectory">Repository-relative project directory to filter commits by (optional).</param>
        /// <param name="excludeTagsOnTarget">When true, a version tag on <paramref name="to"/> itself is not used as the
        /// start of the range (so a tagged release commit still gets the changes since the previous release).</param>
        public static Result Resolve(Repository repo, Commit to, string tagPrefix, string projectName, string projectDirectory, bool excludeTagsOnTarget)
        {
            var result = new Result();
            if (to == null)
            {
                return result;
            }

            var tagged = GetVersionTagsByCommit(repo, tagPrefix, projectName);

            // Nearest tagged ancestor in history order.
            foreach (var commit in repo.Commits.QueryBy(new CommitFilter
            {
                IncludeReachableFrom = to,
                SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time
            }))
            {
                if (excludeTagsOnTarget && commit.Sha == to.Sha)
                {
                    continue;
                }
                if (tagged.TryGetValue(commit.Sha, out var versions))
                {
                    result.FromCommit = commit;
                    result.FromVersion = versions.OrderByDescending(v => v, StringComparer.Ordinal).First();
                    break;
                }
            }

            var filter = new CommitFilter { IncludeReachableFrom = to, SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time };
            if (result.FromCommit != null)
            {
                filter.ExcludeReachableFrom = result.FromCommit;
            }

            var dir = NormalizeDirectory(projectDirectory);
            foreach (var commit in repo.Commits.QueryBy(filter))
            {
                if (dir == null || TouchesDirectory(repo, commit, dir))
                {
                    result.Commits.Add(commit);
                }
            }
            return result;
        }

        private static string NormalizeDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return null;
            }
            var dir = directory.Replace('\\', '/').Trim('/');
            return dir.Length == 0 || dir == "." ? null : dir;
        }

        private static bool TouchesDirectory(Repository repo, Commit commit, string directory)
        {
            var parent = commit.Parents.FirstOrDefault();
            var changes = repo.Diff.Compare<TreeChanges>(parent?.Tree, commit.Tree, new[] { directory + "/" });
            return changes.Count > 0;
        }
    }
}
