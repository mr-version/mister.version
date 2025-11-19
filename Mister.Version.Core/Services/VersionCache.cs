using System;
using System.Collections.Generic;
using System.Linq;
using LibGit2Sharp;
using Mister.Version.Core.Models;

namespace Mister.Version.Core.Services
{
    /// <summary>
    /// Multi-level cache for version calculation operations to improve performance
    /// in multi-project builds by avoiding redundant git operations and project scans
    /// </summary>
    public class VersionCache
    {
        private readonly object _lock = new object();
        private string _currentHeadSha;
        private readonly string _repoRoot;

        // Project discovery cache
        private List<string> _allProjects;
        private Dictionary<string, List<string>> _projectDependenciesCache;

        // Git operations cache
        private List<VersionTag> _globalVersionTagsCache;
        private Dictionary<string, VersionTag> _projectVersionTagsCache;
        private Dictionary<string, int> _commitHeightCache;
        private Dictionary<string, bool> _projectChangesCache;

        // Version calculation cache
        private Dictionary<string, VersionResult> _versionResultsCache;

        public VersionCache(string repoRoot, string currentHeadSha)
        {
            _repoRoot = repoRoot ?? throw new ArgumentNullException(nameof(repoRoot));
            _currentHeadSha = currentHeadSha;

            _allProjects = null;
            _projectDependenciesCache = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            _globalVersionTagsCache = null;
            _projectVersionTagsCache = new Dictionary<string, VersionTag>(StringComparer.OrdinalIgnoreCase);
            _commitHeightCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _projectChangesCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            _versionResultsCache = new Dictionary<string, VersionResult>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Invalidates the cache if the git HEAD has changed
        /// </summary>
        public bool ValidateAndInvalidate(string newHeadSha)
        {
            lock (_lock)
            {
                if (_currentHeadSha != newHeadSha)
                {
                    ClearAll();
                    _currentHeadSha = newHeadSha;
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Clears all cached data
        /// </summary>
        public void ClearAll()
        {
            lock (_lock)
            {
                _allProjects = null;
                _projectDependenciesCache.Clear();

                _globalVersionTagsCache = null;
                _projectVersionTagsCache.Clear();
                _commitHeightCache.Clear();
                _projectChangesCache.Clear();

                _versionResultsCache.Clear();
            }
        }

        #region Project Discovery Cache

        /// <summary>
        /// Gets or sets the cached list of all projects in the repository
        /// </summary>
        public List<string> GetAllProjects()
        {
            lock (_lock)
            {
                return _allProjects?.ToList(); // Return a copy to avoid external modification
            }
        }

        public void SetAllProjects(List<string> projects)
        {
            lock (_lock)
            {
                _allProjects = projects?.ToList();
            }
        }

        /// <summary>
        /// Gets cached project dependencies
        /// </summary>
        public List<string> GetProjectDependencies(string projectPath)
        {
            lock (_lock)
            {
                if (_projectDependenciesCache.TryGetValue(projectPath, out var deps))
                {
                    return deps?.ToList();
                }
                return null;
            }
        }

        public void SetProjectDependencies(string projectPath, List<string> dependencies)
        {
            lock (_lock)
            {
                _projectDependenciesCache[projectPath] = dependencies?.ToList();
            }
        }

        #endregion

        #region Git Operations Cache

        /// <summary>
        /// Gets cached global version tags
        /// </summary>
        public List<VersionTag> GetGlobalVersionTags()
        {
            lock (_lock)
            {
                return _globalVersionTagsCache?.ToList();
            }
        }

        public void SetGlobalVersionTags(List<VersionTag> tags)
        {
            lock (_lock)
            {
                _globalVersionTagsCache = tags?.ToList();
            }
        }

        /// <summary>
        /// Gets cached project version tag
        /// </summary>
        public VersionTag GetProjectVersionTag(string cacheKey)
        {
            lock (_lock)
            {
                if (_projectVersionTagsCache.TryGetValue(cacheKey, out var tag))
                {
                    return tag;
                }
                return null;
            }
        }

        public void SetProjectVersionTag(string cacheKey, VersionTag tag)
        {
            lock (_lock)
            {
                _projectVersionTagsCache[cacheKey] = tag;
            }
        }

        /// <summary>
        /// Gets cached commit height
        /// </summary>
        public int? GetCommitHeight(string commitSha)
        {
            lock (_lock)
            {
                if (string.IsNullOrEmpty(commitSha))
                    return null;

                if (_commitHeightCache.TryGetValue(commitSha, out var height))
                {
                    return height;
                }
                return null;
            }
        }

        public void SetCommitHeight(string commitSha, int height)
        {
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(commitSha))
                {
                    _commitHeightCache[commitSha] = height;
                }
            }
        }

        /// <summary>
        /// Gets cached project changes status
        /// </summary>
        public bool? GetProjectHasChanges(string cacheKey)
        {
            lock (_lock)
            {
                if (_projectChangesCache.TryGetValue(cacheKey, out var hasChanges))
                {
                    return hasChanges;
                }
                return null;
            }
        }

        public void SetProjectHasChanges(string cacheKey, bool hasChanges)
        {
            lock (_lock)
            {
                _projectChangesCache[cacheKey] = hasChanges;
            }
        }

        #endregion

        #region Version Calculation Cache

        /// <summary>
        /// Gets cached version result for a project
        /// </summary>
        public VersionResult GetVersionResult(string projectPath)
        {
            lock (_lock)
            {
                if (_versionResultsCache.TryGetValue(projectPath, out var result))
                {
                    return result;
                }
                return null;
            }
        }

        public void SetVersionResult(string projectPath, VersionResult result)
        {
            lock (_lock)
            {
                _versionResultsCache[projectPath] = result;
            }
        }

        #endregion

        /// <summary>
        /// Gets cache statistics for diagnostics
        /// </summary>
        public CacheStatistics GetStatistics()
        {
            lock (_lock)
            {
                return new CacheStatistics
                {
                    CurrentHeadSha = _currentHeadSha,
                    AllProjectsCached = _allProjects != null,
                    ProjectDependenciesCount = _projectDependenciesCache.Count,
                    GlobalVersionTagsCached = _globalVersionTagsCache != null,
                    ProjectVersionTagsCount = _projectVersionTagsCache.Count,
                    CommitHeightCount = _commitHeightCache.Count,
                    ProjectChangesCount = _projectChangesCache.Count,
                    VersionResultsCount = _versionResultsCache.Count
                };
            }
        }
    }

    /// <summary>
    /// Statistics about the cache contents for diagnostics
    /// </summary>
    public class CacheStatistics
    {
        public string CurrentHeadSha { get; set; }
        public bool AllProjectsCached { get; set; }
        public int ProjectDependenciesCount { get; set; }
        public bool GlobalVersionTagsCached { get; set; }
        public int ProjectVersionTagsCount { get; set; }
        public int CommitHeightCount { get; set; }
        public int ProjectChangesCount { get; set; }
        public int VersionResultsCount { get; set; }

        public override string ToString()
        {
            var headShaDisplay = string.IsNullOrEmpty(CurrentHeadSha)
                ? "null"
                : (CurrentHeadSha.Length >= 8 ? CurrentHeadSha.Substring(0, 8) : CurrentHeadSha);

            return $"VersionCache Statistics: HEAD={headShaDisplay}, " +
                   $"AllProjects={AllProjectsCached}, " +
                   $"ProjectDeps={ProjectDependenciesCount}, " +
                   $"GlobalTags={GlobalVersionTagsCached}, " +
                   $"ProjectTags={ProjectVersionTagsCount}, " +
                   $"CommitHeights={CommitHeightCount}, " +
                   $"ProjectChanges={ProjectChangesCount}, " +
                   $"VersionResults={VersionResultsCount}";
        }
    }
}
