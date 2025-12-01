using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibGit2Sharp;
using Mister.Version.Core.Models;

namespace Mister.Version.Core.Services
{
    /// <summary>
    /// Lightweight service for detecting changes in a project without calculating a full version.
    /// </summary>
    public class HasChangesService : IDisposable
    {
        private readonly IGitService _gitService;
        private readonly Action<string, string> _logger;
        private readonly string _repoRoot;
        private bool _disposed = false;

        public HasChangesService(string repoRoot, Action<string, string> logger)
        {
            _repoRoot = repoRoot;
            _logger = logger ?? ((level, message) => { });
            _gitService = new GitService(repoRoot, null, _logger);
        }

        /// <summary>
        /// Detects changes for a project since a specific tag or commit.
        /// </summary>
        /// <param name="request">Change detection request</param>
        /// <returns>Change detection result</returns>
        public HasChangesResult DetectChanges(HasChangesRequest request)
        {
            try
            {
                _logger("Debug", $"[HasChanges] Starting change detection for: {request.ProjectPath}");

                var projectName = Path.GetFileNameWithoutExtension(request.ProjectPath);

                // Find the commit to compare against
                Commit compareCommit = null;
                string comparedAgainst = null;

                if (!string.IsNullOrEmpty(request.SinceCommit))
                {
                    // Use specific commit SHA
                    compareCommit = FindCommitBySha(request.SinceCommit);
                    comparedAgainst = request.SinceCommit.Length > 7
                        ? request.SinceCommit.Substring(0, 7)
                        : request.SinceCommit;

                    if (compareCommit == null)
                    {
                        _logger("Warning", $"[HasChanges] Commit {request.SinceCommit} not found");
                        return new HasChangesResult
                        {
                            HasChanges = true,
                            ChangeType = VersionBumpType.Patch,
                            Reason = $"Commit {request.SinceCommit} not found, assuming changes",
                            ComparedAgainst = "unknown"
                        };
                    }
                }
                else if (!string.IsNullOrEmpty(request.SinceTag))
                {
                    // Use specific tag
                    var tag = FindTag(request.SinceTag);
                    if (tag != null)
                    {
                        compareCommit = PeelTagToCommit(tag);
                        comparedAgainst = request.SinceTag;
                    }
                    else
                    {
                        _logger("Warning", $"[HasChanges] Tag {request.SinceTag} not found");
                        return new HasChangesResult
                        {
                            HasChanges = true,
                            ChangeType = VersionBumpType.Patch,
                            Reason = $"Tag {request.SinceTag} not found, assuming changes",
                            ComparedAgainst = "unknown"
                        };
                    }
                }
                else
                {
                    // Find the latest version tag for this project
                    var branchType = _gitService.GetBranchType(_gitService.CurrentBranch);
                    var versionOptions = new VersionOptions
                    {
                        TagPrefix = request.TagPrefix,
                        ProjectName = projectName
                    };

                    // Try project-specific tag first
                    var projectTag = _gitService.GetProjectVersionTag(projectName, branchType, request.TagPrefix, versionOptions);
                    if (projectTag?.Commit != null)
                    {
                        compareCommit = projectTag.Commit;
                        comparedAgainst = projectTag.Tag?.FriendlyName ?? $"{projectName}-{request.TagPrefix}{projectTag.SemVer}";
                    }
                    else
                    {
                        // Fall back to global tag
                        var globalTag = _gitService.GetGlobalVersionTag(branchType, versionOptions);
                        if (globalTag?.Commit != null)
                        {
                            compareCommit = globalTag.Commit;
                            comparedAgainst = globalTag.Tag?.FriendlyName ?? $"{request.TagPrefix}{globalTag.SemVer}";
                        }
                    }
                }

                // If no commit to compare against, assume this is a new project
                if (compareCommit == null)
                {
                    _logger("Info", "[HasChanges] No previous version tag found, treating as new project");
                    return new HasChangesResult
                    {
                        HasChanges = true,
                        ChangeType = VersionBumpType.Minor,
                        Reason = "No previous version tag found (new project)",
                        ComparedAgainst = "none",
                        ChangedFiles = GetAllProjectFiles(request)
                    };
                }

                // Get relative project path
                var projectDir = Path.GetDirectoryName(request.ProjectPath);
                string relativeProjectPath;
#if NET472
                relativeProjectPath = PathUtils.GetRelativePath(request.RepoRoot, projectDir ?? request.ProjectPath);
#else
                relativeProjectPath = Path.GetRelativePath(request.RepoRoot, projectDir ?? request.ProjectPath);
#endif

                // Check if change detection with classification is enabled
                if (request.ChangeDetectionEnabled)
                {
                    var config = new ChangeDetectionConfig
                    {
                        Enabled = true,
                        IgnorePatterns = request.IgnorePatterns,
                        MajorPatterns = request.MajorPatterns,
                        MinorPatterns = request.MinorPatterns,
                        PatchPatterns = request.PatchPatterns,
                        AdditionalMonitorPaths = request.AdditionalMonitorPaths
                    };

                    var classification = _gitService.ClassifyProjectChanges(
                        compareCommit,
                        relativeProjectPath,
                        request.Dependencies,
                        request.RepoRoot,
                        config);

                    var changedFiles = new List<string>();
                    changedFiles.AddRange(classification.MajorFiles ?? new List<string>());
                    changedFiles.AddRange(classification.MinorFiles ?? new List<string>());
                    changedFiles.AddRange(classification.PatchFiles ?? new List<string>());
                    changedFiles.AddRange(classification.UnclassifiedFiles ?? new List<string>());

                    return new HasChangesResult
                    {
                        HasChanges = !classification.ShouldIgnore && changedFiles.Count > 0,
                        ChangeType = classification.RequiredBumpType,
                        Reason = classification.Reason,
                        ComparedAgainst = comparedAgainst,
                        ChangedFiles = changedFiles,
                        MajorFiles = classification.MajorFiles ?? new List<string>(),
                        MinorFiles = classification.MinorFiles ?? new List<string>(),
                        PatchFiles = classification.PatchFiles ?? new List<string>(),
                        IgnoredFiles = classification.IgnoredFiles ?? new List<string>()
                    };
                }
                else
                {
                    // Simple change detection without classification
                    var hasChanges = _gitService.ProjectHasChangedSinceTag(
                        compareCommit,
                        relativeProjectPath,
                        request.Dependencies,
                        request.RepoRoot,
                        request.Debug);

                    if (hasChanges)
                    {
                        // Get the list of changed files
                        var changedFiles = GetChangedFiles(compareCommit, relativeProjectPath, request);

                        return new HasChangesResult
                        {
                            HasChanges = true,
                            ChangeType = VersionBumpType.Patch,
                            Reason = $"Changes detected since {comparedAgainst}",
                            ComparedAgainst = comparedAgainst,
                            ChangedFiles = changedFiles
                        };
                    }
                    else
                    {
                        return new HasChangesResult
                        {
                            HasChanges = false,
                            ChangeType = VersionBumpType.None,
                            Reason = $"No changes since {comparedAgainst}",
                            ComparedAgainst = comparedAgainst,
                            ChangedFiles = new List<string>()
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger("Error", $"[HasChanges] Failed to detect changes: {ex.Message}");
                return new HasChangesResult
                {
                    HasChanges = true,
                    ChangeType = VersionBumpType.Patch,
                    Reason = $"Error detecting changes: {ex.Message}",
                    ComparedAgainst = "error"
                };
            }
        }

        private Commit FindCommitBySha(string sha)
        {
            try
            {
                return _gitService.Repository.Lookup<Commit>(sha);
            }
            catch
            {
                return null;
            }
        }

        private Tag FindTag(string tagName)
        {
            return _gitService.Repository.Tags.FirstOrDefault(t =>
                t.FriendlyName.Equals(tagName, StringComparison.OrdinalIgnoreCase));
        }

        private Commit PeelTagToCommit(Tag tag)
        {
            GitObject target = tag.Target;
            while (target is TagAnnotation annotation)
                target = annotation.Target;
            return target as Commit;
        }

        private List<string> GetChangedFiles(Commit sinceCommit, string projectPath, HasChangesRequest request)
        {
            var changedFiles = new List<string>();

            try
            {
                var headCommit = _gitService.Repository.Head.Tip;
                if (headCommit == null)
                    return changedFiles;

                var diff = _gitService.Repository.Diff.Compare<TreeChanges>(
                    sinceCommit.Tree,
                    headCommit.Tree);

                var normalizedProjectPath = NormalizePath(projectPath);

                // Add direct project changes
                foreach (var change in diff)
                {
                    var normalizedPath = NormalizePath(change.Path);
                    if (normalizedPath.StartsWith(normalizedProjectPath))
                    {
                        changedFiles.Add(change.Path);
                    }
                }

                // Add dependency changes
                if (request.Dependencies != null)
                {
                    foreach (var dependency in request.Dependencies)
                    {
#if NET472
                        var relativeDependencyPath = NormalizePath(PathUtils.GetRelativePath(request.RepoRoot, dependency));
#else
                        var relativeDependencyPath = NormalizePath(Path.GetRelativePath(request.RepoRoot, dependency));
#endif
                        var dependencyDirectory = NormalizePath(Path.GetDirectoryName(relativeDependencyPath) ?? relativeDependencyPath);

                        foreach (var change in diff)
                        {
                            var normalizedPath = NormalizePath(change.Path);
                            if (normalizedPath.StartsWith(dependencyDirectory))
                            {
                                changedFiles.Add(change.Path);
                            }
                        }
                    }
                }

                // Add additional monitor path changes
                if (request.AdditionalMonitorPaths != null)
                {
                    foreach (var monitorPath in request.AdditionalMonitorPaths)
                    {
                        if (string.IsNullOrWhiteSpace(monitorPath))
                            continue;

                        string normalizedMonitorPath;
                        if (Path.IsPathRooted(monitorPath))
                        {
#if NET472
                            normalizedMonitorPath = NormalizePath(PathUtils.GetRelativePath(request.RepoRoot, monitorPath));
#else
                            normalizedMonitorPath = NormalizePath(Path.GetRelativePath(request.RepoRoot, monitorPath));
#endif
                        }
                        else
                        {
                            normalizedMonitorPath = NormalizePath(monitorPath);
                        }

                        foreach (var change in diff)
                        {
                            var normalizedPath = NormalizePath(change.Path);
                            if (normalizedPath.StartsWith(normalizedMonitorPath))
                            {
                                changedFiles.Add(change.Path);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger("Warning", $"[HasChanges] Error getting changed files: {ex.Message}");
            }

            return changedFiles.Distinct().ToList();
        }

        private List<string> GetAllProjectFiles(HasChangesRequest request)
        {
            var files = new List<string>();
            try
            {
                var projectDir = Path.GetDirectoryName(request.ProjectPath);
                if (!string.IsNullOrEmpty(projectDir) && Directory.Exists(projectDir))
                {
                    files.AddRange(Directory.GetFiles(projectDir, "*", SearchOption.AllDirectories)
                        .Select(f => Path.GetRelativePath(request.RepoRoot, f)));
                }
            }
            catch
            {
                // Ignore errors reading files
            }
            return files;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;
            return path.Replace('\\', '/');
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _gitService?.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Request object for change detection
    /// </summary>
    public class HasChangesRequest
    {
        public string ProjectPath { get; set; }
        public string RepoRoot { get; set; }
        public List<string> Dependencies { get; set; }
        public string TagPrefix { get; set; } = "v";
        public string SinceTag { get; set; }
        public string SinceCommit { get; set; }
        public bool ChangeDetectionEnabled { get; set; } = false;
        public List<string> IgnorePatterns { get; set; }
        public List<string> MajorPatterns { get; set; }
        public List<string> MinorPatterns { get; set; }
        public List<string> PatchPatterns { get; set; }
        public List<string> AdditionalMonitorPaths { get; set; }
        public bool Debug { get; set; } = false;
    }

    /// <summary>
    /// Result object for change detection
    /// </summary>
    public class HasChangesResult
    {
        public bool HasChanges { get; set; }
        public VersionBumpType ChangeType { get; set; }
        public string Reason { get; set; }
        public string ComparedAgainst { get; set; }
        public List<string> ChangedFiles { get; set; } = new List<string>();
        public List<string> MajorFiles { get; set; } = new List<string>();
        public List<string> MinorFiles { get; set; } = new List<string>();
        public List<string> PatchFiles { get; set; } = new List<string>();
        public List<string> IgnoredFiles { get; set; } = new List<string>();
    }
}
