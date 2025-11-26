using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Mister.Version.Core.Models;

namespace Mister.Version.Core.Services
{
    /// <summary>
    /// File-based cache for version calculation results.
    /// Stores version results in .mrversion/[git-sha]/[project-name]/version.props format.
    /// This enables fast version lookups when the git SHA hasn't changed.
    /// </summary>
    public class FileVersionCache
    {
        private readonly string _repoRoot;
        private readonly string _cacheBasePath;
        private readonly Action<string, string> _logger;
        private readonly object _lock = new object();

        /// <summary>
        /// Default cache folder name
        /// </summary>
        public const string DefaultCacheFolderName = ".mrversion";

        /// <summary>
        /// Creates a new FileVersionCache instance
        /// </summary>
        /// <param name="repoRoot">The repository root path</param>
        /// <param name="cacheBasePath">Optional custom cache base path. If null, uses .mrversion in repo root</param>
        /// <param name="logger">Optional logger function</param>
        public FileVersionCache(string repoRoot, string cacheBasePath = null, Action<string, string> logger = null)
        {
            _repoRoot = repoRoot ?? throw new ArgumentNullException(nameof(repoRoot));
            _cacheBasePath = cacheBasePath ?? Path.Combine(repoRoot, DefaultCacheFolderName);
            _logger = logger ?? ((level, message) => { });
        }

        /// <summary>
        /// Gets the cache directory path for a specific git SHA and project
        /// </summary>
        /// <param name="gitSha">The git commit SHA</param>
        /// <param name="projectName">The project name</param>
        /// <returns>The full path to the cache directory</returns>
        public string GetCachePath(string gitSha, string projectName)
        {
            if (string.IsNullOrEmpty(gitSha))
                throw new ArgumentNullException(nameof(gitSha));
            if (string.IsNullOrEmpty(projectName))
                throw new ArgumentNullException(nameof(projectName));

            // Use short SHA (first 8 characters) for directory name to keep paths manageable
            var shortSha = gitSha.Length > 8 ? gitSha.Substring(0, 8) : gitSha;

            // Sanitize project name for use in file path
            var sanitizedProjectName = SanitizeProjectName(projectName);

            return Path.Combine(_cacheBasePath, shortSha, sanitizedProjectName);
        }

        /// <summary>
        /// Gets the full path to the version.props file for a specific git SHA and project
        /// </summary>
        /// <param name="gitSha">The git commit SHA</param>
        /// <param name="projectName">The project name</param>
        /// <returns>The full path to the version.props file</returns>
        public string GetVersionPropsPath(string gitSha, string projectName)
        {
            return Path.Combine(GetCachePath(gitSha, projectName), "version.props");
        }

        /// <summary>
        /// Tries to get a cached version result for the given git SHA and project
        /// </summary>
        /// <param name="gitSha">The git commit SHA</param>
        /// <param name="projectName">The project name</param>
        /// <returns>The cached VersionResult if found and valid, null otherwise</returns>
        public VersionResult TryGetCachedVersion(string gitSha, string projectName)
        {
            if (string.IsNullOrEmpty(gitSha) || string.IsNullOrEmpty(projectName))
                return null;

            lock (_lock)
            {
                try
                {
                    var propsPath = GetVersionPropsPath(gitSha, projectName);

                    if (!File.Exists(propsPath))
                    {
                        _logger("Debug", $"File cache miss for {projectName} at {gitSha.Substring(0, Math.Min(8, gitSha.Length))}");
                        return null;
                    }

                    var result = ReadVersionProps(propsPath);

                    if (result != null)
                    {
                        _logger("Info", $"File cache hit for {projectName} at {gitSha.Substring(0, Math.Min(8, gitSha.Length))}: {result.Version}");
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    _logger("Warning", $"Failed to read cached version for {projectName}: {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// Caches a version result for the given git SHA and project
        /// </summary>
        /// <param name="gitSha">The git commit SHA</param>
        /// <param name="projectName">The project name</param>
        /// <param name="result">The version result to cache</param>
        /// <returns>True if caching succeeded, false otherwise</returns>
        public bool CacheVersion(string gitSha, string projectName, VersionResult result)
        {
            if (string.IsNullOrEmpty(gitSha) || string.IsNullOrEmpty(projectName) || result == null)
                return false;

            lock (_lock)
            {
                try
                {
                    var cachePath = GetCachePath(gitSha, projectName);
                    var propsPath = GetVersionPropsPath(gitSha, projectName);

                    // Ensure directory exists
                    Directory.CreateDirectory(cachePath);

                    // Write version.props file
                    WriteVersionProps(propsPath, result, gitSha, projectName);

                    _logger("Debug", $"Cached version {result.Version} for {projectName} at {gitSha.Substring(0, Math.Min(8, gitSha.Length))}");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger("Warning", $"Failed to cache version for {projectName}: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Checks if a cached version exists for the given git SHA and project
        /// </summary>
        /// <param name="gitSha">The git commit SHA</param>
        /// <param name="projectName">The project name</param>
        /// <returns>True if a cached version exists</returns>
        public bool HasCachedVersion(string gitSha, string projectName)
        {
            if (string.IsNullOrEmpty(gitSha) || string.IsNullOrEmpty(projectName))
                return false;

            lock (_lock)
            {
                var propsPath = GetVersionPropsPath(gitSha, projectName);
                return File.Exists(propsPath);
            }
        }

        /// <summary>
        /// Clears all cached versions for a specific git SHA
        /// </summary>
        /// <param name="gitSha">The git commit SHA to clear cache for</param>
        public void ClearCacheForSha(string gitSha)
        {
            if (string.IsNullOrEmpty(gitSha))
                return;

            lock (_lock)
            {
                try
                {
                    var shortSha = gitSha.Length > 8 ? gitSha.Substring(0, 8) : gitSha;
                    var shaPath = Path.Combine(_cacheBasePath, shortSha);

                    if (Directory.Exists(shaPath))
                    {
                        Directory.Delete(shaPath, true);
                        _logger("Debug", $"Cleared file cache for SHA {shortSha}");
                    }
                }
                catch (Exception ex)
                {
                    _logger("Warning", $"Failed to clear cache for SHA {gitSha}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Clears all cached versions (entire .mrversion folder)
        /// </summary>
        public void ClearAllCache()
        {
            lock (_lock)
            {
                try
                {
                    if (Directory.Exists(_cacheBasePath))
                    {
                        Directory.Delete(_cacheBasePath, true);
                        _logger("Info", "Cleared all file cache");
                    }
                }
                catch (Exception ex)
                {
                    _logger("Warning", $"Failed to clear all cache: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Cleans up old cache entries, keeping only the most recent N SHA directories
        /// </summary>
        /// <param name="keepCount">Number of SHA directories to keep (default: 10)</param>
        public void CleanupOldCache(int keepCount = 10)
        {
            lock (_lock)
            {
                try
                {
                    if (!Directory.Exists(_cacheBasePath))
                        return;

                    var shaDirs = Directory.GetDirectories(_cacheBasePath);

                    if (shaDirs.Length <= keepCount)
                        return;

                    // Sort by last write time, oldest first
                    var sortedDirs = new List<(string Path, DateTime LastWrite)>();
                    foreach (var dir in shaDirs)
                    {
                        var lastWrite = Directory.GetLastWriteTimeUtc(dir);
                        sortedDirs.Add((dir, lastWrite));
                    }
                    sortedDirs.Sort((a, b) => a.LastWrite.CompareTo(b.LastWrite));

                    // Delete oldest directories beyond keepCount
                    var toDelete = sortedDirs.Count - keepCount;
                    for (int i = 0; i < toDelete; i++)
                    {
                        try
                        {
                            Directory.Delete(sortedDirs[i].Path, true);
                            _logger("Debug", $"Cleaned up old cache: {Path.GetFileName(sortedDirs[i].Path)}");
                        }
                        catch (Exception ex)
                        {
                            _logger("Warning", $"Failed to delete old cache {sortedDirs[i].Path}: {ex.Message}");
                        }
                    }

                    _logger("Info", $"Cleaned up {toDelete} old cache entries");
                }
                catch (Exception ex)
                {
                    _logger("Warning", $"Failed to cleanup old cache: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Gets statistics about the file cache
        /// </summary>
        /// <returns>File cache statistics</returns>
        public FileCacheStatistics GetStatistics()
        {
            lock (_lock)
            {
                var stats = new FileCacheStatistics
                {
                    CacheBasePath = _cacheBasePath,
                    Exists = Directory.Exists(_cacheBasePath)
                };

                if (!stats.Exists)
                    return stats;

                try
                {
                    var shaDirs = Directory.GetDirectories(_cacheBasePath);
                    stats.ShaCount = shaDirs.Length;

                    foreach (var shaDir in shaDirs)
                    {
                        var projectDirs = Directory.GetDirectories(shaDir);
                        stats.TotalProjectCount += projectDirs.Length;
                    }
                }
                catch (Exception ex)
                {
                    _logger("Warning", $"Failed to get cache statistics: {ex.Message}");
                }

                return stats;
            }
        }

        /// <summary>
        /// Reads a version.props file and parses it into a VersionResult
        /// </summary>
        private VersionResult ReadVersionProps(string propsPath)
        {
            var doc = XDocument.Load(propsPath);
            var root = doc.Root;

            if (root == null || root.Name.LocalName != "Project")
                return null;

            var propertyGroup = root.Element("PropertyGroup");
            if (propertyGroup == null)
                return null;

            var result = new VersionResult
            {
                Version = GetElementValue(propertyGroup, "MrVersion"),
                VersionChanged = bool.TryParse(GetElementValue(propertyGroup, "MrVersionChanged"), out var changed) && changed,
                ChangeReason = GetElementValue(propertyGroup, "MrVersionChangeReason"),
                CommitSha = GetElementValue(propertyGroup, "MrVersionCommitSha"),
                BranchName = GetElementValue(propertyGroup, "MrVersionBranchName"),
                PreviousVersion = GetElementValue(propertyGroup, "MrVersionPrevious")
            };

            // Parse SemVer
            if (!string.IsNullOrEmpty(result.Version))
            {
                result.SemVer = ParseSemVer(result.Version);
            }

            // Parse BranchType
            if (Enum.TryParse<BranchType>(GetElementValue(propertyGroup, "MrVersionBranchType"), true, out var branchType))
            {
                result.BranchType = branchType;
            }

            // Parse CommitHeight
            if (int.TryParse(GetElementValue(propertyGroup, "MrVersionCommitHeight"), out var commitHeight))
            {
                result.CommitHeight = commitHeight;
            }

            // Parse CommitDate
            if (DateTime.TryParse(GetElementValue(propertyGroup, "MrVersionCommitDate"), out var commitDate))
            {
                result.CommitDate = commitDate;
            }

            // Parse Scheme
            if (Enum.TryParse<VersionScheme>(GetElementValue(propertyGroup, "MrVersionScheme"), true, out var scheme))
            {
                result.Scheme = scheme;
            }

            // Parse BumpType
            if (Enum.TryParse<VersionBumpType>(GetElementValue(propertyGroup, "MrVersionBumpType"), true, out var bumpType))
            {
                result.BumpType = bumpType;
            }

            // Parse ConventionalCommitsEnabled
            if (bool.TryParse(GetElementValue(propertyGroup, "MrVersionConventionalCommitsEnabled"), out var ccEnabled))
            {
                result.ConventionalCommitsEnabled = ccEnabled;
            }

            return result;
        }

        /// <summary>
        /// Writes a VersionResult to a version.props file in MSBuild-compatible format
        /// </summary>
        private void WriteVersionProps(string propsPath, VersionResult result, string gitSha, string projectName)
        {
            var doc = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XComment($" Mister.Version cache file for {projectName} "),
                new XComment($" Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC "),
                new XComment($" Git SHA: {gitSha} "),
                new XElement("Project",
                    new XElement("PropertyGroup",
                        new XElement("MrVersion", result.Version ?? ""),
                        new XElement("MrVersionChanged", result.VersionChanged.ToString().ToLowerInvariant()),
                        new XElement("MrVersionChangeReason", result.ChangeReason ?? ""),
                        new XElement("MrVersionCommitSha", result.CommitSha ?? ""),
                        new XElement("MrVersionCommitDate", result.CommitDate?.ToString("o") ?? ""),
                        new XElement("MrVersionBranchName", result.BranchName ?? ""),
                        new XElement("MrVersionBranchType", result.BranchType.ToString()),
                        new XElement("MrVersionCommitHeight", result.CommitHeight.ToString()),
                        new XElement("MrVersionPrevious", result.PreviousVersion ?? ""),
                        new XElement("MrVersionScheme", result.Scheme.ToString()),
                        new XElement("MrVersionBumpType", result.BumpType?.ToString() ?? ""),
                        new XElement("MrVersionConventionalCommitsEnabled", result.ConventionalCommitsEnabled.ToString().ToLowerInvariant()),
                        new XElement("MrVersionCachedAt", DateTime.UtcNow.ToString("o")),
                        new XElement("MrVersionGitSha", gitSha),
                        new XElement("MrVersionProjectName", projectName)
                    )
                )
            );

            doc.Save(propsPath);
        }

        /// <summary>
        /// Gets the value of an XML element, or empty string if not found
        /// </summary>
        private string GetElementValue(XElement parent, string elementName)
        {
            return parent.Element(elementName)?.Value ?? "";
        }

        /// <summary>
        /// Sanitizes a project name for use in file paths
        /// </summary>
        private string SanitizeProjectName(string projectName)
        {
            // Replace invalid path characters with underscores
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = projectName;

            foreach (var c in invalidChars)
            {
                sanitized = sanitized.Replace(c, '_');
            }

            return sanitized;
        }

        /// <summary>
        /// Simple SemVer parser for cached versions
        /// </summary>
        private SemVer ParseSemVer(string version)
        {
            if (string.IsNullOrEmpty(version))
                return null;

            try
            {
                var semver = new SemVer();
                var versionPart = version;

                // Handle build metadata
                var plusIndex = version.IndexOf('+');
                if (plusIndex >= 0)
                {
                    semver.BuildMetadata = version.Substring(plusIndex + 1);
                    versionPart = version.Substring(0, plusIndex);
                }

                // Handle prerelease
                var dashIndex = versionPart.IndexOf('-');
                if (dashIndex >= 0)
                {
                    semver.PreRelease = versionPart.Substring(dashIndex + 1);
                    versionPart = versionPart.Substring(0, dashIndex);
                }

                // Parse major.minor.patch
                var parts = versionPart.Split('.');
                if (parts.Length >= 1 && int.TryParse(parts[0], out var major))
                    semver.Major = major;
                if (parts.Length >= 2 && int.TryParse(parts[1], out var minor))
                    semver.Minor = minor;
                if (parts.Length >= 3 && int.TryParse(parts[2], out var patch))
                    semver.Patch = patch;

                return semver;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Statistics about the file cache
    /// </summary>
    public class FileCacheStatistics
    {
        /// <summary>
        /// The base path of the cache
        /// </summary>
        public string CacheBasePath { get; set; }

        /// <summary>
        /// Whether the cache directory exists
        /// </summary>
        public bool Exists { get; set; }

        /// <summary>
        /// Number of SHA directories in the cache
        /// </summary>
        public int ShaCount { get; set; }

        /// <summary>
        /// Total number of cached projects across all SHAs
        /// </summary>
        public int TotalProjectCount { get; set; }

        public override string ToString()
        {
            if (!Exists)
                return $"FileCache: Not initialized (path: {CacheBasePath})";

            return $"FileCache: {ShaCount} SHA(s), {TotalProjectCount} project(s) cached at {CacheBasePath}";
        }
    }
}
