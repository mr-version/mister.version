using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LibGit2Sharp;
using Mister.Version.Core.Models;

namespace Mister.Version.Core.Services
{
    public interface IGitService : IDisposable
    {
        Repository Repository { get; }
        string CurrentBranch { get; }
        BranchType GetBranchType(string branchName);
        SemVer ExtractReleaseVersion(string branchName, string tagPrefix);
        VersionTag GetGlobalVersionTag(BranchType branchType, string tagPrefix);
        VersionTag GetProjectVersionTag(string projectName, BranchType branchType, string tagPrefix);
        bool ProjectHasChangedSinceTag(Commit tagCommit, string projectPath, List<string> dependencies, string repoRoot, bool debug = false);
        int GetCommitHeight(Commit fromCommit, Commit toCommit = null);
        string GetCommitShortHash(Commit commit);
        SemVer ParseSemVer(string version);
        List<ChangeInfo> GetChangesSinceCommit(Commit sinceCommit, string projectPath = null);
    }

    public class GitService : IGitService
    {
        private Repository _repository;

        public Repository Repository => _repository;
        public string CurrentBranch => _repository.Head.FriendlyName;

        public GitService(string repoPath)
        {
            _repository = new Repository(repoPath);
        }

        public BranchType GetBranchType(string branchName)
        {
            if (branchName.Equals("main", StringComparison.OrdinalIgnoreCase) ||
                branchName.Equals("master", StringComparison.OrdinalIgnoreCase))
            {
                return BranchType.Main;
            }
            else if (branchName.Equals("dev", StringComparison.OrdinalIgnoreCase) ||
                     branchName.Equals("develop", StringComparison.OrdinalIgnoreCase) ||
                     branchName.Equals("development", StringComparison.OrdinalIgnoreCase))
            {
                return BranchType.Dev;
            }
            else if (branchName.StartsWith("release/", StringComparison.OrdinalIgnoreCase) ||
                     Regex.IsMatch(branchName, @"^v\d+\.\d+(\.\d+)?$", RegexOptions.IgnoreCase) ||
                     Regex.IsMatch(branchName, @"^release-\d+\.\d+(\.\d+)?$", RegexOptions.IgnoreCase))
            {
                return BranchType.Release;
            }
            else
            {
                return BranchType.Feature;
            }
        }

        public SemVer ExtractReleaseVersion(string branchName, string tagPrefix)
        {
            string versionPart = branchName;

            if (branchName.StartsWith("release/", StringComparison.OrdinalIgnoreCase))
            {
                versionPart = branchName.Substring("release/".Length);
            }
            else if (branchName.StartsWith("release-", StringComparison.OrdinalIgnoreCase))
            {
                versionPart = branchName.Substring("release-".Length);
            }

            if (versionPart.StartsWith(tagPrefix, StringComparison.OrdinalIgnoreCase))
            {
                versionPart = versionPart.Substring(tagPrefix.Length);
            }

            return ParseSemVer(versionPart);
        }

        public VersionTag GetGlobalVersionTag(BranchType branchType, string tagPrefix)
        {
            var globalVersionTags = _repository.Tags
                .Where(t => t.FriendlyName.StartsWith(tagPrefix, StringComparison.OrdinalIgnoreCase))
                .Where(t => !t.FriendlyName.Contains("-")) // Exclude project-specific tags
                .Select(t => new VersionTag
                {
                    Tag = t,
                    SemVer = ParseSemVer(t.FriendlyName.Substring(tagPrefix.Length)),
                    Commit = t.Target as Commit,
                    IsGlobal = true
                })
                .Where(vt => vt.SemVer != null)
                .OrderByDescending(vt => vt.SemVer.Major)
                .ThenByDescending(vt => vt.SemVer.Minor)
                .ThenByDescending(vt => vt.SemVer.Patch)
                .ToList();

            // Filter tags based on branch type
            if (branchType == BranchType.Release)
            {
                var releaseVersion = ExtractReleaseVersion(_repository.Head.FriendlyName, tagPrefix);
                if (releaseVersion != null)
                {
                    globalVersionTags = globalVersionTags
                        .Where(vt => vt.SemVer.Major == releaseVersion.Major &&
                                    vt.SemVer.Minor == releaseVersion.Minor)
                        .ToList();
                }
            }

            if (globalVersionTags.Any())
            {
                return globalVersionTags.First();
            }

            // Default version if no tags found
            return new VersionTag
            {
                SemVer = new SemVer { Major = 0, Minor = 1, Patch = 0 },
                IsGlobal = true
            };
        }

        public VersionTag GetProjectVersionTag(string projectName, BranchType branchType, string tagPrefix)
        {
            var projectSuffix = $"-{projectName.ToLowerInvariant()}";

            var projectVersionTags = _repository.Tags
                .Where(t => t.FriendlyName.StartsWith(tagPrefix, StringComparison.OrdinalIgnoreCase))
                .Where(t => t.FriendlyName.ToLowerInvariant().EndsWith(projectSuffix))
                .Select(t =>
                {
                    var tagName = t.FriendlyName;
                    var versionPart = tagName.Substring(tagPrefix.Length, tagName.Length - tagPrefix.Length - projectSuffix.Length);

                    return new VersionTag
                    {
                        Tag = t,
                        SemVer = ParseSemVer(versionPart),
                        Commit = t.Target as Commit,
                        IsGlobal = false,
                        ProjectName = projectName
                    };
                })
                .Where(vt => vt.SemVer != null)
                .OrderByDescending(vt => vt.SemVer.Major)
                .ThenByDescending(vt => vt.SemVer.Minor)
                .ThenByDescending(vt => vt.SemVer.Patch)
                .ToList();

            // Filter tags based on branch type
            if (branchType == BranchType.Release)
            {
                var releaseVersion = ExtractReleaseVersion(_repository.Head.FriendlyName, tagPrefix);
                if (releaseVersion != null)
                {
                    projectVersionTags = projectVersionTags
                        .Where(vt => vt.SemVer.Major == releaseVersion.Major &&
                                    vt.SemVer.Minor == releaseVersion.Minor)
                        .ToList();
                }
            }

            return projectVersionTags.FirstOrDefault();
        }

        public bool ProjectHasChangedSinceTag(Commit tagCommit, string projectPath, List<string> dependencies, string repoRoot, bool debug = false)
        {
            if (tagCommit == null)
                return true;

            try
            {
                var compareOptions = new CompareOptions();
                var diff = _repository.Diff.Compare<TreeChanges>(
                    tagCommit.Tree,
                    _repository.Head.Tip.Tree,
                    compareOptions);

                string normalizedProjectPath = NormalizePath(projectPath);

                // Check for direct changes in project
                bool hasChanges = diff.Any(c => NormalizePath(c.Path).StartsWith(normalizedProjectPath));

                if (hasChanges && debug)
                {
                    var changedFiles = diff.Where(c => NormalizePath(c.Path).StartsWith(normalizedProjectPath))
                        .Take(5)
                        .Select(c => $"{c.Path} ({c.Status})");
                    // Log would need to be passed in or abstracted
                }

                // Check for dependency changes
                if (!hasChanges && dependencies != null)
                {
                    foreach (var dependency in dependencies)
                    {
#if NET472
#if NET472
                        var relativeDependencyPath = NormalizePath(Mister.Version.Core.PathUtils.GetRelativePath(repoRoot, dependency));
#else
                        var relativeDependencyPath = NormalizePath(Path.GetRelativePath(repoRoot, dependency));
#endif
#else
                        var relativeDependencyPath = NormalizePath(Path.GetRelativePath(repoRoot, dependency));
#endif
                        string dependencyDirectory = Path.GetDirectoryName(relativeDependencyPath) ?? relativeDependencyPath;

                        var dependencyChanges = diff.Where(c => NormalizePath(c.Path).StartsWith(dependencyDirectory)).ToList();
                        if (dependencyChanges.Any())
                        {
                            hasChanges = true;
                            break;
                        }
                    }
                }

                // Check for packages.lock.json changes
                if (!hasChanges)
                {
                    string projectDir = Path.GetDirectoryName(projectPath);
                    string lockFilePath = projectDir == "" ? "packages.lock.json" : Path.Combine(projectDir, "packages.lock.json");
                    string normalizedLockFilePath = NormalizePath(lockFilePath);

                    foreach (var change in diff)
                    {
                        if (NormalizePath(change.Path).Equals(normalizedLockFilePath, StringComparison.OrdinalIgnoreCase))
                        {
                            hasChanges = true;
                            break;
                        }
                    }
                }

                return hasChanges;
            }
            catch (Exception)
            {
                return true; // Assume changes if we can't determine
            }
        }

        public int GetCommitHeight(Commit fromCommit, Commit toCommit = null)
        {
            try
            {
                if (fromCommit == null)
                    return 0;

                toCommit = toCommit ?? _repository.Head.Tip;

                var filter = new CommitFilter
                {
                    ExcludeReachableFrom = fromCommit,
                    IncludeReachableFrom = toCommit
                };

                return _repository.Commits.QueryBy(filter).Count();
            }
            catch
            {
                return 0;
            }
        }

        public string GetCommitShortHash(Commit commit)
        {
            if (commit == null) return "0000000";
            return commit.Sha.Substring(0, 7);
        }

        public SemVer ParseSemVer(string version)
        {
            if (string.IsNullOrEmpty(version))
                return null;

            // Handle semver with prerelease and build metadata
            var match = Regex.Match(version, @"^(\d+)\.(\d+)(?:\.(\d+))?(?:-([0-9A-Za-z\-\.]+))?(?:\+([0-9A-Za-z\-\.]+))?$");
            if (!match.Success)
                return null;

            return new SemVer
            {
                Major = int.Parse(match.Groups[1].Value),
                Minor = int.Parse(match.Groups[2].Value),
                Patch = match.Groups.Count > 3 && match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0,
                PreRelease = match.Groups.Count > 4 && match.Groups[4].Success ? match.Groups[4].Value : null,
                BuildMetadata = match.Groups.Count > 5 && match.Groups[5].Success ? match.Groups[5].Value : null
            };
        }

        public List<ChangeInfo> GetChangesSinceCommit(Commit sinceCommit, string projectPath = null)
        {
            var changes = new List<ChangeInfo>();

            if (sinceCommit == null)
                return changes;

            try
            {
                var compareOptions = new CompareOptions();
                var diff = _repository.Diff.Compare<TreeChanges>(
                    sinceCommit.Tree,
                    _repository.Head.Tip.Tree,
                    compareOptions);

                foreach (var change in diff)
                {
                    var changeInfo = new ChangeInfo
                    {
                        FilePath = change.Path,
                        ChangeType = change.Status.ToString(),
                        IsPackageLockFile = Path.GetFileName(change.Path).Equals("packages.lock.json", StringComparison.OrdinalIgnoreCase)
                    };

                    if (!string.IsNullOrEmpty(projectPath))
                    {
                        var normalizedProjectPath = NormalizePath(projectPath);
                        var normalizedChangePath = NormalizePath(change.Path);
                        
                        if (normalizedChangePath.StartsWith(normalizedProjectPath))
                        {
                            changeInfo.ProjectName = Path.GetFileName(projectPath);
                        }
                    }

                    changes.Add(changeInfo);
                }
            }
            catch (Exception)
            {
                // Return empty list on error
            }

            return changes;
        }

        private string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            return path.Replace('\\', '/');
        }

        public void Dispose()
        {
            _repository?.Dispose();
        }
    }
}