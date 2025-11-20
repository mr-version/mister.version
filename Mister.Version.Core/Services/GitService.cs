using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LibGit2Sharp;
using Mister.Version.Core.Models;

namespace Mister.Version.Core.Services
{
    public static class GitRepositoryHelper
    {
        /// <summary>
        /// Discovers the Git repository root using LibGit2Sharp and normalizes the path.
        /// This method handles the case where Repository.Discover returns the .git directory
        /// instead of the working directory.
        /// </summary>
        /// <param name="startPath">The path to start searching from</param>
        /// <returns>The normalized path to the Git repository working directory, or null if not found</returns>
        public static string DiscoverRepositoryRoot(string startPath)
        {
            if (string.IsNullOrEmpty(startPath))
                return null;

            try
            {
                // Use LibGit2Sharp to discover the repository
                var discoveredPath = Repository.Discover(startPath);
                if (discoveredPath == null)
                    return null;

                // Remove trailing slashes for consistency
                var gitRepoRoot = discoveredPath.TrimEnd('/', '\\');

                // If the path ends with .git, get the parent directory (the actual repo root)
                if (gitRepoRoot.EndsWith(".git"))
                {
                    gitRepoRoot = Path.GetDirectoryName(gitRepoRoot);
                }

                return gitRepoRoot;
            }
            catch
            {
                // Return null if discovery fails
                return null;
            }
        }
    }

    public interface IGitService : IDisposable
    {
        Repository Repository { get; }
        string CurrentBranch { get; }
        bool IsShallowClone { get; }
        BranchType GetBranchType(string branchName);
        SemVer ExtractReleaseVersion(string branchName, string tagPrefix);
        VersionTag GetGlobalVersionTag(BranchType branchType, VersionOptions options);
        VersionTag GetProjectVersionTag(string projectName, BranchType branchType, string tagPrefix, VersionOptions options = null);
        bool ProjectHasChangedSinceTag(Commit tagCommit, string projectPath, List<string> dependencies, string repoRoot, bool debug = false);
        ChangeClassification ClassifyProjectChanges(Commit tagCommit, string projectPath, List<string> dependencies, string repoRoot, ChangeDetectionConfig config);
        int GetCommitHeight(Commit fromCommit, Commit toCommit = null);
        string GetCommitShortHash(Commit commit);
        SemVer ParseSemVer(string version);
        SemVer AddBranchMetadata(SemVer version, string branchName, GitIntegrationConfig config = null);
        List<ChangeInfo> GetChangesSinceCommit(Commit sinceCommit, string projectPath = null);
        bool CreateTag(string tagName, string message, bool isGlobalTag, string projectName = null, bool dryRun = false);
        bool TagExists(string tagName);
        bool HasSubmoduleChanges(Commit fromCommit, Commit toCommit = null);
        bool IsCommitReachable(Commit fromCommit, Commit toCommit = null);
    }

    public class GitService : IGitService
    {
        // Constants for version parsing and formatting
        private const int SHORT_HASH_LENGTH = 7;
        private const int DEFAULT_PRERELEASE_PRECEDENCE = 999; // No prerelease = highest precedence
        private const int RC_PRERELEASE_PRECEDENCE = 3;
        private const int BETA_PRERELEASE_PRECEDENCE = 2;
        private const int ALPHA_PRERELEASE_PRECEDENCE = 1;
        private const int UNKNOWN_PRERELEASE_PRECEDENCE = 0;

        private Repository _repository;
        private VersionCache _cache;
        private Action<string, string> _logger;

        public Repository Repository => _repository;
        public string CurrentBranch => _repository?.Head?.FriendlyName ?? "HEAD";
        public bool IsShallowClone => _repository?.Info?.IsShallow ?? false;
        public VersionCache Cache
        {
            get => _cache;
            set => _cache = value;
        }

        public GitService(string repoPath) : this(repoPath, null, null)
        {
        }

        public GitService(string repoPath, VersionCache cache) : this(repoPath, cache, null)
        {
        }

        public GitService(string repoPath, VersionCache cache, Action<string, string> logger)
        {
            Repository repo = null;
            try
            {
                repo = new Repository(repoPath);
                _repository = repo;
                _cache = cache;
                _logger = logger ?? ((level, message) => { }); // Default no-op logger
            }
            catch
            {
                repo?.Dispose();
                throw;
            }
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

        public VersionTag GetGlobalVersionTag(BranchType branchType, VersionOptions options)
        {
            // Check for shallow clone and log warning
            if (IsShallowClone && options.GitIntegration?.ShallowCloneSupport == true)
            {
                _logger?.Invoke("Info", "Repository is a shallow clone - version tag history may be limited");
            }

            var globalVersionTags = _repository.Tags
                .Where(t => t.FriendlyName.StartsWith(options.TagPrefix, StringComparison.OrdinalIgnoreCase))
                .Where(t =>
                {
                    // Global tags: v1.0.0, v1.0.0-alpha.1 (no project name prefix)
                    // Project-specific tags: ProjectName-v1.0.0, v1.0.0-projectname
                    var tagName = t.FriendlyName;

                    // Validate tag name is long enough to contain version after prefix
                    if (tagName.Length <= options.TagPrefix.Length)
                        return false;

                    // Since we don't support suffix format for project tags anymore,
                    // all tags starting with the tag prefix are potentially global
                    // (including v1.2.3-projectname which could be feature branch tags)

                    return true; // Treat as global
                })
                .Select(t =>
                {
                    var tagName = t.FriendlyName;
                    string versionPart = tagName.Length > options.TagPrefix.Length
                        ? tagName.Substring(options.TagPrefix.Length)
                        : "";

                    // Peel annotated tags to get the actual commit
                    GitObject target = t.Target;
                    while (target is TagAnnotation annotation)
                        target = annotation.Target;

                    return new VersionTag
                    {
                        Tag = t,
                        SemVer = ParseSemVer(versionPart),
                        Commit = target as Commit,
                        IsGlobal = true
                    };
                })
                .Where(vt => vt.SemVer != null)
                .ToList();

            // Filter by tag ancestry if validation is enabled and not a shallow clone
            if (options.GitIntegration?.ValidateTagAncestry == true && !IsShallowClone)
            {
                globalVersionTags = globalVersionTags
                    .Where(vt => vt.Commit == null || IsCommitReachable(vt.Commit))
                    .ToList();
            }

            // Sort version tags by precedence
            globalVersionTags = SortVersionTagsByPrecedence(globalVersionTags).ToList();

            // Filter tags based on branch type
            if (branchType == BranchType.Release)
            {
                // Check for detached HEAD state
                if (_repository.Head.IsDetached)
                {
                    _logger?.Invoke("Warning", "Repository is in detached HEAD state");
                    // Skip release branch filtering in detached HEAD state
                }
                else
                {
                    var releaseVersion = ExtractReleaseVersion(_repository.Head.FriendlyName, options.TagPrefix);
                    if (releaseVersion != null)
                    {
                        globalVersionTags = globalVersionTags
                            .Where(vt => vt.SemVer.Major == releaseVersion.Major &&
                                        vt.SemVer.Minor == releaseVersion.Minor)
                            .ToList();
                    }
                }
            }

            // Consider BaseVersion from config as a potential global version
            if (!string.IsNullOrEmpty(options.BaseVersion))
            {
                var configBaseSemVer = ParseSemVer(options.BaseVersion);
                if (configBaseSemVer != null)
                {
                    var configVersionTag = new VersionTag
                    {
                        SemVer = configBaseSemVer,
                        IsGlobal = true,
                        Commit = null // No commit for config-based version
                    };
                    
                    // Add config version to the list for comparison
                    var allVersionTags = globalVersionTags.ToList();
                    allVersionTags.Add(configVersionTag);

                    // Re-sort with config version included
                    var sortedTags = SortVersionTagsByPrecedence(allVersionTags);

                    return sortedTags.First();
                }
            }

            if (globalVersionTags.Any())
            {
                return globalVersionTags.First();
            }

            // Check for shallow clone fallback version
            if (IsShallowClone && options.GitIntegration?.ShallowCloneSupport == true &&
                !string.IsNullOrEmpty(options.GitIntegration.ShallowCloneFallbackVersion))
            {
                var fallbackSemVer = ParseSemVer(options.GitIntegration.ShallowCloneFallbackVersion);
                if (fallbackSemVer != null)
                {
                    _logger?.Invoke("Info", $"Using shallow clone fallback version: {options.GitIntegration.ShallowCloneFallbackVersion}");
                    return new VersionTag
                    {
                        SemVer = fallbackSemVer,
                        IsGlobal = true,
                        Commit = null
                    };
                }
            }

            // Default version if no tags found and no config BaseVersion
            return new VersionTag
            {
                SemVer = new SemVer { Major = 0, Minor = 1, Patch = 0 },
                IsGlobal = true
            };
        }

        public VersionTag GetProjectVersionTag(string projectName, BranchType branchType, string tagPrefix, VersionOptions options = null)
        {
            // Check cache first
            var cacheKey = $"{projectName}_{branchType}_{tagPrefix}";
            if (_cache != null)
            {
                var cachedTag = _cache.GetProjectVersionTag(cacheKey);
                if (cachedTag != null)
                {
                    return cachedTag;
                }
            }

            // Build possible prefixes list from standard patterns and custom patterns
            var possiblePrefixes = new List<string>
            {
                $"{projectName.ToLowerInvariant()}-{tagPrefix}",
                $"{projectName}-{tagPrefix}",
                $"{projectName}/{tagPrefix}",
                $"{projectName.ToLowerInvariant()}/{tagPrefix}",
            };

            // Add custom tag patterns if configured
            if (options?.GitIntegration?.CustomTagPatterns != null)
            {
                foreach (var pattern in options.GitIntegration.CustomTagPatterns)
                {
                    // Parse pattern: "ProjectName={name}-{prefix}{version}" or "{name}/{prefix}{version}"
                    var parts = pattern.Split('=');
                    if (parts.Length == 2)
                    {
                        var patternProjectName = parts[0].Trim();
                        var patternFormat = parts[1].Trim();

                        // Check if this pattern applies to current project
                        if (patternProjectName.Equals(projectName, StringComparison.OrdinalIgnoreCase) ||
                            patternProjectName == "*")
                        {
                            // Replace placeholders with actual values (prefix only, version will be parsed later)
                            var customPrefix = patternFormat
                                .Replace("{name}", projectName)
                                .Replace("{prefix}", tagPrefix)
                                .Replace("{version}", ""); // Remove version placeholder to get prefix

                            if (!string.IsNullOrWhiteSpace(customPrefix))
                            {
                                possiblePrefixes.Add(customPrefix);
                            }
                        }
                    }
                }
            }

            // Note: We don't support suffix format (v1.2.3-projectname) as it conflicts with feature branch tags

            var projectVersionTags = _repository.Tags
                .Where(t =>
                    // Check prefix formats: ProjectName-v1.0.0, ProjectName/v1.0.0
                    possiblePrefixes.Any(prefix =>
                        t.FriendlyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                .Select(t =>
                {
                    var tagName = t.FriendlyName;
                    string versionPart = null;

                    // Extract version part from prefix format
                    foreach (var prefix in possiblePrefixes)
                    {
                        if (tagName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                            tagName.Length > prefix.Length)
                        {
                            versionPart = tagName.Substring(prefix.Length);
                            break;
                        }
                    }

                    // Peel annotated tags to get the actual commit
                    GitObject target = t.Target;
                    while (target is TagAnnotation annotation)
                        target = annotation.Target;

                    return new VersionTag
                    {
                        Tag = t,
                        SemVer = ParseSemVer(versionPart),
                        Commit = target as Commit,
                        IsGlobal = false,
                        ProjectName = projectName
                    };
                })
                .Where(vt => vt.SemVer != null)
                .ToList();

            // Filter by tag ancestry if validation is enabled and not a shallow clone
            if (options?.GitIntegration?.ValidateTagAncestry == true && !IsShallowClone)
            {
                projectVersionTags = projectVersionTags
                    .Where(vt => vt.Commit == null || IsCommitReachable(vt.Commit))
                    .ToList();
            }

            // Sort version tags by precedence
            projectVersionTags = SortVersionTagsByPrecedence(projectVersionTags).ToList();

            // Filter tags based on branch type
            if (branchType == BranchType.Release)
            {
                // Check for detached HEAD state
                if (_repository.Head.IsDetached)
                {
                    _logger?.Invoke("Warning", "Repository is in detached HEAD state");
                    // Skip release branch filtering in detached HEAD state
                }
                else
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
            }

            var result = projectVersionTags.FirstOrDefault();

            // Cache the result (even if null)
            if (_cache != null)
            {
                _cache.SetProjectVersionTag(cacheKey, result);
            }

            return result;
        }

        public bool ProjectHasChangedSinceTag(Commit tagCommit, string projectPath, List<string> dependencies, string repoRoot, bool debug = false)
        {
            if (tagCommit == null)
                return true;

            // Validate HEAD and Tip are available
            if (_repository?.Head?.Tip == null)
            {
                _logger?.Invoke("Warning", "Repository HEAD or Tip is null, assuming changes exist");
                return true;
            }

            // Create cache key from commit SHA, project path, and dependencies
            var commitSha = tagCommit.Sha;
            var depKey = dependencies != null && dependencies.Any()
                ? string.Join("|", dependencies.OrderBy(d => d))
                : "";
            var cacheKey = $"{commitSha}_{projectPath}_{depKey}";

            // Check cache first
            if (_cache != null)
            {
                var cachedResult = _cache.GetProjectHasChanges(cacheKey);
                if (cachedResult.HasValue)
                {
                    return cachedResult.Value;
                }
            }

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
                        var relativeDependencyPath = NormalizePath(Mister.Version.Core.PathUtils.GetRelativePath(repoRoot, dependency));
#else
                        var relativeDependencyPath = NormalizePath(Path.GetRelativePath(repoRoot, dependency));
#endif
                        string dependencyDirectory = NormalizePath(Path.GetDirectoryName(relativeDependencyPath) ?? relativeDependencyPath);

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
                    if (string.IsNullOrEmpty(projectDir))
                    {
                        projectDir = projectPath; // Use project path itself if at root
                    }
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

                // Cache the result
                if (_cache != null)
                {
                    _cache.SetProjectHasChanges(cacheKey, hasChanges);
                }

                return hasChanges;
            }
            catch (Exception ex)
            {
                // Log the exception for debugging
                _logger?.Invoke("Warning", $"Error detecting changes: {ex.Message}");
                // Don't cache exception results as they may be transient
                return true; // Assume changes if we can't determine
            }
        }

        public ChangeClassification ClassifyProjectChanges(Commit tagCommit, string projectPath, List<string> dependencies, string repoRoot, ChangeDetectionConfig config)
        {
            var classification = new ChangeClassification();

            if (config == null || !config.Enabled)
            {
                // If change detection is disabled, just return whether project has changes
                var hasChanges = ProjectHasChangedSinceTag(tagCommit, projectPath, dependencies, repoRoot);
                classification.ShouldIgnore = !hasChanges;
                classification.RequiredBumpType = hasChanges ? VersionBumpType.Patch : VersionBumpType.None;
                classification.Reason = hasChanges ? "Changes detected (pattern matching disabled)" : "No changes detected";
                return classification;
            }

            if (tagCommit == null)
            {
                classification.ShouldIgnore = false;
                classification.RequiredBumpType = VersionBumpType.Patch;
                classification.Reason = "No tag commit (initial version)";
                return classification;
            }

            if (_repository?.Head?.Tip == null)
            {
                classification.ShouldIgnore = false;
                classification.RequiredBumpType = VersionBumpType.Patch;
                classification.Reason = "Repository HEAD or Tip is null, assuming changes exist";
                return classification;
            }

            try
            {
                var compareOptions = new CompareOptions();
                var diff = _repository.Diff.Compare<TreeChanges>(
                    tagCommit.Tree,
                    _repository.Head.Tip.Tree,
                    compareOptions);

                string normalizedProjectPath = NormalizePath(projectPath);

                // Collect all changed files for this project
                var changedFiles = new List<string>();

                // Add direct project changes
                changedFiles.AddRange(diff
                    .Where(c => NormalizePath(c.Path).StartsWith(normalizedProjectPath))
                    .Select(c => c.Path));

                // Add dependency changes
                if (dependencies != null)
                {
                    foreach (var dependency in dependencies)
                    {
#if NET472
                        var relativeDependencyPath = NormalizePath(Mister.Version.Core.PathUtils.GetRelativePath(repoRoot, dependency));
#else
                        var relativeDependencyPath = NormalizePath(Path.GetRelativePath(repoRoot, dependency));
#endif
                        string dependencyDirectory = NormalizePath(Path.GetDirectoryName(relativeDependencyPath) ?? relativeDependencyPath);

                        changedFiles.AddRange(diff
                            .Where(c => NormalizePath(c.Path).StartsWith(dependencyDirectory))
                            .Select(c => c.Path));
                    }
                }

                // Add packages.lock.json changes
                string projectDir = Path.GetDirectoryName(projectPath);
                if (string.IsNullOrEmpty(projectDir))
                {
                    projectDir = projectPath;
                }
                string lockFilePath = projectDir == "" ? "packages.lock.json" : Path.Combine(projectDir, "packages.lock.json");
                string normalizedLockFilePath = NormalizePath(lockFilePath);

                var lockFileChange = diff.FirstOrDefault(c =>
                    NormalizePath(c.Path).Equals(normalizedLockFilePath, StringComparison.OrdinalIgnoreCase));
                if (lockFileChange != null)
                {
                    changedFiles.Add(lockFileChange.Path);
                }

                // Remove duplicates
                changedFiles = changedFiles.Distinct().ToList();
                classification.TotalFiles = changedFiles.Count;

                if (changedFiles.Count == 0)
                {
                    classification.ShouldIgnore = true;
                    classification.RequiredBumpType = VersionBumpType.None;
                    classification.Reason = "No files changed";
                    return classification;
                }

                // Use pattern matcher to classify changes
                var patternMatcher = new FilePatternMatcher();
                classification = patternMatcher.ClassifyChanges(changedFiles, config);
                var bumpType = patternMatcher.DetermineBumpType(classification, config);

                return classification;
            }
            catch (Exception ex)
            {
                _logger?.Invoke("Warning", $"Error classifying changes: {ex.Message}");
                // On error, assume changes and use default bump type
                classification.ShouldIgnore = false;
                classification.RequiredBumpType = VersionBumpType.Patch;
                classification.Reason = $"Error classifying changes: {ex.Message}";
                return classification;
            }
        }

        public int GetCommitHeight(Commit fromCommit, Commit toCommit = null)
        {
            try
            {
                if (fromCommit == null)
                    return 0;

                toCommit = toCommit ?? _repository?.Head?.Tip;
                if (toCommit == null)
                {
                    _logger?.Invoke("Warning", "Cannot calculate commit height: HEAD tip is null");
                    return 0;
                }

                // Check cache first (use from and to commit SHAs as key)
                var cacheKey = $"{fromCommit.Sha}_{toCommit?.Sha ?? "HEAD"}";
                if (_cache != null)
                {
                    var cachedHeight = _cache.GetCommitHeight(cacheKey);
                    if (cachedHeight.HasValue)
                    {
                        return cachedHeight.Value;
                    }
                }

                var filter = new CommitFilter
                {
                    ExcludeReachableFrom = fromCommit,
                    IncludeReachableFrom = toCommit
                };

                var height = _repository.Commits.QueryBy(filter).Count();

                // Cache the result
                if (_cache != null)
                {
                    _cache.SetCommitHeight(cacheKey, height);
                }

                return height;
            }
            catch (Exception ex)
            {
                // Log the exception for debugging
                _logger?.Invoke("Warning", $"Error calculating commit height: {ex.Message}");
                // Return 0 if we can't calculate height (e.g., invalid commits, repository issues)
                // This prevents version calculation failures due to git errors
                return 0;
            }
        }

        public string GetCommitShortHash(Commit commit)
        {
            if (commit == null)
                return new string('0', SHORT_HASH_LENGTH);

            if (commit.Sha == null || commit.Sha.Length < SHORT_HASH_LENGTH)
            {
                _logger?.Invoke("Warning", $"Commit SHA is null or too short: {commit.Sha}");
                return new string('0', SHORT_HASH_LENGTH);
            }

            return commit.Sha.Substring(0, SHORT_HASH_LENGTH);
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

            // Validate HEAD and Tip are available
            if (_repository?.Head?.Tip == null)
            {
                _logger?.Invoke("Warning", "Cannot get changes: HEAD tip is null");
                return changes;
            }

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
            catch (Exception ex)
            {
                // Log the exception for debugging
                _logger?.Invoke("Warning", $"Error getting changes since commit: {ex.Message}");
                // Return empty list on error (e.g., commit not found, repository issues)
                // This allows the caller to continue with an assumption of no changes
            }

            return changes;
        }

        private string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            return path.Replace('\\', '/');
        }

        private int GetPrereleasePrecedence(string prerelease)
        {
            if (string.IsNullOrEmpty(prerelease)) return DEFAULT_PRERELEASE_PRECEDENCE;
            if (prerelease.StartsWith("rc.")) return RC_PRERELEASE_PRECEDENCE;
            if (prerelease.StartsWith("beta.")) return BETA_PRERELEASE_PRECEDENCE;
            if (prerelease.StartsWith("alpha.")) return ALPHA_PRERELEASE_PRECEDENCE;
            return UNKNOWN_PRERELEASE_PRECEDENCE;
        }

        private int GetPrereleaseNumber(string prerelease)
        {
            if (string.IsNullOrEmpty(prerelease)) return DEFAULT_PRERELEASE_PRECEDENCE;

            var match = Regex.Match(prerelease, @"\.(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var number))
            {
                return number;
            }
            return UNKNOWN_PRERELEASE_PRECEDENCE;
        }

        public SemVer AddBranchMetadata(SemVer version, string branchName, GitIntegrationConfig config = null)
        {
            if (version == null || string.IsNullOrWhiteSpace(branchName))
                return version;

            // Only add metadata if enabled in config
            if (config?.IncludeBranchInMetadata != true)
                return version;

            // Normalize branch name for metadata (remove invalid characters)
            var sanitizedBranch = SanitizeBranchName(branchName);

            // Don't add metadata for main/master/release branches (stable branches)
            var branchType = GetBranchType(branchName);
            if (branchType == BranchType.Main || branchType == BranchType.Release)
                return version;

            // Create new SemVer with branch metadata
            var newVersion = new SemVer
            {
                Major = version.Major,
                Minor = version.Minor,
                Patch = version.Patch,
                PreRelease = version.PreRelease,
                BuildMetadata = string.IsNullOrEmpty(version.BuildMetadata)
                    ? sanitizedBranch
                    : $"{version.BuildMetadata}.{sanitizedBranch}"
            };

            return newVersion;
        }

        private string SanitizeBranchName(string branchName)
        {
            if (string.IsNullOrWhiteSpace(branchName))
                return "unknown";

            // Remove common prefixes
            var sanitized = branchName
                .Replace("feature/", "")
                .Replace("hotfix/", "")
                .Replace("bugfix/", "")
                .Replace("dev/", "");

            // Replace invalid characters with hyphens
            sanitized = Regex.Replace(sanitized, @"[^a-zA-Z0-9\-]", "-");

            // Remove consecutive hyphens
            sanitized = Regex.Replace(sanitized, @"-+", "-");

            // Trim hyphens from start and end
            sanitized = sanitized.Trim('-');

            // Truncate if too long
            if (sanitized.Length > 20)
                sanitized = sanitized.Substring(0, 20).TrimEnd('-');

            return string.IsNullOrEmpty(sanitized) ? "branch" : sanitized.ToLowerInvariant();
        }

        public bool CreateTag(string tagName, string message, bool isGlobalTag, string projectName = null, bool dryRun = false)
        {
            try
            {
                // Validate tag name
                if (!IsValidTagName(tagName))
                {
                    _logger?.Invoke("Error", $"Invalid tag name: {tagName}");
                    return false;
                }

                // Check if tag already exists
                if (TagExists(tagName))
                {
                    if (dryRun)
                    {
                        Console.WriteLine($"[DRY RUN] Tag '{tagName}' already exists - would skip creation");
                    }
                    return false;
                }

                // Get the current HEAD commit
                var commit = _repository?.Head?.Tip;
                if (commit == null)
                {
                    _logger?.Invoke("Error", "No commits in repository");
                    throw new InvalidOperationException("No commits in repository");
                }

                if (dryRun)
                {
                    var commitHash = commit.Sha != null && commit.Sha.Length >= SHORT_HASH_LENGTH
                        ? commit.Sha.Substring(0, SHORT_HASH_LENGTH)
                        : "unknown";
                    Console.WriteLine($"[DRY RUN] Would create tag:");
                    Console.WriteLine($"  Tag Name: {tagName}");
                    Console.WriteLine($"  Message: {message}");
                    Console.WriteLine($"  Commit: {commitHash} - {commit.MessageShort}");
                    Console.WriteLine($"  Type: {(isGlobalTag ? "Global" : $"Project ({projectName})")}");
                    return true;
                }

                // Create the tag
                var tag = _repository.Tags.Add(tagName, commit, new Signature("Mister.Version", "mister.version@automated.tag", DateTimeOffset.Now), message);

                return tag != null;
            }
            catch (Exception ex)
            {
                // Log the exception for debugging
                _logger?.Invoke("Error", $"Tag creation failed: {ex.Message}");
                // Tag creation failed (e.g., tag already exists, invalid name, permission issues)
                // Return false to allow the caller to handle the failure gracefully
                return false;
            }
        }

        private bool IsValidTagName(string tagName)
        {
            if (string.IsNullOrWhiteSpace(tagName))
                return false;

            // Git tag name validation rules
            // Tag names cannot contain: .., @{, \, spaces at the end, or start with a dot
            if (tagName.Contains("..") ||
                tagName.Contains("@{") ||
                tagName.Contains("\\") ||
                tagName.StartsWith(".") ||
                tagName.EndsWith(".") ||
                tagName.EndsWith(".lock") ||
                tagName != tagName.TrimEnd())
            {
                return false;
            }

            return true;
        }

        public bool TagExists(string tagName)
        {
            return _repository.Tags.Any(t => t.FriendlyName == tagName);
        }

        public bool HasSubmoduleChanges(Commit fromCommit, Commit toCommit = null)
        {
            if (fromCommit == null)
                return false;

            try
            {
                toCommit = toCommit ?? _repository?.Head?.Tip;
                if (toCommit == null)
                    return false;

                var compareOptions = new CompareOptions();
                var diff = _repository.Diff.Compare<TreeChanges>(
                    fromCommit.Tree,
                    toCommit.Tree,
                    compareOptions);

                // Check for .gitmodules file changes or submodule pointer changes
                foreach (var change in diff)
                {
                    // .gitmodules file changed
                    if (change.Path.Equals(".gitmodules", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger?.Invoke("Info", "Submodule configuration changed (.gitmodules)");
                        return true;
                    }

                    // Submodule pointer changed (mode 160000 is a gitlink/submodule)
                    if (change.Mode == Mode.GitLink)
                    {
                        _logger?.Invoke("Info", $"Submodule changed: {change.Path}");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger?.Invoke("Warning", $"Error checking submodule changes: {ex.Message}");
                return false;
            }
        }

        public bool IsCommitReachable(Commit fromCommit, Commit toCommit = null)
        {
            if (fromCommit == null)
                return false;

            try
            {
                toCommit = toCommit ?? _repository?.Head?.Tip;
                if (toCommit == null)
                    return false;

                // If commits are the same, they're reachable
                if (fromCommit.Sha == toCommit.Sha)
                    return true;

                // Use LibGit2Sharp's commit filter to check reachability
                var filter = new CommitFilter
                {
                    IncludeReachableFrom = toCommit,
                    ExcludeReachableFrom = fromCommit.Parents
                };

                var reachableCommits = _repository.Commits.QueryBy(filter);
                return reachableCommits.Any(c => c.Sha == fromCommit.Sha);
            }
            catch (Exception ex)
            {
                _logger?.Invoke("Warning", $"Error checking commit reachability: {ex.Message}");
                // In shallow clones or error cases, assume reachable to avoid false negatives
                return true;
            }
        }

        /// <summary>
        /// Sorts version tags by semantic version precedence (highest version first)
        /// </summary>
        private IEnumerable<VersionTag> SortVersionTagsByPrecedence(IEnumerable<VersionTag> tags)
        {
            return tags
                .OrderByDescending(vt => vt.SemVer.Major)
                .ThenByDescending(vt => vt.SemVer.Minor)
                .ThenByDescending(vt => vt.SemVer.Patch)
                .ThenByDescending(vt => GetPrereleasePrecedence(vt.SemVer.PreRelease))
                .ThenByDescending(vt => GetPrereleaseNumber(vt.SemVer.PreRelease));
        }

        public void Dispose()
        {
            _repository?.Dispose();
        }
    }
}