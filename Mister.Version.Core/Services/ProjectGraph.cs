using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mister.Version.Core.Models;

namespace Mister.Version.Core.Services
{
    /// <summary>
    /// Project-reference graph over a set of <see cref="ProjectInfo"/>. Edges are resolved by project file path when
    /// known (<see cref="ProjectInfo.DirectDependencyPaths"/>), so two projects with the same file name in different
    /// directories stay distinct, and by name otherwise. Lookups are memoized, so the transitive closure is linear in
    /// the size of the graph even with many diamonds.
    /// </summary>
    internal sealed class ProjectGraph
    {
        private readonly List<ProjectInfo> _projects;
        private readonly Dictionary<string, ProjectInfo> _byPath = new Dictionary<string, ProjectInfo>(StringComparer.Ordinal);
        // Fallback for case-insensitive file systems (Windows, macOS), where references can differ in case
        private readonly Dictionary<string, ProjectInfo> _byPathIgnoreCase = new Dictionary<string, ProjectInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ProjectInfo> _byName = new Dictionary<string, ProjectInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _duplicateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<ProjectInfo, List<ProjectInfo>> _direct = new Dictionary<ProjectInfo, List<ProjectInfo>>(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<ProjectInfo, List<ProjectInfo>> _closure = new Dictionary<ProjectInfo, List<ProjectInfo>>(ReferenceEqualityComparer.Instance);
        private Dictionary<ProjectInfo, List<ProjectInfo>> _dependents;

        public ProjectGraph(IEnumerable<ProjectInfo> projects)
        {
            _projects = (projects ?? Enumerable.Empty<ProjectInfo>()).Where(p => p != null).ToList();
            foreach (var project in _projects)
            {
                var key = NormalizePath(project.FullPath);
                if (key != null && !_byPath.ContainsKey(key))
                    _byPath[key] = project;
                if (key != null && !_byPathIgnoreCase.ContainsKey(key))
                    _byPathIgnoreCase[key] = project;

                if (string.IsNullOrEmpty(project.Name))
                    continue;
                if (_byName.ContainsKey(project.Name))
                    _duplicateNames.Add(project.Name);
                else
                    _byName[project.Name] = project;
            }
        }

        public IReadOnlyList<ProjectInfo> Projects => _projects;

        /// <summary>
        /// Display name: the project name, plus its directory when another project in the graph has the same name.
        /// </summary>
        public string Label(ProjectInfo project)
        {
            if (project?.Name == null || !_duplicateNames.Contains(project.Name))
                return project?.Name;
            var directory = Path.GetDirectoryName(project.Path ?? project.FullPath ?? "")?.Replace('\\', '/');
            return string.IsNullOrEmpty(directory) ? project.Name : $"{project.Name} ({directory})";
        }

        /// <summary>The projects in the graph that <paramref name="project"/> references directly.</summary>
        public List<ProjectInfo> DirectDependencies(ProjectInfo project)
        {
            if (_direct.TryGetValue(project, out var cached))
                return cached;

            var result = new List<ProjectInfo>();
            if (project.DirectDependencyPaths != null && project.DirectDependencyPaths.Count > 0)
            {
                foreach (var path in project.DirectDependencyPaths)
                {
                    var key = NormalizePath(path);
                    if (key != null && (_byPath.TryGetValue(key, out var dependency) || _byPathIgnoreCase.TryGetValue(key, out dependency)))
                        AddDistinct(result, dependency, project);
                }
            }
            else
            {
                foreach (var name in project.DirectDependencies ?? new List<string>())
                {
                    if (name != null && _byName.TryGetValue(name, out var dependency))
                        AddDistinct(result, dependency, project);
                }
            }

            _direct[project] = result;
            return result;
        }

        /// <summary>
        /// Every project in the graph that <paramref name="project"/> depends on directly or transitively, in
        /// breadth-first order (direct dependencies first). Cycles are tolerated.
        /// </summary>
        public List<ProjectInfo> AllDependencies(ProjectInfo project)
        {
            if (_closure.TryGetValue(project, out var cached))
                return cached;

            var result = new List<ProjectInfo>();
            var seen = new HashSet<ProjectInfo>(ReferenceEqualityComparer.Instance) { project };
            var queue = new Queue<ProjectInfo>();
            queue.Enqueue(project);
            while (queue.Count > 0)
            {
                foreach (var dependency in DirectDependencies(queue.Dequeue()))
                {
                    if (seen.Add(dependency))
                    {
                        result.Add(dependency);
                        queue.Enqueue(dependency);
                    }
                }
            }

            _closure[project] = result;
            return result;
        }

        /// <summary>The projects in the graph that reference <paramref name="project"/> directly.</summary>
        public List<ProjectInfo> Dependents(ProjectInfo project)
        {
            if (_dependents == null)
            {
                _dependents = new Dictionary<ProjectInfo, List<ProjectInfo>>(ReferenceEqualityComparer.Instance);
                foreach (var candidate in _projects)
                {
                    foreach (var dependency in DirectDependencies(candidate))
                    {
                        if (!_dependents.TryGetValue(dependency, out var list))
                            _dependents[dependency] = list = new List<ProjectInfo>();
                        list.Add(candidate);
                    }
                }
            }

            return _dependents.TryGetValue(project, out var dependents) ? dependents : new List<ProjectInfo>();
        }

        /// <summary>
        /// The project named <paramref name="name"/> that <paramref name="project"/> depends on (searching its own
        /// dependency closure first, so a duplicated name resolves to the right project), or any project with that
        /// name, or null.
        /// </summary>
        public ProjectInfo FindDependency(ProjectInfo project, string name)
        {
            if (name == null)
                return null;
            if (project != null)
            {
                foreach (var dependency in AllDependencies(project))
                {
                    if (string.Equals(dependency.Name, name, StringComparison.OrdinalIgnoreCase))
                        return dependency;
                }
            }
            return _byName.TryGetValue(name, out var byName) ? byName : null;
        }

        private static void AddDistinct(List<ProjectInfo> list, ProjectInfo dependency, ProjectInfo self)
        {
            if (!ReferenceEquals(dependency, self) && !list.Any(d => ReferenceEquals(d, dependency)))
                list.Add(dependency);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            try
            {
                return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception)
            {
                return path;
            }
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<ProjectInfo>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
            public bool Equals(ProjectInfo x, ProjectInfo y) => ReferenceEquals(x, y);
            public int GetHashCode(ProjectInfo obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
