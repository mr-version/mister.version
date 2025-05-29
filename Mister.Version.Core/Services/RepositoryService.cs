using System;
using Mister.Version.Core.Models;

namespace Mister.Version.Core.Services
{
    /// <summary>
    /// Service for repository discovery and common Git operations
    /// </summary>
    public static class RepositoryService
    {
        // Common error messages
        public const string NoRepositoryFoundError = "No Git repository found starting from: {0}";
        public const string EnsureGitRepositoryMessage = "Please ensure you're running this from within a Git repository.";
        public const string InitializationTimeoutError = "Git repository initialization timed out.";
        public const string LibGit2SharpIssuesMessage = "This may be due to LibGit2Sharp native library issues.";
        public const string EnsureGitRepositoryCliMessage = "Please ensure you are running this command from within a git repository.";

        /// <summary>
        /// Discovers repository root with standardized error handling and logging
        /// </summary>
        /// <param name="startPath">Path to start discovery from</param>
        /// <param name="logger">Logger for debug/error messages</param>
        /// <param name="pathDescription">Description of the path for logging (e.g., "search path", "repository path")</param>
        /// <returns>Repository root path or null if not found</returns>
        public static string DiscoverRepository(string startPath, Action<string, string> logger, string pathDescription = "path")
        {
            logger?.Invoke("Debug", $"Discovering Git repository from: {startPath}");
            
            var gitRepoRoot = GitRepositoryHelper.DiscoverRepositoryRoot(startPath);
            if (gitRepoRoot == null)
            {
                logger?.Invoke("Error", string.Format(NoRepositoryFoundError, startPath));
                return null;
            }
            
            logger?.Invoke("Debug", $"Git repository found at: {gitRepoRoot}");
            return gitRepoRoot;
        }

        /// <summary>
        /// Discovers repository with error output to console (for CLI scenarios)
        /// </summary>
        /// <param name="startPath">Path to start discovery from</param>
        /// <param name="logger">Logger for debug messages</param>
        /// <returns>Repository root path or null if not found</returns>
        public static string DiscoverRepositoryWithConsoleErrors(string startPath, Action<string, string> logger)
        {
            var gitRepoRoot = DiscoverRepository(startPath, logger);
            if (gitRepoRoot == null)
            {
                Console.Error.WriteLine(string.Format(NoRepositoryFoundError, startPath));
                Console.Error.WriteLine(EnsureGitRepositoryCliMessage);
                return null;
            }
            return gitRepoRoot;
        }

        /// <summary>
        /// Creates a GitService with timeout and error handling
        /// </summary>
        /// <param name="repoRoot">Repository root path</param>
        /// <param name="logger">Logger for messages</param>
        /// <param name="timeoutSeconds">Timeout in seconds for initialization</param>
        /// <returns>GitService instance or null if failed</returns>
        public static IGitService CreateGitServiceWithTimeout(string repoRoot, Action<string, string> logger, int timeoutSeconds = 10)
        {
            logger?.Invoke("Debug", $"Initializing GitService for repo: {repoRoot}");
            
            try
            {
                // Use a timeout to prevent hanging on LibGit2Sharp initialization
                var initTask = System.Threading.Tasks.Task.Run(() => new GitService(repoRoot));
                if (initTask.Wait(TimeSpan.FromSeconds(timeoutSeconds)))
                {
                    var gitService = initTask.Result;
                    logger?.Invoke("Debug", "GitService initialized successfully");
                    return gitService;
                }
                else
                {
                    logger?.Invoke("Error", $"GitService initialization timed out after {timeoutSeconds} seconds");
                    return null;
                }
            }
            catch (Exception ex)
            {
                logger?.Invoke("Error", $"Failed to initialize GitService: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Creates a GitService with timeout and console error output (for CLI scenarios)
        /// </summary>
        /// <param name="repoRoot">Repository root path</param>
        /// <param name="logger">Logger for debug messages</param>
        /// <param name="timeoutSeconds">Timeout in seconds for initialization</param>
        /// <returns>GitService instance or null if failed</returns>
        public static IGitService CreateGitServiceWithConsoleErrors(string repoRoot, Action<string, string> logger, int timeoutSeconds = 10)
        {
            var gitService = CreateGitServiceWithTimeout(repoRoot, logger, timeoutSeconds);
            if (gitService == null)
            {
                Console.Error.WriteLine(InitializationTimeoutError);
                Console.Error.WriteLine(LibGit2SharpIssuesMessage);
                Console.Error.WriteLine($"Git repository error occurred.");
                Console.Error.WriteLine(EnsureGitRepositoryCliMessage);
                return null;
            }
            return gitService;
        }

        /// <summary>
        /// Helper method to safely execute a GitService operation
        /// </summary>
        /// <typeparam name="T">Return type</typeparam>
        /// <param name="repoRoot">Repository root path</param>
        /// <param name="operation">Operation to execute with GitService</param>
        /// <param name="defaultValue">Default value if operation fails</param>
        /// <returns>Result of operation or default value</returns>
        public static T ExecuteGitOperation<T>(string repoRoot, Func<IGitService, T> operation, T defaultValue = default(T))
        {
            try
            {
                using var gitService = new GitService(repoRoot);
                return operation(gitService);
            }
            catch
            {
                return defaultValue;
            }
        }
    }
}