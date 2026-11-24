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
                // Walk up from the start path in the caller's own path namespace. libgit2 returns
                // symlink-resolved paths (e.g. /private/var/... for /var/... on macOS), and for worktrees
                // and submodules it returns the git dir (.git/worktrees/x, .git/modules/x) rather than the
                // working directory, both of which break repo-relative paths computed from MSBuild paths.
                string full;
                try
                {
                    full = Path.GetFullPath(startPath);
                }
                catch (Exception)
                {
                    full = startPath;
                }

                for (var dir = full; !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
                {
                    var dotGit = Path.Combine(dir, ".git");
                    if ((Directory.Exists(dotGit) || File.Exists(dotGit)) && Repository.IsValid(dir))
                    {
                        return TrimTrailingSeparators(dir);
                    }
                }

                var discoveredPath = Repository.Discover(startPath);
                if (discoveredPath == null)
                    return null;

                using (var repo = new Repository(discoveredPath))
                {
                    if (!string.IsNullOrEmpty(repo.Info.WorkingDirectory))
                    {
                        return TrimTrailingSeparators(repo.Info.WorkingDirectory);
                    }
                }

                // Bare repository: keep the historical behaviour
                var gitRepoRoot = discoveredPath.TrimEnd('/', '\\');
                if (gitRepoRoot.EndsWith(".git"))
                {
                    gitRepoRoot = Path.GetDirectoryName(gitRepoRoot);
                }
                return gitRepoRoot;
            }
            catch (Exception ex) when (IsNativeLoadFailure(ex))
            {
                // A missing or wrong-architecture libgit2 used to be swallowed here and reported as
                // "No Git repository found", hiding the real cause on unsupported hosts.
                throw new InvalidOperationException(
                    "The libgit2 native library could not be loaded " +
                    $"(OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}, " +
                    $"process architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}, " +
                    $"runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}, " +
                    $"LibGit2Sharp: {Path.GetDirectoryName(typeof(Repository).Assembly.Location)}). " +
                    RepositoryService.LibGit2SharpIssuesMessage + " " + GetInnermostMessage(ex), ex);
            }
            catch
            {
                // Not a repository (or not readable): return null so callers report "No Git repository found"
                return null;
            }
        }

        /// <summary>
        /// Why <see cref="DiscoverRepositoryRoot"/> found no repository although a .git entry exists above
        /// <paramref name="startPath"/> (for example a repository owned by another user, which git refuses as
        /// "dubious ownership"), or null when there simply is no repository.
        /// </summary>
        public static string DescribeDiscoveryFailure(string startPath)
        {
            if (string.IsNullOrEmpty(startPath))
                return null;

            try
            {
                for (var dir = Path.GetFullPath(startPath); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
                {
                    var dotGit = Path.Combine(dir, ".git");
                    if (!Directory.Exists(dotGit) && !File.Exists(dotGit))
                        continue;

                    try
                    {
                        using (new Repository(dir))
                        {
                            return null; // opens fine
                        }
                    }
                    catch (Exception ex)
                    {
                        var message = GetInnermostMessage(ex);
                        var ownership = message.IndexOf("owner", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        message.IndexOf("safe.directory", StringComparison.OrdinalIgnoreCase) >= 0;
                        return $"The Git repository at {dir} cannot be opened: {message}" +
                               (ownership
                                   ? $". It is owned by another user; if you trust it, run: git config --global --add safe.directory {dir.Replace('\\', '/')}"
                                   : string.Empty);
                    }
                }
            }
            catch (Exception)
            {
                // Diagnostics only
            }

            return null;
        }

        private static bool IsNativeLoadFailure(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                if (e is DllNotFoundException || e is BadImageFormatException || e is EntryPointNotFoundException)
                    return true;
            }
            return ex is TypeInitializationException;
        }

        private static string GetInnermostMessage(Exception ex)
        {
            while (ex.InnerException != null)
                ex = ex.InnerException;
            return ex.Message;
        }

        private static string TrimTrailingSeparators(string path)
        {
            var trimmed = path.TrimEnd('/', '\\');
            // Keep filesystem roots ("/", "C:\\") intact
            if (trimmed.Length == 0 || (trimmed.Length == 2 && trimmed[1] == ':'))
            {
                return path;
            }
            return trimmed;
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
        /// <summary>
        /// Whether a file or directory (absolute, repository-relative, or "./"/"../"-relative to
        /// <paramref name="baseDirectory"/>) changed between <paramref name="sinceCommit"/> and HEAD.
        /// </summary>
        bool HasPathChangedSinceCommit(Commit sinceCommit, string path, string repoRoot, string baseDirectory = null);
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
        private string _resolvedBranch;

        /// <summary>
        /// The branch being built: the checked-out branch, or for a detached HEAD (the default in most CI
        /// systems) the CI system's branch when it describes this commit, else a local or remote branch whose tip
        /// is HEAD, else "(no branch)".
        /// </summary>
        public string CurrentBranch => _resolvedBranch ?? (_resolvedBranch = ResolveCurrentBranch());

        /// <summary>CI environment variables that name the branch being built (checked in order).</summary>
        public static readonly string[] CiBranchVariables =
        {
            "GITHUB_HEAD_REF",                        // GitHub Actions pull requests (source branch)
            "CI_MERGE_REQUEST_SOURCE_BRANCH_NAME",    // GitLab merge requests
            "CI_COMMIT_BRANCH",                       // GitLab branch pipelines
            "SYSTEM_PULLREQUEST_SOURCEBRANCH",        // Azure Pipelines pull requests
            "BUILD_SOURCEBRANCH",                     // Azure Pipelines
            "BITBUCKET_BRANCH",                       // Bitbucket Pipelines
            "BRANCH_NAME",                            // Jenkins multibranch
            "GIT_BRANCH",                             // Jenkins git plugin
            "GITHUB_REF_NAME"                         // GitHub Actions push (only when GITHUB_REF_TYPE=branch)
        };

        /// <summary>CI environment variables that name the commit being built.</summary>
        public static readonly string[] CiCommitVariables =
        {
            "GITHUB_SHA", "CI_COMMIT_SHA", "BUILD_SOURCEVERSION", "BITBUCKET_COMMIT", "GIT_COMMIT"
        };

        private string ResolveCurrentBranch()
        {
            var head = _repository?.Head;
            if (head == null)
                return "HEAD";
            if (!_repository.Info.IsHeadDetached)
                return head.FriendlyName;

            var tip = head.Tip?.Sha;

            // 1. The CI system's branch, but only if the CI system is building this very commit (other
            //    repositories built in the same job must not pick it up).
            if (tip != null && CiCommitVariables.Any(v => string.Equals(Environment.GetEnvironmentVariable(v), tip, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var variable in CiBranchVariables)
                {
                    var value = Environment.GetEnvironmentVariable(variable);
                    if (string.IsNullOrWhiteSpace(value))
                        continue;
                    if (variable == "GITHUB_REF_NAME" &&
                        !string.Equals(Environment.GetEnvironmentVariable("GITHUB_REF_TYPE"), "branch", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var name = NormalizeBranchName(value);
                    if (name.StartsWith("refs/", StringComparison.Ordinal))
                        continue; // e.g. refs/pull/1/merge: not a branch name
                    _logger?.Invoke("Info", $"Detached HEAD: using branch '{name}' from {variable}");
                    return name;
                }
            }

            // 2. A branch whose tip is HEAD (local first, then remote), preferring main/master, release, dev
            if (tip != null)
            {
                var candidates = _repository.Branches
                    .Where(b => b.Tip?.Sha == tip && !b.CanonicalName.EndsWith("/HEAD", StringComparison.Ordinal))
                    .Select(b => new
                    {
                        Name = b.IsRemote && b.FriendlyName.IndexOf('/') >= 0
                            ? b.FriendlyName.Substring(b.FriendlyName.IndexOf('/') + 1)
                            : b.FriendlyName,
                        b.IsRemote
                    })
                    .ToList();
                if (candidates.Count > 0)
                {
                    int Rank(string n)
                    {
                        var t = GetBranchType(n);
                        return t == BranchType.Main ? 0 : t == BranchType.Release ? 1 : t == BranchType.Dev ? 2 : 3;
                    }
                    var best = candidates.OrderBy(c => c.IsRemote).ThenBy(c => Rank(c.Name)).ThenBy(c => c.Name, StringComparer.Ordinal).First();
                    _logger?.Invoke("Info", $"Detached HEAD: using branch '{best.Name}' whose tip is HEAD");
                    return best.Name;
                }
            }

            return head.FriendlyName; // "(no branch)"
        }
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
                     Regex.IsMatch(branchName, @"^v\d+\.\d+(\.\d+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                     Regex.IsMatch(branchName, @"^release-\d+\.\d+(\.\d+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
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

            // All tags starting with the prefix are global candidates (v1.0.0, v1.0.0-alpha.1, ...). Work on
            // (name, target sha) pairs from one ref enumeration; commits are peeled lazily, in precedence order,
            // so repositories with thousands of tags don't pay an object lookup per tag per calculation.
            var prefix = options.TagPrefix ?? string.Empty;
            var candidates = OrderByPrecedence(GetTagRefs()
                .Where(t => t.Name.Length > prefix.Length && t.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(t => new TagCandidate(t.Name, t.TargetSha, ParseSemVer(t.Name.Substring(prefix.Length))))
                .Where(c => c.SemVer != null))
                .ToList();

            // Filter tags based on branch type
            if (branchType == BranchType.Release)
            {
                // Check for detached HEAD state
                var releaseBranch = !string.IsNullOrEmpty(options.BranchName) ? options.BranchName : CurrentBranch;
                if (_repository.Head.Tip != null && _repository.Info.IsHeadDetached && releaseBranch == _repository.Head.FriendlyName)
                {
                    _logger?.Invoke("Info", "Repository is in detached HEAD state");
                    // Skip release branch filtering in detached HEAD state
                }
                else
                {
                    var releaseVersion = ExtractReleaseVersion(releaseBranch, options.TagPrefix);
                    if (releaseVersion != null)
                    {
                        candidates = candidates
                            .Where(c => c.SemVer.Major == releaseVersion.Major && c.SemVer.Minor == releaseVersion.Minor)
                            .ToList();
                    }
                }
            }

            // Only the highest-precedence tag that is an ancestor of HEAD is used. Checking candidates lazily in
            // precedence order against one shared incremental history walk is O(history) once, instead of one
            // walk per tag (which was O(tags x history) and dominated build time on long-lived repositories).
            var selected = options.GitIntegration?.ValidateTagAncestry == true && !IsShallowClone
                ? candidates.FirstOrDefault(IsCandidateReachable)
                : candidates.FirstOrDefault(PointsAtCommit);
            var globalVersionTags = selected == null
                ? new List<VersionTag>()
                : new List<VersionTag> { Materialize(selected, isGlobal: true, projectName: null) };

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
            if (options?.GitIntegration != null)
            {
                // The lookup also depends on these settings
                cacheKey += $"_{options.GitIntegration.ValidateTagAncestry}_{string.Join(";", options.GitIntegration.CustomTagPatterns ?? new List<string>())}";
            }
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

            var projectCandidates = OrderByPrecedence(GetTagRefs()
                .Select(t =>
                {
                    // Extract the version part after the first matching prefix
                    foreach (var candidatePrefix in possiblePrefixes)
                    {
                        if (t.Name.Length > candidatePrefix.Length &&
                            t.Name.StartsWith(candidatePrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            return new TagCandidate(t.Name, t.TargetSha, ParseSemVer(t.Name.Substring(candidatePrefix.Length)));
                        }
                    }
                    return null;
                })
                .Where(c => c?.SemVer != null))
                .ToList();

            // Filter tags based on branch type
            if (branchType == BranchType.Release)
            {
                // Check for detached HEAD state
                var releaseBranch = !string.IsNullOrEmpty(options?.BranchName) ? options.BranchName : CurrentBranch;
                if (_repository.Head.Tip != null && _repository.Info.IsHeadDetached && releaseBranch == _repository.Head.FriendlyName)
                {
                    _logger?.Invoke("Info", "Repository is in detached HEAD state");
                    // Skip release branch filtering in detached HEAD state
                }
                else
                {
                    var releaseVersion = ExtractReleaseVersion(releaseBranch, tagPrefix);
                    if (releaseVersion != null)
                    {
                        projectCandidates = projectCandidates
                            .Where(c => c.SemVer.Major == releaseVersion.Major && c.SemVer.Minor == releaseVersion.Minor)
                            .ToList();
                    }
                }
            }

            // Lazily pick the first (highest-precedence) tag reachable from HEAD - see GetGlobalVersionTag
            var selectedProjectTag = options?.GitIntegration?.ValidateTagAncestry == true && !IsShallowClone
                ? projectCandidates.FirstOrDefault(IsCandidateReachable)
                : projectCandidates.FirstOrDefault(PointsAtCommit);
            var projectVersionTags = selectedProjectTag == null
                ? new List<VersionTag>()
                : new List<VersionTag> { Materialize(selectedProjectTag, isGlobal: false, projectName: projectName) };

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
            var head = _repository?.Head?.Tip;
            if (head == null)
            {
                _logger?.Invoke("Warning", "Repository HEAD or Tip is null, assuming changes exist");
                return true;
            }

            // Create cache key from commit SHA, project path, and dependencies
            var commitSha = tagCommit.Sha;
            var depKey = dependencies != null && dependencies.Any()
                ? string.Join("|", dependencies.OrderBy(d => d, StringComparer.Ordinal))
                : "";
            var cacheKey = $"{commitSha}_{head.Sha}_{projectPath}_{depKey}";

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
                bool hasChanges;
                string reason = null;
                if (tagCommit.Sha == head.Sha)
                {
                    hasChanges = false;
                }
                else
                {
                    var baseTree = tagCommit.Tree;
                    var headTree = head.Tree;

                    // "Did <dir> change?" is answered by comparing the directory's tree ids in both commits:
                    // O(depth), exact on directory boundaries (src/Lib1 vs src/Lib10), and it also sees files
                    // moved out of the directory (which a rename-detecting diff reports only under the new path).
                    var projectDirectory = ToRepoRelativePath(projectPath, repoRoot);
                    hasChanges = projectDirectory == null || PathChanged(baseTree, headTree, projectDirectory);
                    if (hasChanges)
                    {
                        reason = projectDirectory ?? projectPath;
                    }

                    if (!hasChanges && dependencies != null)
                    {
                        foreach (var dependency in dependencies)
                        {
                            var dependencyDirectory = ToRepoRelativePath(dependency, repoRoot);
                            if (dependencyDirectory != null && PathChanged(baseTree, headTree, dependencyDirectory))
                            {
                                hasChanges = true;
                                reason = dependencyDirectory;
                                break;
                            }
                        }
                    }
                }

                if (hasChanges && debug)
                {
                    _logger?.Invoke("Debug", $"Changes detected since {tagCommit.Sha.Substring(0, Math.Min(7, tagCommit.Sha.Length))} in {reason}");
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

        public bool HasPathChangedSinceCommit(Commit sinceCommit, string path, string repoRoot, string baseDirectory = null)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var head = _repository?.Head?.Tip;
            if (head == null)
                return true;
            if (sinceCommit == null)
                return true;

            try
            {
                var relative = ToRepoRelativePath(path, repoRoot, projectFileToDirectory: false, baseDirectory: baseDirectory);
                if (relative == null)
                {
                    _logger?.Invoke("Warning", $"Monitored path is outside the repository and is ignored: {path}");
                    return false;
                }
                return sinceCommit.Sha != head.Sha && PathChanged(sinceCommit.Tree, head.Tree, relative);
            }
            catch (Exception ex)
            {
                _logger?.Invoke("Warning", $"Error detecting changes in {path}: {ex.Message}");
                return true;
            }
        }

        private static readonly string[] ProjectFileExtensions =
        {
            ".csproj", ".fsproj", ".vbproj", ".proj", ".sqlproj", ".shproj", ".esproj", ".vcxproj"
        };

        /// <summary>An MSBuild project file: any extension ending in "proj" (.csproj, .wixproj, .msbuildproj, .ilproj, ...).</summary>
        private static bool IsProjectFile(string path)
        {
            var extension = Path.GetExtension(path);
            return ProjectFileExtensions.Any(ext => string.Equals(extension, ext, StringComparison.OrdinalIgnoreCase)) ||
                   (extension.Length > 5 && extension.EndsWith("proj", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Converts a path to a repository-relative path with '/' separators and no leading/trailing
        /// separator ("" is the repository root). Accepts absolute paths, repository-relative paths and,
        /// when <paramref name="baseDirectory"/> is given, "./" or "../"-relative paths resolved against it.
        /// Project files are mapped to their directory when <paramref name="projectFileToDirectory"/> is true.
        /// Returns null when the path is outside the repository.
        /// </summary>
        public static string ToRepoRelativePath(string path, string repoRoot, bool projectFileToDirectory = true, string baseDirectory = null)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "";

            var value = path.Trim();
            string relative;
            if (Path.IsPathRooted(value))
            {
                relative = GetRelativePath(repoRoot, Path.GetFullPath(value));
            }
            else if (baseDirectory != null &&
                     (value.StartsWith("../", StringComparison.Ordinal) || value.StartsWith("..\\", StringComparison.Ordinal) ||
                      value.StartsWith("./", StringComparison.Ordinal) || value.StartsWith(".\\", StringComparison.Ordinal)))
            {
                relative = GetRelativePath(repoRoot, Path.GetFullPath(Path.Combine(baseDirectory, value)));
            }
            else
            {
                relative = value;
            }

            relative = relative.Replace('\\', '/');
            if (projectFileToDirectory && IsProjectFile(relative))
            {
                var slash = relative.LastIndexOf('/');
                relative = slash < 0 ? "" : relative.Substring(0, slash);
            }

            while (relative.StartsWith("./", StringComparison.Ordinal))
            {
                relative = relative.Substring(2);
            }
            relative = relative.Trim('/');
            if (relative == ".")
            {
                relative = "";
            }

            if (relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            {
                // A path reached through a symbolic link (e.g. a link into the repository) may still be inside
                // the repository once links are resolved
                if (!_resolvingLinks && !string.IsNullOrEmpty(repoRoot))
                {
                    var absolute = Path.IsPathRooted(value)
                        ? value
                        : baseDirectory != null ? Path.Combine(baseDirectory, value) : null;
                    if (absolute != null)
                    {
                        var resolvedPath = Mister.Version.Core.PathUtils.ResolveLinks(Path.GetFullPath(absolute));
                        var resolvedRoot = Mister.Version.Core.PathUtils.ResolveLinks(Path.GetFullPath(repoRoot));
                        if (!string.Equals(resolvedPath, Path.GetFullPath(absolute), StringComparison.Ordinal) ||
                            !string.Equals(resolvedRoot, Path.GetFullPath(repoRoot), StringComparison.Ordinal))
                        {
                            try
                            {
                                _resolvingLinks = true;
                                return ToRepoRelativePath(resolvedPath, resolvedRoot, projectFileToDirectory);
                            }
                            finally
                            {
                                _resolvingLinks = false;
                            }
                        }
                    }
                }
                return null; // outside the repository
            }
            return relative;
        }

        [ThreadStatic]
        private static bool _resolvingLinks;

        private static string GetRelativePath(string relativeTo, string path)
        {
#if NET472
            return Mister.Version.Core.PathUtils.GetRelativePath(relativeTo, path);
#else
            return Path.GetRelativePath(relativeTo, path);
#endif
        }

        /// <summary>
        /// A branch name as CI systems and users write it, reduced to the short name: "refs/heads/main",
        /// "refs/remotes/origin/main" and "origin/main" all become "main". Other refs (refs/pull/1/merge, tags) are
        /// returned unchanged.
        /// </summary>
        public static string NormalizeBranchName(string branchName)
        {
            if (string.IsNullOrWhiteSpace(branchName))
                return branchName;

            var name = branchName.Trim();
            if (name.StartsWith("refs/heads/", StringComparison.Ordinal))
                return name.Substring("refs/heads/".Length);
            if (name.StartsWith("refs/remotes/", StringComparison.Ordinal))
            {
                var rest = name.Substring("refs/remotes/".Length);
                var slash = rest.IndexOf('/');
                return slash >= 0 ? rest.Substring(slash + 1) : rest;
            }
            if (name.StartsWith("origin/", StringComparison.Ordinal))
                return name.Substring("origin/".Length);
            return name;
        }

        /// <summary>
        /// Whether the file or directory at <paramref name="relativePath"/> differs between two trees.
        /// A null <paramref name="baseTree"/> means "before the first commit".
        /// </summary>
        public static bool PathChanged(Tree baseTree, Tree headTree, string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                return baseTree == null || headTree == null || baseTree.Id != headTree.Id;
            }

            var before = baseTree == null ? null : FindEntry(baseTree, relativePath);
            var after = headTree == null ? null : FindEntry(headTree, relativePath);
            if (before == null && after == null)
                return false;
            if (before == null || after == null)
                return true;
            return before.Target.Id != after.Target.Id || before.Mode != after.Mode;
        }

        /// <summary>
        /// Looks up a tree entry by repository-relative path; falls back to a case-insensitive match per
        /// component, because MSBuild paths on Windows/macOS may differ in case from what git stores.
        /// </summary>
        private static TreeEntry FindEntry(Tree tree, string relativePath)
        {
            var exact = tree[relativePath];
            if (exact != null)
                return exact;

            TreeEntry found = null;
            var current = tree;
            foreach (var part in relativePath.Split('/'))
            {
                if (current == null)
                {
                    // A path below a submodule: the submodule's commit pointer stands for everything in it
                    return found != null && found.TargetType == TreeEntryTargetType.GitLink ? found : null;
                }
                found = current.FirstOrDefault(e => string.Equals(e.Name, part, StringComparison.OrdinalIgnoreCase));
                if (found == null)
                    return null;
                current = found.TargetType == TreeEntryTargetType.Tree ? (Tree)found.Target : null;
            }
            return found;
        }

        /// <summary>Submodule paths declared in a tree's .gitmodules (empty when it has none).</summary>
        private static List<string> ReadSubmodulePaths(Tree tree)
        {
            var result = new List<string>();
            try
            {
                if (tree?[".gitmodules"]?.Target is Blob blob)
                {
                    foreach (Match match in Regex.Matches(blob.GetContentText(), @"^\s*path\s*=\s*(.+?)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant))
                    {
                        result.Add(match.Groups[1].Value.Replace('\\', '/').Trim('/'));
                    }
                }
            }
            catch (Exception)
            {
                // Unreadable .gitmodules: no submodules
            }
            return result;
        }

        /// <summary>
        /// Whether a repository-relative file path is inside the repository-relative directory (exact
        /// directory boundary; "" is the repository root).
        /// </summary>
        public static bool IsUnderDirectory(string filePath, string directory)
        {
            if (string.IsNullOrEmpty(directory))
                return true;
            var path = filePath.Replace('\\', '/');
            return path.Equals(directory, StringComparison.OrdinalIgnoreCase) ||
                   (path.Length > directory.Length && path[directory.Length] == '/' &&
                    path.StartsWith(directory, StringComparison.OrdinalIgnoreCase));
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
                var projectDirectory = ToRepoRelativePath(projectPath, repoRoot);
                var baseDirectory = Path.IsPathRooted(projectPath ?? "")
                    ? Path.GetDirectoryName(Path.GetFullPath(projectPath))
                    : Path.Combine(repoRoot ?? "", projectDirectory ?? "");

                // Directories/files whose changes count for this project
                var scopes = new List<string> { projectDirectory ?? "" };
                if (dependencies != null)
                {
                    scopes.AddRange(dependencies.Select(d => ToRepoRelativePath(d, repoRoot)).Where(d => d != null));
                }
                if (config?.AdditionalMonitorPaths != null)
                {
                    scopes.AddRange(config.AdditionalMonitorPaths
                        .Where(m => !string.IsNullOrWhiteSpace(m))
                        .Select(m => ToRepoRelativePath(m, repoRoot, projectFileToDirectory: false, baseDirectory: baseDirectory))
                        .Where(m => m != null));
                }
                scopes = scopes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                // One diff, limited to the scopes, without rename detection (renames are reported as a
                // delete of the old path plus an add of the new one, so both projects see the change).
                var compareOptions = new CompareOptions { Similarity = SimilarityOptions.None };
                var diff = scopes.Any(string.IsNullOrEmpty)
                    ? _repository.Diff.Compare<TreeChanges>(tagCommit.Tree, _repository.Head.Tip.Tree, compareOptions)
                    : _repository.Diff.Compare<TreeChanges>(tagCommit.Tree, _repository.Head.Tip.Tree, scopes, null, compareOptions);

                var changedFiles = new List<string>();
                foreach (var change in diff)
                {
                    foreach (var path in new[] { change.Path, change.OldPath })
                    {
                        if (!string.IsNullOrEmpty(path) && scopes.Any(scope => IsUnderDirectory(path, scope)))
                        {
                            changedFiles.Add(path.Replace('\\', '/'));
                        }
                    }
                }

                // A scope inside a submodule (e.g. a reference to a project in a submodule) changes when the
                // submodule's commit pointer moves; a pathspec diff below the gitlink does not report that
                var headTree = _repository.Head.Tip.Tree;
                var tagTree = tagCommit.Tree;
                var submodulePaths = ReadSubmodulePaths(headTree).Union(ReadSubmodulePaths(tagTree), StringComparer.OrdinalIgnoreCase).ToList();
                if (submodulePaths.Count > 0)
                {
                    foreach (var scope in scopes.Where(scope => !string.IsNullOrEmpty(scope)))
                    {
                        foreach (var submodule in submodulePaths)
                        {
                            if (IsUnderDirectory(scope, submodule) && !string.Equals(scope, submodule, StringComparison.OrdinalIgnoreCase) &&
                                PathChanged(tagTree, headTree, submodule))
                            {
                                changedFiles.Add(submodule);
                            }
                        }
                    }
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
                patternMatcher.DetermineBumpType(classification, config);

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
                toCommit = toCommit ?? _repository?.Head?.Tip;
                if (toCommit == null)
                {
                    _logger?.Invoke("Warning", "Cannot calculate commit height: HEAD tip is null");
                    return 0;
                }

                // Check cache first (use from and to commit SHAs as key). Without a base commit (no version tag
                // yet) the height counts every commit, so successive commits still get distinct prerelease numbers.
                var cacheKey = $"{fromCommit?.Sha ?? "ROOT"}_{toCommit?.Sha ?? "HEAD"}";
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
                    IncludeReachableFrom = toCommit,
                    // Counting needs no ordering; the default (time) sort makes libgit2 walk everything up front
                    SortBy = CommitSortStrategies.None
                };
                if (fromCommit != null)
                {
                    filter.ExcludeReachableFrom = fromCommit;
                }

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

            return SemVer.TryParse(version, out var parsed) ? parsed : null;
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

                        if (IsUnderDirectory(normalizedChangePath, normalizedProjectPath.Trim('/')))
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

        // One incremental history walk from HEAD shared by every reachability check of this instance
        private HashSet<ObjectId> _reachableFromHead;
        private IEnumerator<Commit> _reachabilityWalk;
        private bool _reachabilityWalkDone;

        /// <summary>
        /// Whether <paramref name="commit"/> is HEAD or one of its ancestors. All calls share one lazy history
        /// walk (no ordering), so checking many tags costs at most one pass over the history.
        /// </summary>
        private bool IsReachableFromHead(Commit commit)
        {
            if (commit == null)
                return false;

            try
            {
                var head = _repository?.Head?.Tip;
                if (head == null)
                    return false;

                if (_reachableFromHead == null)
                {
                    _reachableFromHead = new HashSet<ObjectId>();
                    _reachabilityWalk = _repository.Commits.QueryBy(new CommitFilter
                    {
                        IncludeReachableFrom = head,
                        SortBy = CommitSortStrategies.None
                    }).GetEnumerator();
                }

                if (_reachableFromHead.Contains(commit.Id))
                    return true;

                while (!_reachabilityWalkDone)
                {
                    if (!_reachabilityWalk.MoveNext())
                    {
                        _reachabilityWalkDone = true;
                        break;
                    }

                    var id = _reachabilityWalk.Current.Id;
                    _reachableFromHead.Add(id);
                    if (id == commit.Id)
                        return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger?.Invoke("Warning", $"Error checking commit reachability: {ex.Message}");
                // In shallow clones or error cases, assume reachable to avoid false negatives
                return true;
            }
        }

        public bool IsCommitReachable(Commit fromCommit, Commit toCommit = null)
        {
            if (fromCommit == null)
                return false;

            if (toCommit == null)
            {
                return IsReachableFromHead(fromCommit);
            }

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

        private sealed class TagCandidate
        {
            public TagCandidate(string name, string targetSha, SemVer semVer)
            {
                Name = name;
                TargetSha = targetSha;
                SemVer = semVer;
            }

            public string Name { get; }
            public string TargetSha { get; }
            public SemVer SemVer { get; }
            public bool Peeled { get; set; }
            public Commit Commit { get; set; }
        }

        private List<(string Name, string TargetSha)> _tagRefs;

        /// <summary>Every tag as (name, target sha), enumerated once per instance.</summary>
        private List<(string Name, string TargetSha)> GetTagRefs()
        {
            if (_tagRefs != null)
                return _tagRefs;

            var list = new List<(string, string)>();
            foreach (var reference in _repository.Refs.FromGlob("refs/tags/*"))
            {
                var direct = reference.ResolveToDirectReference();
                if (direct?.TargetIdentifier == null)
                    continue;
                list.Add((reference.CanonicalName.Substring("refs/tags/".Length), direct.TargetIdentifier));
            }
            return _tagRefs = list;
        }

        // Tag/commit object sha -> peeled commit sha ("" when the tag doesn't point at a commit). Objects are
        // immutable, so this is valid for the lifetime of the process and across repositories.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> PeeledCommitShas =
            new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        private Commit Peel(TagCandidate candidate)
        {
            if (candidate.Peeled)
                return candidate.Commit;
            candidate.Peeled = true;

            if (!PeeledCommitShas.TryGetValue(candidate.TargetSha, out var commitSha))
            {
                GitObject target = _repository.Lookup(candidate.TargetSha);
                while (target is TagAnnotation annotation)
                    target = annotation.Target;
                commitSha = (target as Commit)?.Sha ?? string.Empty;
                if (PeeledCommitShas.Count > 100000)
                    PeeledCommitShas.Clear();
                PeeledCommitShas[candidate.TargetSha] = commitSha;
            }

            candidate.Commit = commitSha.Length == 0 ? null : _repository.Lookup<Commit>(commitSha);
            return candidate.Commit;
        }

        private bool IsCandidateReachable(TagCandidate candidate)
        {
            // A tag on a tree or blob is not a version of any commit
            var commit = Peel(candidate);
            return commit != null && IsReachableFromHead(commit);
        }

        /// <summary>
        /// Candidates usable without an ancestry check (shallow clones): skip tags on trees or blobs, keep tags whose
        /// commit is not in the shallow history.
        /// </summary>
        private bool PointsAtCommit(TagCandidate candidate)
        {
            try
            {
                return Peel(candidate) != null || _repository.Lookup(candidate.TargetSha) == null;
            }
            catch (Exception)
            {
                return true;
            }
        }

        private VersionTag Materialize(TagCandidate candidate, bool isGlobal, string projectName)
        {
            return new VersionTag
            {
                Tag = _repository.Tags[candidate.Name],
                SemVer = candidate.SemVer,
                Commit = Peel(candidate),
                IsGlobal = isGlobal,
                ProjectName = projectName
            };
        }

        private IEnumerable<TagCandidate> OrderByPrecedence(IEnumerable<TagCandidate> candidates)
        {
            return candidates
                .OrderByDescending(c => c.SemVer.Major)
                .ThenByDescending(c => c.SemVer.Minor)
                .ThenByDescending(c => c.SemVer.Patch)
                .ThenByDescending(c => GetPrereleasePrecedence(c.SemVer.PreRelease))
                .ThenByDescending(c => GetPrereleaseNumber(c.SemVer.PreRelease));
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
            _reachabilityWalk?.Dispose();
            _repository?.Dispose();
        }
    }
}