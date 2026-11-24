using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Mister.Version.Core.Models;

namespace Mister.Version.Core.Services
{
    public interface IProjectAnalyzer
    {
        List<ProjectInfo> AnalyzeProjects(string repositoryPath, string projectDirectory = null);
        ProjectInfo AnalyzeProject(string projectPath, string repositoryPath);
        ProjectInfo AnalyzeProject(string projectPath, string repositoryPath, VersionOptions additionalOptions);
        List<string> GetProjectDependencies(string projectPath, string repositoryPath);
        void BuildDependencyGraph(List<ProjectInfo> projects);
    }

    /// <summary>
    /// Project kind as MSBuild would evaluate it (IsTestProject / IsPackable).
    /// </summary>
    public sealed class ProjectTypeInfo
    {
        public bool IsTestProject { get; set; }
        public bool IsPackable { get; set; } = true;
    }

    public class ProjectAnalyzer : IProjectAnalyzer
    {
        // Regex patterns for project file parsing
        private const string PATTERN_PROJECT_REFERENCE = @"<ProjectReference\s+Include\s*=\s*""([^""]+)""";
        private const string PATTERN_IS_TEST_PROJECT = @"<IsTestProject\s*>\s*(true|false)\s*</IsTestProject>";
        private const string PATTERN_IS_PACKABLE = @"<IsPackable\s*>\s*(true|false)\s*</IsPackable>";
        private const string PATTERN_TEST_SDK_REFERENCE = @"<PackageReference[^>]+Include\s*=\s*""Microsoft\.NET\.Test\.Sdk""";

        /// <summary>
        /// The package whose build props set IsTestProject=true (test framework packages such as xunit, NUnit or
        /// MSTest.TestFramework do not, so a library referencing only those is not a test project).
        /// </summary>
        private const string TEST_SDK_PACKAGE = "Microsoft.NET.Test.Sdk";

        private const string DIRECTORY_BUILD_PROPS = "Directory.Build.props";

        private readonly IVersionCalculator _versionCalculator;
        private readonly IGitService _gitService;
        private readonly Action<string, string> _logger;
        private VersionCache _cache;

        public ProjectAnalyzer(IVersionCalculator versionCalculator, IGitService gitService, Action<string, string> logger = null)
        {
            _versionCalculator = versionCalculator;
            _gitService = gitService;
            _logger = logger ?? ((level, message) => { });
            _cache = null; // Cache is optional
        }

        public VersionCache Cache
        {
            get => _cache;
            set => _cache = value;
        }

        public List<ProjectInfo> AnalyzeProjects(string repositoryPath, string projectDirectory = null)
        {
            var projects = new List<ProjectInfo>();
            
            // Default to scanning the entire repository if no project directory specified
            var searchPath = string.IsNullOrEmpty(projectDirectory) 
                ? repositoryPath 
                : Path.Combine(repositoryPath, projectDirectory);

            if (!Directory.Exists(searchPath))
            {
                _logger("Warning", $"Project directory does not exist: {searchPath}");
                return projects;
            }

            // Find all project files
            var projectFiles = Directory.GetFiles(searchPath, "*.csproj", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(searchPath, "*.vbproj", SearchOption.AllDirectories))
                .Concat(Directory.GetFiles(searchPath, "*.fsproj", SearchOption.AllDirectories))
                .ToList();

            _logger("Info", $"Found {projectFiles.Count} project files");

            foreach (var projectFile in projectFiles)
            {
                try
                {
                    var projectInfo = AnalyzeProject(projectFile, repositoryPath);
                    if (projectInfo != null)
                    {
                        projects.Add(projectInfo);
                    }
                }
                catch (Exception ex)
                {
                    _logger("Error", $"Failed to analyze project {projectFile}: {ex.Message}");
                }
            }

            // Build dependency graph
            BuildDependencyGraph(projects);

            return projects;
        }

        public ProjectInfo AnalyzeProject(string projectPath, string repositoryPath)
        {
            return AnalyzeProject(projectPath, repositoryPath, null);
        }

        public ProjectInfo AnalyzeProject(string projectPath, string repositoryPath, VersionOptions additionalOptions)
        {
            return AnalyzeProjectWith(projectPath, repositoryPath, (path, projectType, transitiveDependencies) =>
            {
                var versionOptions = new VersionOptions
                {
                    RepoRoot = repositoryPath,
                    ProjectPath = path,
                    ProjectName = Path.GetFileNameWithoutExtension(path),
                    Dependencies = transitiveDependencies,
                    IsTestProject = projectType.IsTestProject,
                    IsPackable = projectType.IsPackable,
                    BaseVersion = additionalOptions?.BaseVersion
                };

                // Apply additional options if provided
                if (additionalOptions != null)
                {
                    versionOptions.PrereleaseType = additionalOptions.PrereleaseType;
                    versionOptions.Debug = additionalOptions.Debug;
                    versionOptions.TagPrefix = additionalOptions.TagPrefix;
                    versionOptions.ForceVersion = additionalOptions.ForceVersion;

                    // Override dependencies if provided in additional options
                    if (additionalOptions.Dependencies != null && additionalOptions.Dependencies.Count > 0)
                    {
                        versionOptions.Dependencies = additionalOptions.Dependencies;
                    }
                }

                return _versionCalculator.CalculateVersion(versionOptions);
            });
        }

        /// <summary>
        /// Analyzes a project (type, direct and transitive dependencies) and obtains its version from
        /// <paramref name="calculateVersion"/>, which receives the project path, its type and its transitive
        /// dependencies (full paths). Lets callers compute versions exactly like the MSBuild task does
        /// (through <see cref="VersioningService"/>).
        /// </summary>
        public ProjectInfo AnalyzeProjectWith(string projectPath, string repositoryPath, Func<string, ProjectTypeInfo, List<string>, VersionResult> calculateVersion)
        {
            if (!File.Exists(projectPath))
            {
                _logger("Error", $"Project file does not exist: {projectPath}");
                return null;
            }

            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            var projectType = DetectProjectType(projectPath, repositoryPath);

            // Get dependencies (including transitive)
            var dependencies = GetProjectDependencies(projectPath, repositoryPath);
            var transitiveDependencies = GetTransitiveDependencies(projectPath, repositoryPath, dependencies);

            var versionResult = calculateVersion(projectPath, projectType, transitiveDependencies);

            return new ProjectInfo
            {
                Name = projectName,
                Path = PathUtils.GetRelativePath(repositoryPath, projectPath),
                FullPath = projectPath,
                Version = versionResult,
                DirectDependencies = dependencies.Select(d => Path.GetFileNameWithoutExtension(d)).ToList(),
                IsTestProject = projectType.IsTestProject,
                IsPackable = projectType.IsPackable
            };
        }

        /// <summary>
        /// Direct and transitive ProjectReferences of a project (full paths), resolved by parsing project files.
        /// </summary>
        public List<string> GetTransitiveDependencies(string projectPath, string repositoryPath)
        {
            return GetTransitiveDependencies(projectPath, repositoryPath, GetProjectDependencies(projectPath, repositoryPath));
        }

        /// <summary>
        /// All project files (*.csproj, *.fsproj, *.vbproj) under <paramref name="repositoryPath"/>.
        /// </summary>
        public List<string> FindAllProjects(string repositoryPath)
        {
            return DiscoverAllProjects(repositoryPath);
        }

        public List<string> GetProjectDependencies(string projectPath, string repositoryPath)
        {
            // Check cache first
            if (_cache != null)
            {
                var cachedDeps = _cache.GetProjectDependencies(projectPath);
                if (cachedDeps != null)
                {
                    return cachedDeps;
                }
            }

            var dependencies = new List<string>();

            try
            {
                var projectContent = File.ReadAllText(projectPath);
                var projectDir = Path.GetDirectoryName(projectPath);

                if (string.IsNullOrEmpty(projectDir))
                {
                    _logger("Error", $"Cannot determine directory for project: {projectPath}");
                    return dependencies;
                }

                // Find ProjectReference elements
                var matches = Regex.Matches(projectContent, PATTERN_PROJECT_REFERENCE, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                
                foreach (Match match in matches)
                {
                    var dependencyPath = match.Groups[1].Value;
                    
                    // Normalize path separators for cross-platform compatibility
                    dependencyPath = dependencyPath.Replace('\\', Path.DirectorySeparatorChar);
                    
                    // Resolve relative path to absolute path
                    if (!Path.IsPathRooted(dependencyPath))
                    {
                        dependencyPath = Path.GetFullPath(Path.Combine(projectDir, dependencyPath));
                    }
                    else
                    {
                        // Even for rooted paths, ensure they're fully resolved
                        dependencyPath = Path.GetFullPath(dependencyPath);
                    }
                    
                    if (File.Exists(dependencyPath))
                    {
                        dependencies.Add(dependencyPath);
                    }
                    else
                    {
                        _logger("Warning", $"Referenced project not found: {dependencyPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger("Error", $"Failed to get dependencies for {projectPath}: {ex.Message}");
            }

            // Cache the result
            if (_cache != null)
            {
                _cache.SetProjectDependencies(projectPath, dependencies);
            }

            return dependencies;
        }

        private List<string> GetTransitiveDependencies(string projectPath, string repositoryPath, List<string> directDependencies)
        {
            try
            {
                // For performance, we'll do a lightweight discovery of all projects in the repo
                var allProjects = DiscoverAllProjects(repositoryPath);
                // Build a lookup of project name -> project info
                var projectLookup = new Dictionary<string, (string Path, List<string> Dependencies)>(StringComparer.OrdinalIgnoreCase);
                
                foreach (var proj in allProjects)
                {
                    var projName = Path.GetFileNameWithoutExtension(proj);
                    var projDeps = GetProjectDependencies(proj, repositoryPath);
                    projectLookup[projName] = (proj, projDeps);
                }
                
                // Build transitive dependencies for our target project
                var targetProjectName = Path.GetFileNameWithoutExtension(projectPath);
                var transitiveDeps = new List<string>();
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                
                BuildTransitiveDependencyList(targetProjectName, projectLookup, transitiveDeps, visited);
                
                return transitiveDeps;
            }
            catch (Exception ex)
            {
                _logger("Warning", $"Failed to calculate transitive dependencies for {projectPath}: {ex.Message}");
                // Fallback to direct dependencies only
                return directDependencies;
            }
        }

        private void BuildTransitiveDependencyList(string projectName, Dictionary<string, (string Path, List<string> Dependencies)> projectLookup, List<string> result, HashSet<string> visited)
        {
            if (visited.Contains(projectName))
                return; // Avoid circular dependencies

            visited.Add(projectName);

            if (projectLookup.TryGetValue(projectName, out var projectInfo))
            {
                foreach (var depPath in projectInfo.Dependencies)
                {
                    if (!result.Contains(depPath))
                    {
                        result.Add(depPath);
                    }
                    
                    // Recursively add transitive dependencies
                    var depName = Path.GetFileNameWithoutExtension(depPath);
                    BuildTransitiveDependencyList(depName, projectLookup, result, visited);
                }
            }
        }

        private List<string> DiscoverAllProjects(string repositoryPath)
        {
            // Check cache first
            if (_cache != null)
            {
                var cachedProjects = _cache.GetAllProjects();
                if (cachedProjects != null)
                {
                    return cachedProjects;
                }
            }

            var projects = new List<string>();
            var projectExtensions = new[] { "*.csproj", "*.fsproj", "*.vbproj" };

            foreach (var extension in projectExtensions)
            {
                var foundProjects = Directory.GetFiles(repositoryPath, extension, SearchOption.AllDirectories);
                projects.AddRange(foundProjects);
            }

            // Cache the result
            if (_cache != null)
            {
                _cache.SetAllProjects(projects);
            }

            return projects;
        }

        public void BuildDependencyGraph(List<ProjectInfo> projects)
        {
            var projectLookup = projects.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var project in projects)
            {
                project.AllDependencies = BuildTransitiveDependencies(project.Name, projectLookup, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            }
        }

        private List<string> BuildTransitiveDependencies(string projectName, Dictionary<string, ProjectInfo> projectLookup, HashSet<string> visited)
        {
            if (visited.Contains(projectName))
            {
                return new List<string>(); // Avoid circular dependencies
            }

            visited.Add(projectName);
            var result = new List<string>();

            if (projectLookup.TryGetValue(projectName, out var project))
            {
                foreach (var dependency in project.DirectDependencies)
                {
                    if (!result.Contains(dependency, StringComparer.OrdinalIgnoreCase))
                    {
                        result.Add(dependency);
                    }

                    // Add transitive dependencies
                    var transitiveDeps = BuildTransitiveDependencies(dependency, projectLookup, new HashSet<string>(visited, StringComparer.OrdinalIgnoreCase));
                    foreach (var transitiveDep in transitiveDeps)
                    {
                        if (!result.Contains(transitiveDep, StringComparer.OrdinalIgnoreCase))
                        {
                            result.Add(transitiveDep);
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Whether a project file (content only, without Directory.Build.props) is a test project:
        /// &lt;IsTestProject&gt;true&lt;/IsTestProject&gt;, or a reference to Microsoft.NET.Test.Sdk (whose build props set
        /// IsTestProject=true) not overridden by &lt;IsTestProject&gt;false&lt;/IsTestProject&gt;.
        /// </summary>
        public static bool IsTestProject(string projectContent)
        {
            return Evaluate(Enumerable.Empty<ProjectFileFacts>(), ReadFacts(projectContent)).IsTestProject;
        }

        /// <summary>
        /// Whether a project file (content only, without Directory.Build.props) is packable. As in the .NET SDK,
        /// every project (console apps included) is packable unless &lt;IsPackable&gt;false&lt;/IsPackable&gt;; test projects
        /// default to not packable (NuGet's pack targets).
        /// </summary>
        public static bool IsPackable(string projectContent)
        {
            return Evaluate(Enumerable.Empty<ProjectFileFacts>(), ReadFacts(projectContent)).IsPackable;
        }

        /// <summary>
        /// Determines IsTestProject / IsPackable the way MSBuild evaluates them, approximated without MSBuild:
        /// unconditional properties from the Directory.Build.props files that apply to the project (the nearest one,
        /// plus the ones it imports further up, never above <paramref name="repositoryRoot"/>), then the
        /// Microsoft.NET.Test.Sdk package props, then the project file itself; last value wins. Conditional
        /// properties and items are ignored because they cannot be evaluated here.
        /// </summary>
        public static ProjectTypeInfo DetectProjectType(string projectPath, string repositoryRoot = null)
        {
            var propsFacts = new List<ProjectFileFacts>();
            foreach (var propsFile in GetDirectoryBuildPropsChain(projectPath, repositoryRoot))
            {
                propsFacts.Add(ReadFacts(SafeReadAllText(propsFile)));
            }

            return Evaluate(propsFacts, ReadFacts(SafeReadAllText(projectPath)));
        }

        /// <summary>
        /// Directory.Build.props files that MSBuild imports for a project, outermost first: the nearest one walking
        /// up from the project directory, and its ancestors as long as each one imports the next one up.
        /// The search stops at <paramref name="repositoryRoot"/> when given.
        /// </summary>
        internal static List<string> GetDirectoryBuildPropsChain(string projectPath, string repositoryRoot)
        {
            var chain = new List<string>();
            var directory = Path.GetDirectoryName(Path.GetFullPath(projectPath));
            var root = string.IsNullOrEmpty(repositoryRoot) ? null : TrimDirectory(Path.GetFullPath(repositoryRoot));

            while (!string.IsNullOrEmpty(directory))
            {
                var candidate = Path.Combine(directory, DIRECTORY_BUILD_PROPS);
                if (File.Exists(candidate))
                {
                    chain.Add(candidate);
                    if (!ImportsParentDirectoryBuildProps(SafeReadAllText(candidate)))
                    {
                        break;
                    }
                }

                if (root != null && string.Equals(TrimDirectory(directory), root, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                directory = Path.GetDirectoryName(directory);
            }

            chain.Reverse();
            return chain;
        }

        private sealed class ProjectFileFacts
        {
            public bool? IsTestProject { get; set; }
            public bool? IsPackable { get; set; }
            public bool ReferencesTestSdk { get; set; }
        }

        private static ProjectTypeInfo Evaluate(IEnumerable<ProjectFileFacts> directoryBuildProps, ProjectFileFacts project)
        {
            bool? isTestProject = null;
            bool? isPackable = null;
            var referencesTestSdk = project.ReferencesTestSdk;

            foreach (var props in directoryBuildProps)
            {
                isTestProject = props.IsTestProject ?? isTestProject;
                isPackable = props.IsPackable ?? isPackable;
                referencesTestSdk |= props.ReferencesTestSdk;
            }

            // NuGet package props are imported after Directory.Build.props and before the project body
            if (referencesTestSdk)
            {
                isTestProject = true;
            }

            isTestProject = project.IsTestProject ?? isTestProject;
            isPackable = project.IsPackable ?? isPackable;

            var test = isTestProject ?? false;
            return new ProjectTypeInfo
            {
                IsTestProject = test,
                // NuGet.Build.Tasks.Pack.targets: IsPackable defaults to false for test projects, true otherwise
                IsPackable = isPackable ?? !test
            };
        }

        private static ProjectFileFacts ReadFacts(string content)
        {
            var facts = new ProjectFileFacts();
            if (string.IsNullOrWhiteSpace(content))
            {
                return facts;
            }

            XElement root;
            try
            {
                root = XDocument.Parse(content).Root;
            }
            catch (XmlException)
            {
                return ReadFactsWithRegex(content);
            }

            if (root == null)
            {
                return facts;
            }

            foreach (var group in root.Elements().Where(e => !HasCondition(e)))
            {
                if (NameIs(group, "PropertyGroup"))
                {
                    foreach (var property in group.Elements().Where(e => !HasCondition(e)))
                    {
                        if (NameIs(property, "IsTestProject"))
                        {
                            facts.IsTestProject = ParseBool(property.Value) ?? facts.IsTestProject;
                        }
                        else if (NameIs(property, "IsPackable"))
                        {
                            facts.IsPackable = ParseBool(property.Value) ?? facts.IsPackable;
                        }
                    }
                }
                else if (NameIs(group, "ItemGroup"))
                {
                    foreach (var item in group.Elements().Where(e => !HasCondition(e) && NameIs(e, "PackageReference")))
                    {
                        var include = (string)item.Attribute("Include") ?? string.Empty;
                        if (include.Split(';').Any(p => string.Equals(p.Trim(), TEST_SDK_PACKAGE, StringComparison.OrdinalIgnoreCase)))
                        {
                            facts.ReferencesTestSdk = true;
                        }
                    }
                }
            }

            return facts;
        }

        private static ProjectFileFacts ReadFactsWithRegex(string content)
        {
            const RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
            var facts = new ProjectFileFacts
            {
                ReferencesTestSdk = Regex.IsMatch(content, PATTERN_TEST_SDK_REFERENCE, options)
            };

            var isTestProject = Regex.Matches(content, PATTERN_IS_TEST_PROJECT, options).Cast<Match>().LastOrDefault();
            if (isTestProject != null)
            {
                facts.IsTestProject = ParseBool(isTestProject.Groups[1].Value);
            }

            var isPackable = Regex.Matches(content, PATTERN_IS_PACKABLE, options).Cast<Match>().LastOrDefault();
            if (isPackable != null)
            {
                facts.IsPackable = ParseBool(isPackable.Groups[1].Value);
            }

            return facts;
        }

        private static bool ImportsParentDirectoryBuildProps(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

            try
            {
                var root = XDocument.Parse(content).Root;
                return root != null && root.Descendants()
                    .Where(e => NameIs(e, "Import"))
                    .Any(e => ((string)e.Attribute("Project") ?? string.Empty).IndexOf(DIRECTORY_BUILD_PROPS, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch (XmlException)
            {
                return false;
            }
        }

        private static bool NameIs(XElement element, string name) =>
            string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase);

        private static bool HasCondition(XElement element) =>
            element.Attributes().Any(a => string.Equals(a.Name.LocalName, "Condition", StringComparison.OrdinalIgnoreCase) &&
                                          !string.IsNullOrWhiteSpace(a.Value));

        private static bool? ParseBool(string value)
        {
            var trimmed = value?.Trim();
            if (string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase))
                return false;
            return null; // e.g. $(SomeProperty): cannot be evaluated here
        }

        private static string TrimDirectory(string path) =>
            path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private static string SafeReadAllText(string path)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}