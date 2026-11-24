using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Mister.Version.Core.Services
{
    /// <summary>
    /// Finds the project files (*.csproj, *.fsproj, *.vbproj) that belong to a repository.
    /// </summary>
    public static class ProjectDiscovery
    {
        private static readonly string[] ProjectExtensions = { ".csproj", ".fsproj", ".vbproj" };

        /// <summary>Directories never scanned for projects.</summary>
        private static readonly HashSet<string> SkippedDirectoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", "node_modules"
        };

        /// <summary>
        /// Project files under <paramref name="root"/>, sorted by path. Skips directories that are symbolic links or
        /// junctions (the same project would be found twice, and link loops never end) and directories that are
        /// another repository or worktree (they contain a .git file or directory: nested worktrees, submodules,
        /// nested clones); projects there are versioned from their own repository.
        /// </summary>
        public static List<string> FindProjectFiles(string root, Action<string, string> logger = null)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return result;

            var pending = new Stack<string>();
            pending.Push(Path.GetFullPath(root));
            var isRoot = true;

            while (pending.Count > 0)
            {
                var directory = pending.Pop();

                if (!isRoot)
                {
                    if (IsLink(directory))
                    {
                        logger?.Invoke("Debug", $"Skipping linked directory {directory}");
                        continue;
                    }

                    if (File.Exists(Path.Combine(directory, ".git")) || Directory.Exists(Path.Combine(directory, ".git")))
                    {
                        logger?.Invoke("Debug", $"Skipping {directory}: it is another repository, worktree or submodule (versioned from its own repository)");
                        continue;
                    }
                }
                isRoot = false;

                try
                {
                    foreach (var file in Directory.EnumerateFiles(directory))
                    {
                        if (ProjectExtensions.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                            result.Add(file);
                    }

                    foreach (var subdirectory in Directory.EnumerateDirectories(directory))
                    {
                        if (!SkippedDirectoryNames.Contains(Path.GetFileName(subdirectory)))
                            pending.Push(subdirectory);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    logger?.Invoke("Debug", $"Skipping inaccessible directory {directory}");
                }
                catch (IOException ex)
                {
                    logger?.Invoke("Debug", $"Skipping directory {directory}: {ex.Message}");
                }
            }

            result.Sort(StringComparer.Ordinal);
            return result;
        }

        private static bool IsLink(string directory)
        {
            try
            {
                return (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
