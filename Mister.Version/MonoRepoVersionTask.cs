using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;

namespace Mister.Version;

/// <summary>
/// MSBuild task that automatically versions projects in a monorepo based on git history
/// and change detection rules without requiring version changes to be committed
/// </summary>
public class MonoRepoVersionTask : Task
{
    // Static cache shared across all task instances in the same build
    // This significantly improves performance in multi-project builds
    private static readonly object _cacheLock = new object();
    private static VersionCache _staticCache;
    private static string _cacheRepoRoot;

    /// <summary>
    /// Path to the project file being built
    /// </summary>
    [Required]
    public string ProjectPath { get; set; }

    /// <summary>
    /// Path to start searching for the Git repository root.
    /// If not specified, will start from the project directory.
    /// </summary>
    public string RepoRoot { get; set; }

    /// <summary>
    /// Output parameter for the calculated version
    /// </summary>
    [Output]
    public string Version { get; set; }

    /// <summary>
    /// Output parameter that indicates if changes were detected for this project
    /// </summary>
    [Output]
    public bool VersionChanged { get; set; }

    /// <summary>
    /// Output parameter for the discovered Git repository root path
    /// </summary>
    [Output]
    public string DiscoveredRepoRoot { get; set; }

    /// <summary>
    /// Whether to automatically update the project file with the new version
    /// This is set to FALSE by default to avoid requiring commits for version changes
    /// </summary>
    public bool UpdateProjectFile { get; set; } = false;

    /// <summary>
    /// Optional parameter to force a specific version
    /// </summary>
    public string ForceVersion { get; set; }

    /// <summary>
    /// List of project dependencies to check for changes
    /// </summary>
    public ITaskItem[] Dependencies { get; set; }

    /// <summary>
    /// The project's NuGet assets file ($(ProjectAssetsFile)). Used to find transitive project references,
    /// so a change in a dependency's dependency also bumps this project (the CLI already did this).
    /// </summary>
    public string ProjectAssetsFile { get; set; }

    /// <summary>
    /// Branch name to version as. Overrides the checked-out branch; useful for detached-HEAD CI checkouts.
    /// When empty and HEAD is detached, the branch is taken from the CI environment (when it describes the
    /// checked-out commit) or from a branch whose tip is HEAD.
    /// </summary>
    public string BranchName { get; set; }

    /// <summary>
    /// When true, building outside a git repository is an error. By default it is a warning (MRV003) and the
    /// project's own version is kept, so source archives and Docker contexts without .git still build.
    /// </summary>
    public bool RequireGitRepository { get; set; } = false;

    /// <summary>
    /// Custom tag prefix for version tags (default: v)
    /// </summary>
    public string TagPrefix { get; set; } = "v";

    /// <summary>
    /// Debug mode for verbose logging
    /// </summary>
    public bool Debug { get; set; } = false;

    /// <summary>
    /// Extra debug info to display dependency graph and change details
    /// </summary>
    public bool ExtraDebug { get; set; } = false;

    /// <summary>
    /// Whether to skip versioning for test projects
    /// </summary>
    public bool SkipTestProjects { get; set; } = true;

    /// <summary>
    /// Whether to skip versioning for non-packable projects
    /// </summary>
    public bool SkipNonPackableProjects { get; set; } = true;

    /// <summary>
    /// Whether this project is a test project
    /// </summary>
    public bool IsTestProject { get; set; } = false;

    /// <summary>
    /// Whether this project is packable
    /// </summary>
    public bool IsPackable { get; set; } = true;

    /// <summary>
    /// Prerelease type to use for main/dev branches when incrementing versions
    /// Options: none, alpha, beta, rc
    /// Default: none (no prerelease suffix)
    /// </summary>
    public string PrereleaseType { get; set; } = "none";
    
    /// <summary>
    /// Whether to create a git tag for the calculated version
    /// </summary>
    public bool CreateTag { get; set; } = false;
    
    /// <summary>
    /// Custom message for the git tag (used with CreateTag)
    /// </summary>
    public string TagMessage { get; set; }

    /// <summary>
    /// Path to version configuration YAML file
    /// </summary>
    public string ConfigFile { get; set; }
    
    /// <summary>
    /// Whether to run in dry-run mode (show what would be done without making changes)
    /// </summary>
    public bool DryRun { get; set; } = false;

    /// <summary>
    /// Whether to enable caching of version calculations across builds (default: true)
    /// Set to false to disable caching for testing or debugging
    /// </summary>
    public bool EnableCache { get; set; } = true;

    /// <summary>
    /// Optional per-project file in which the calculated version is cached across processes
    /// (parallel build nodes, separate build/pack/test invocations). The entry is keyed on a fingerprint
    /// of the repository state and all task inputs, so it can never return a stale version.
    /// </summary>
    public string CacheFile { get; set; }

    /// <summary>
    /// Enable file-based caching of version results (default: false).
    /// When enabled, version results are stored in .mrversion/[git-sha]/[project-name]/version.props
    /// This allows for fast version lookups when the git SHA hasn't changed, even across separate build processes.
    /// </summary>
    public bool EnableFileCache { get; set; } = false;

    /// <summary>
    /// Custom path for the file cache directory.
    /// If not specified, defaults to .mrversion in the repository root.
    /// </summary>
    public string FileCachePath { get; set; }

    /// <summary>
    /// Enable conventional commits analysis for semantic version bump detection
    /// </summary>
    public bool ConventionalCommitsEnabled { get; set; } = false;

    /// <summary>
    /// Semicolon-separated list of commit patterns for major version bumps
    /// </summary>
    public string MajorPatterns { get; set; }

    /// <summary>
    /// Semicolon-separated list of commit patterns for minor version bumps
    /// </summary>
    public string MinorPatterns { get; set; }

    /// <summary>
    /// Semicolon-separated list of commit patterns for patch version bumps
    /// </summary>
    public string PatchPatterns { get; set; }

    /// <summary>
    /// Semicolon-separated list of commit patterns to ignore for versioning
    /// </summary>
    public string IgnorePatterns { get; set; }

    /// <summary>
    /// Enable file pattern-based change detection for smart versioning
    /// </summary>
    public bool ChangeDetectionEnabled { get; set; } = false;

    /// <summary>
    /// Semicolon-separated list of file patterns to ignore (won't trigger version bumps)
    /// </summary>
    public string IgnoreFilePatterns { get; set; }

    /// <summary>
    /// Semicolon-separated list of file patterns that require major version bumps
    /// </summary>
    public string MajorFilePatterns { get; set; }

    /// <summary>
    /// Semicolon-separated list of file patterns that require minor version bumps
    /// </summary>
    public string MinorFilePatterns { get; set; }

    /// <summary>
    /// Semicolon-separated list of file patterns that require patch version bumps
    /// </summary>
    public string PatchFilePatterns { get; set; }

    /// <summary>
    /// Source-only mode: only version when source code changes (ignore test/doc changes)
    /// </summary>
    public bool SourceOnlyMode { get; set; } = false;

    /// <summary>
    /// Semicolon-separated list of additional directories to monitor for changes
    /// </summary>
    public string AdditionalMonitorPaths { get; set; }

    /// <summary>
    /// Enable automatic changelog generation during build
    /// </summary>
    public bool GenerateChangelog { get; set; } = false;

    /// <summary>
    /// Changelog output format (markdown, text, json)
    /// </summary>
    public string ChangelogFormat { get; set; } = "markdown";

    /// <summary>
    /// Changelog output file path
    /// </summary>
    public string ChangelogOutputPath { get; set; } = "CHANGELOG.md";

    /// <summary>
    /// Repository URL for generating links in changelog
    /// </summary>
    public string ChangelogRepositoryUrl { get; set; }

    /// <summary>
    /// Include commit authors in changelog
    /// </summary>
    public bool ChangelogIncludeAuthors { get; set; } = false;

    /// <summary>
    /// Enable shallow clone support for repos cloned with --depth
    /// </summary>
    public bool ShallowCloneSupport { get; set; } = true;

    /// <summary>
    /// Fallback version for shallow clones with limited history
    /// </summary>
    public string ShallowCloneFallbackVersion { get; set; }

    /// <summary>
    /// Enable submodule change detection
    /// </summary>
    public bool SubmoduleSupport { get; set; } = false;

    /// <summary>
    /// Custom tag patterns (semicolon-separated)
    /// </summary>
    public string CustomTagPatterns { get; set; }

    /// <summary>
    /// Enable branch-based versioning strategies
    /// </summary>
    public bool BranchBasedVersioning { get; set; } = false;

    /// <summary>
    /// Branch versioning rules (semicolon-separated)
    /// </summary>
    public string BranchVersioningRules { get; set; }

    /// <summary>
    /// Include branch name in version build metadata
    /// </summary>
    public bool IncludeBranchInMetadata { get; set; } = false;

    /// <summary>
    /// Validate that version tags are reachable from current branch
    /// </summary>
    public bool ValidateTagAncestry { get; set; } = true;

    /// <summary>
    /// Fetch depth for shallow clone operations
    /// </summary>
    public int FetchDepth { get; set; } = 50;

    /// <summary>
    /// Version scheme to use: semver or calver (default: semver)
    /// </summary>
    public string VersionScheme { get; set; } = "semver";

    /// <summary>
    /// CalVer format: YYYY.MM.PATCH, YY.0M.PATCH, YYYY.WW.PATCH, YYYY.0M.PATCH
    /// </summary>
    public string CalVerFormat { get; set; } = "YYYY.MM.PATCH";

    /// <summary>
    /// CalVer start date (optional): YYYY-MM-DD format
    /// </summary>
    public string CalVerStartDate { get; set; }

    /// <summary>
    /// Reset patch version periodically for CalVer (monthly/weekly)
    /// </summary>
    public bool CalVerResetPatch { get; set; } = true;

    /// <summary>
    /// CalVer separator (default: .)
    /// </summary>
    public string CalVerSeparator { get; set; } = ".";

    // ========== Validation Constraints Properties ==========

    /// <summary>
    /// Enable version validation constraints
    /// </summary>
    public bool ValidationEnabled { get; set; } = false;

    /// <summary>
    /// Minimum version that cannot be violated
    /// </summary>
    public string MinimumVersion { get; set; }

    /// <summary>
    /// Maximum version that cannot be exceeded
    /// </summary>
    public string MaximumVersion { get; set; }

    /// <summary>
    /// Allowed version range pattern (e.g., "3.x.x", "2.1.x")
    /// </summary>
    public string AllowedVersionRange { get; set; }

    /// <summary>
    /// Validate dependency version compatibility
    /// </summary>
    public bool ValidateDependencyVersions { get; set; } = false;

    /// <summary>
    /// Require explicit approval for major version bumps
    /// </summary>
    public bool RequireMajorApproval { get; set; } = false;

    /// <summary>
    /// Semicolon-separated list of blocked/forbidden versions
    /// </summary>
    public string BlockedVersions { get; set; }

    /// <summary>
    /// Require monotonically increasing versions
    /// </summary>
    public bool RequireMonotonicIncrease { get; set; } = true;

    /// <summary>
    /// Whether major version bump has been explicitly approved
    /// </summary>
    public bool MajorVersionApproved { get; set; } = false;

    /// <summary>
    /// Gets or creates the static cache for the current repository
    /// </summary>
    private VersionCache GetOrCreateCache(string repoRoot, string repositoryStateKey)
    {
        if (!EnableCache)
            return null;

        lock (_cacheLock)
        {
            // If cache doesn't exist or is for a different repo, create a new one
            if (_staticCache == null || _cacheRepoRoot != repoRoot)
            {
                _staticCache = new VersionCache(repoRoot, repositoryStateKey);
                _cacheRepoRoot = repoRoot;
                Log.LogMessage(MessageImportance.Low, $"[Mister.Version] Created new cache for repository: {repoRoot}");
            }
            else
            {
                // Invalidate when anything the versions depend on changed (HEAD, branch, tags, config) - not just HEAD,
                // otherwise a tag created between two builds served by the same reused MSBuild node is ignored.
                if (_staticCache.ValidateAndInvalidate(repositoryStateKey))
                {
                    Log.LogMessage(MessageImportance.Low, "[Mister.Version] Cache invalidated due to repository state change");
                }
            }

            return _staticCache;
        }
    }

    /// <summary>
    /// Clears the static cache (useful for testing)
    /// </summary>
    public static void ClearCache()
    {
        lock (_cacheLock)
        {
            _staticCache = null;
            _cacheRepoRoot = null;
        }
        VersionResultCache.ClearMemory();
    }

    public override bool Execute()
    {
        try
        {
            // Validate required properties
            if (string.IsNullOrEmpty(ProjectPath))
                throw new InvalidOperationException("ProjectPath is required but was not provided.");

            // Use project directory as starting point if RepoRoot is not specified
            var searchStartPath = string.IsNullOrEmpty(RepoRoot) 
                ? Path.GetDirectoryName(ProjectPath) 
                : RepoRoot;
                
            Log.LogMessage(Debug ? MessageImportance.High : MessageImportance.Low, $"Starting Mister.Version versioning for {ProjectPath}");

            // Create logger function for core services
            var logger = MSBuildLoggerFactory.CreateMSBuildLogger(Log, Debug, ExtraDebug);

            // Discover Git repository root
            var gitRepoRoot = GitRepositoryHelper.DiscoverRepositoryRoot(searchStartPath);
            if (gitRepoRoot == null)
            {
                if (!string.IsNullOrEmpty(ForceVersion))
                {
                    // An explicit version needs no git history
                    Version = ForceVersion;
                    VersionChanged = true;
                    return true;
                }

                // A .git that cannot be opened (e.g. owned by another user) is not "no repository"
                var discoveryProblem = GitRepositoryHelper.DescribeDiscoveryFailure(searchStartPath);
                if (RequireGitRepository)
                {
                    Log.LogError(discoveryProblem ?? string.Format(RepositoryService.NoRepositoryFoundError, searchStartPath));
                    Log.LogError(RepositoryService.EnsureGitRepositoryMessage);
                    return false;
                }

                Log.LogWarning(null, "MRV003", null, null, 0, 0, 0, 0,
                    "[Mister.Version] " + (discoveryProblem ?? string.Format(RepositoryService.NoRepositoryFoundError, searchStartPath)) +
                    ". The version is not calculated" +
                    (string.IsNullOrEmpty(ShallowCloneFallbackVersion) ? " and the project's own version is kept" : $"; using {ShallowCloneFallbackVersion}") +
                    ". Set MonoRepoRequireGitRepository=true to make this an error.");
                Version = ShallowCloneFallbackVersion ?? string.Empty;
                return true;
            }

            // Fingerprint of everything the version depends on (HEAD, branch, tags, config, ...).
            // Computed once per build per node and reused by every project / TFM in that node.
            var repositoryState = GetRepositoryFingerprint(gitRepoRoot);

            // Side effects (project file update, tag creation, changelog) must run every time, so skip the result cache.
            string resultCacheKey = null;
            if (EnableCache && repositoryState != null && !UpdateProjectFile && !CreateTag && !GenerateChangelog)
            {
                resultCacheKey = VersionResultCache.CreateKey(repositoryState,
                    DescribeInputs() + "AllDependencies=" + string.Join("|", GetAllProjectDependencies().Select(NormalizeForKey)) + "\n");
                if (VersionResultCache.TryGet(resultCacheKey, CacheFile, out var cached))
                {
                    Version = cached.Version;
                    VersionChanged = cached.VersionChanged;
                    DiscoveredRepoRoot = gitRepoRoot;
                    Log.LogMessage(Debug ? MessageImportance.High : MessageImportance.Low,
                        $"Calculated version: {Version} for {Path.GetFileNameWithoutExtension(ProjectPath)} (cached)");
                    return true;
                }
            }

            // Get or create cache
            var cache = GetOrCreateCache(gitRepoRoot, repositoryState);

            // Log cache statistics if in debug mode
            if (Debug && cache != null)
            {
                var stats = cache.GetStatistics();
                Log.LogMessage(MessageImportance.Low, $"[Mister.Version] Cache Stats: {stats}");
            }

            // Initialize versioning service with cache
            using var versioningService = new VersioningService(gitRepoRoot, logger, cache);

            // Prepare version request
            var dependencies = GetAllProjectDependencies();
            var versionRequest = new VersioningRequest
            {
                RepoRoot = gitRepoRoot,
                ProjectPath = ProjectPath,
                ConfigFile = ConfigFile,
                PrereleaseType = PrereleaseType,
                TagPrefix = TagPrefix,
                ForceVersion = ForceVersion,
                Dependencies = dependencies,
                BranchName = BranchName,
                Debug = Debug,
                ExtraDebug = ExtraDebug,
                SkipTestProjects = SkipTestProjects,
                SkipNonPackableProjects = SkipNonPackableProjects,
                IsTestProject = IsTestProject,
                IsPackable = IsPackable,
                ConventionalCommitsEnabled = ConventionalCommitsEnabled,
                MajorPatterns = MajorPatterns,
                MinorPatterns = MinorPatterns,
                PatchPatterns = PatchPatterns,
                IgnorePatterns = IgnorePatterns,
                ChangeDetectionEnabled = ChangeDetectionEnabled,
                IgnoreFilePatterns = IgnoreFilePatterns,
                MajorFilePatterns = MajorFilePatterns,
                MinorFilePatterns = MinorFilePatterns,
                PatchFilePatterns = PatchFilePatterns,
                SourceOnlyMode = SourceOnlyMode,
                AdditionalMonitorPaths = AdditionalMonitorPaths,
                ShallowCloneSupport = ShallowCloneSupport,
                ShallowCloneFallbackVersion = ShallowCloneFallbackVersion,
                SubmoduleSupport = SubmoduleSupport,
                CustomTagPatterns = CustomTagPatterns,
                BranchBasedVersioning = BranchBasedVersioning,
                BranchVersioningRules = BranchVersioningRules,
                IncludeBranchInMetadata = IncludeBranchInMetadata,
                ValidateTagAncestry = ValidateTagAncestry,
                FetchDepth = FetchDepth,
                VersionScheme = VersionScheme,
                CalVerFormat = CalVerFormat,
                CalVerStartDate = CalVerStartDate,
                CalVerResetPatch = CalVerResetPatch,
                CalVerSeparator = CalVerSeparator,
                ValidationEnabled = ValidationEnabled,
                MinimumVersion = MinimumVersion,
                MaximumVersion = MaximumVersion,
                AllowedVersionRange = AllowedVersionRange,
                ValidateDependencyVersions = ValidateDependencyVersions,
                RequireMajorApproval = RequireMajorApproval,
                BlockedVersions = BlockedVersions,
                RequireMonotonicIncrease = RequireMonotonicIncrease,
                MajorVersionApproved = MajorVersionApproved,
                EnableFileCache = EnableFileCache,
                FileCachePath = FileCachePath
            };

            // Calculate version
            var versionResult = versioningService.CalculateProjectVersion(versionRequest);
            
            if (!versionResult.Success)
            {
                if (versionResult.ValidationFailed)
                {
                    Log.LogError(null, "MRV004", null, ProjectPath, 0, 0, 0, 0, "[Mister.Version] " + versionResult.ErrorMessage);
                }
                else
                {
                    Log.LogError($"Version calculation failed: {versionResult.ErrorMessage}");
                }
                return false;
            }

            // Set output parameters. Skipped projects without a configured base version keep their own version
            // (previously they were stamped with a placeholder 1.0.0, overriding an explicit <Version>).
            Version = versionResult.Skipped && !versionResult.SkippedWithConfiguredVersion ? string.Empty : versionResult.Version;
            VersionChanged = versionResult.VersionChanged;
            DiscoveredRepoRoot = gitRepoRoot;

            Log.LogMessage(Debug ? MessageImportance.High : MessageImportance.Normal, $"Calculated version: {Version} for {versionResult.ProjectName}");

            if (VersionChanged)
            {
                Log.LogMessage(Debug ? MessageImportance.High : MessageImportance.Normal, $"Version changed: {versionResult.ChangeReason}");
            }

            if (resultCacheKey != null && !string.IsNullOrEmpty(Version))
            {
                VersionResultCache.Store(resultCacheKey, CacheFile, new CachedVersionResult
                {
                    Version = Version,
                    VersionChanged = versionResult.VersionChanged,
                    ChangeReason = versionResult.ChangeReason
                });
            }

            // Update the project file if enabled (disabled by default)
            if (UpdateProjectFile && !string.IsNullOrEmpty(Version))
            {
                if (DryRun)
                {
                    Log.LogMessage(MessageImportance.High, $"[DRY RUN] Would update project file {ProjectPath} with version {Version}");
                }
                else
                {
                    UpdateProjectFileVersion();
                }
            }
            
            if (GenerateChangelog && !string.IsNullOrEmpty(Version))
            {
                WriteChangelog(gitRepoRoot, versionResult.ProjectName, logger);
            }

            // Create git tag if requested
            if (CreateTag && !string.IsNullOrEmpty(Version))
            {
                var tagSuccess = versioningService.CreateTag(versionResult, TagPrefix, TagMessage, DryRun);
                
                if (!tagSuccess && !DryRun)
                {
                    Log.LogWarning("Tag creation failed");
                }
            }

            return !Log.HasLoggedErrors;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true);
            return false;
        }
    }

    /// <summary>
    /// Generates the project's changelog (commits since the previous version tag that touch the project
    /// directory) and writes it to <see cref="ChangelogOutputPath"/> (relative to the project directory).
    /// The file is only rewritten when its content changes.
    /// </summary>
    private void WriteChangelog(string gitRepoRoot, string projectName, Action<string, string> logger)
    {
        try
        {
            var config = ConfigurationService.LoadConfiguration(ConfigFile, gitRepoRoot, logger);
            var changelogConfig = config?.Changelog ?? new ChangelogConfig { Enabled = true };
            changelogConfig.RepositoryUrl = string.IsNullOrEmpty(ChangelogRepositoryUrl) ? changelogConfig.RepositoryUrl : ChangelogRepositoryUrl;
            changelogConfig.IncludeAuthors = changelogConfig.IncludeAuthors || ChangelogIncludeAuthors;

            var conventions = config?.CommitConventions ?? new ConventionalCommitConfig
            {
                Enabled = true,
                MajorPatterns = SplitPatterns(MajorPatterns),
                MinorPatterns = SplitPatterns(MinorPatterns),
                PatchPatterns = SplitPatterns(PatchPatterns),
                IgnorePatterns = SplitPatterns(IgnorePatterns)
            };
            if (conventions.MajorPatterns == null || conventions.MajorPatterns.Count == 0)
            {
                conventions = new ConventionalCommitConfig { Enabled = true };
            }

            var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(ProjectPath));
            var relativeDirectory = PathUtilsForTask.GetRelativePath(gitRepoRoot, projectDirectory);

            string output;
            using (var repo = new LibGit2Sharp.Repository(gitRepoRoot))
            {
                var range = ChangelogRange.Resolve(repo, repo.Head?.Tip, TagPrefix, projectName, relativeDirectory, excludeTagsOnTarget: true);
                var generator = new ChangelogGenerator(new ConventionalCommitAnalyzer());
                var changelog = generator.GenerateChangelog(range.Commits, Version, range.FromVersion, changelogConfig, conventions, projectName);

                switch ((ChangelogFormat ?? "markdown").ToLowerInvariant())
                {
                    case "text":
                    case "txt":
                        output = generator.FormatAsText(changelog, changelogConfig);
                        break;
                    case "json":
                        output = generator.FormatAsJson(changelog);
                        break;
                    default:
                        output = generator.FormatAsMarkdown(changelog, changelogConfig);
                        break;
                }
            }

            var outputPath = Path.Combine(projectDirectory, string.IsNullOrEmpty(ChangelogOutputPath) ? "CHANGELOG.md" : ChangelogOutputPath);
            if (File.Exists(outputPath) && File.ReadAllText(outputPath) == output)
            {
                return;
            }

            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }
            File.WriteAllText(outputPath, output);
            Log.LogMessage(MessageImportance.Normal, $"[Mister.Version] Changelog written to {outputPath}");
        }
        catch (Exception ex)
        {
            Log.LogWarning($"[Mister.Version] Failed to generate changelog: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates the project file with the calculated version
    /// Note: This is disabled by default to avoid requiring version changes to be committed
    /// </summary>
    private void UpdateProjectFileVersion()
    {
        try
        {
            var projectFile = File.ReadAllText(ProjectPath);

            // Update or add the Version element
            var versionRegex = new System.Text.RegularExpressions.Regex(@"<Version>.*?</Version>");
            if (versionRegex.IsMatch(projectFile))
            {
                // Update existing Version element
                projectFile = versionRegex.Replace(projectFile, $"<Version>{Version}</Version>");
            }
            else
            {
                // Add Version element after PropertyGroup opening tag
                var propertyGroupRegex = new System.Text.RegularExpressions.Regex(@"<PropertyGroup>");
                projectFile = propertyGroupRegex.Replace(projectFile, $"<PropertyGroup>\r\n    <Version>{Version}</Version>", 1);
            }

            File.WriteAllText(ProjectPath, projectFile);
            Log.LogMessage(MessageImportance.Normal, $"Updated project file with version {Version}");
        }
        catch (Exception ex)
        {
            Log.LogWarning($"Failed to update project file: {ex.Message}");
        }
    }

    /// <summary>
    /// Determines the type of branch based on its name
    /// </summary>
    internal BranchType DetermineBranchType(string branchName)
    {
        return RepositoryService.ExecuteGitOperation(RepoRoot, 
            gitService => gitService.GetBranchType(branchName), 
            BranchType.Feature);
    }

    /// <summary>
    /// Extracts version from release branch name
    /// </summary>
    internal SemVer ExtractReleaseVersion(string branchName)
    {
        return RepositoryService.ExecuteGitOperation(RepoRoot, 
            gitService => gitService.ExtractReleaseVersion(branchName, TagPrefix), 
            null);
    }

    /// <summary>
    /// Parses a string into a semantic version
    /// </summary>
    internal SemVer ParseSemVer(string version)
    {
        return RepositoryService.ExecuteGitOperation(RepoRoot,
            gitService => gitService.ParseSemVer(version),
            null);
    }

    /// <summary>
    /// Returns the repository fingerprint, memoized for the duration of the current build in this node
    /// (IBuildEngine4 registered task object), or null if the repository cannot be read.
    /// </summary>
    private string GetRepositoryFingerprint(string repoRoot)
    {
        var configFile = string.IsNullOrEmpty(ConfigFile) ? null : Path.GetFullPath(ConfigFile);
        var key = "Mister.Version.RepositoryFingerprint|" + repoRoot + "|" + configFile;

        try
        {
            if (BuildEngine is IBuildEngine4 engine4 &&
                engine4.GetRegisteredTaskObject(key, RegisteredTaskObjectLifetime.Build) is string memoized)
            {
                return memoized;
            }

            var fingerprint = RepositoryFingerprint.Compute(repoRoot, configFile);

            if (BuildEngine is IBuildEngine4 engine)
            {
                engine.RegisterTaskObject(key, fingerprint, RegisteredTaskObjectLifetime.Build, allowEarlyCollection: false);
            }

            return fingerprint;
        }
        catch (Exception ex)
        {
            // Caching is disabled if the repository state can't be fingerprinted; the calculation still runs.
            Log.LogMessage(MessageImportance.Low, $"[Mister.Version] Unable to fingerprint repository state: {ex.Message}");
            return null;
        }
    }

    private static readonly HashSet<string> NonKeyInputs = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(EnableCache), nameof(CacheFile), nameof(Debug), nameof(ExtraDebug), nameof(DryRun)
    };

    private static readonly System.Reflection.PropertyInfo[] KeyInputProperties = typeof(MonoRepoVersionTask)
        .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
        .Where(p => p.CanRead && p.CanWrite && !NonKeyInputs.Contains(p.Name) &&
                    !p.IsDefined(typeof(OutputAttribute), inherit: true))
        .OrderBy(p => p.Name, StringComparer.Ordinal)
        .ToArray();

    /// <summary>
    /// Stable description of every task input that can influence the calculated version.
    /// </summary>
    internal string DescribeInputs()
    {
        var sb = new System.Text.StringBuilder(1024);
        foreach (var property in KeyInputProperties)
        {
            var value = property.GetValue(this);
            string text;
            if (value is ITaskItem[] items)
            {
                text = string.Join("|", items
                    .Select(i => NormalizeForKey(i.GetMetadata("FullPath") is string full && full.Length > 0 ? full : i.ItemSpec))
                    .OrderBy(x => x, StringComparer.Ordinal));
            }
            else if (property.Name == nameof(ProjectPath) && value is string path && path.Length > 0)
            {
                text = NormalizeForKey(Path.GetFullPath(path));
            }
            else
            {
                text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            }
            sb.Append(property.Name).Append('=').Append(text).Append('\n');
        }
        return sb.ToString();
    }

    private static List<string> SplitPatterns(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? new List<string>()
            : value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();

    private List<string> _allDependencies;

    /// <summary>
    /// Direct ProjectReferences plus every transitive project reference NuGet restore recorded in the assets
    /// file, as full paths.
    /// </summary>
    internal List<string> GetAllProjectDependencies()
    {
        if (_allDependencies != null)
            return _allDependencies;

        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(ProjectPath));
        var all = new List<string>();
        foreach (var item in Dependencies ?? Array.Empty<ITaskItem>())
        {
            var full = item.GetMetadata("FullPath");
            all.Add(string.IsNullOrEmpty(full) ? Path.GetFullPath(Path.Combine(projectDirectory, item.ItemSpec)) : full);
        }
        all.AddRange(ReadProjectReferenceClosure(ProjectAssetsFile, projectDirectory));
        // Edges NuGet does not record (ReferenceOutputAssembly=false, PrivateAssets=all) further down the graph
        all.AddRange(ReadUnflowedReferenceClosure(all.ToList()));

        var self = Path.GetFullPath(ProjectPath);
        _allDependencies = all
            .Where(d => !string.Equals(d, self, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return _allDependencies;
    }

    /// <summary>
    /// Transitive project references from a project.assets.json (see <see cref="ProjectReferenceReader.ReadAssetsClosure"/>).
    /// </summary>
    internal static List<string> ReadProjectReferenceClosure(string assetsFile, string projectDirectory) =>
        ProjectReferenceReader.ReadAssetsClosure(assetsFile, projectDirectory);

    /// <summary>
    /// Projects the build depends on that NuGet's closure leaves out: below the given MSBuild/NuGet closure, the
    /// ReferenceOutputAssembly=false references (source generators, analyzers, build-order references) and
    /// PrivateAssets=all project references, which NuGet does not record or flow, and what they reference. Only
    /// references whose conditions are certain are followed (see <see cref="ProjectReferenceReader.GetUnflowedReferenceClosure"/>).
    /// </summary>
    internal static List<string> ReadUnflowedReferenceClosure(IEnumerable<string> buildClosure) =>
        ProjectReferenceReader.GetUnflowedReferenceClosure(buildClosure);

    private static string NormalizeForKey(string path) => path?.Replace('\\', '/');

}

internal static class PathUtilsForTask
{
    public static string GetRelativePath(string relativeTo, string path)
    {
#if NET472
        return Mister.Version.Core.PathUtils.GetRelativePath(relativeTo, path);
#else
        return Path.GetRelativePath(relativeTo, path);
#endif
    }
}
