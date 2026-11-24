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

        /// <summary>
        /// The version the project sets itself (Version, or VersionPrefix-VersionSuffix), or null for the SDK default
        /// (1.0.0). It is what a project that is not versioned (test / non-packable) is built with.
        /// </summary>
        public string ProjectVersion { get; set; }

        /// <summary>MonoRepoSkipTestProjects as the project sets it (null when not set).</summary>
        public bool? SkipTestProjects { get; set; }

        /// <summary>MonoRepoSkipNonPackableProjects as the project sets it (null when not set).</summary>
        public bool? SkipNonPackableProjects { get; set; }
    }

    public class ProjectAnalyzer : IProjectAnalyzer
    {
        // Regex patterns for project file parsing
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
            var projectFiles = string.IsNullOrEmpty(projectDirectory)
                ? DiscoverAllProjects(repositoryPath)
                : ProjectDiscovery.FindProjectFiles(searchPath, _logger);

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
            var projectType = DetectProject(projectPath, repositoryPath);

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
                DirectDependencyPaths = dependencies.ToList(),
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

        /// <summary>
        /// Direct ProjectReferences of a project (full paths of existing project files), read the way MSBuild
        /// evaluates them: properties ($(MSBuildThisFileDirectory), properties from Directory.Build.props, ...),
        /// ';'-separated lists, imports and references added by Directory.Build.props/targets
        /// (see <see cref="ProjectReferenceReader"/>).
        /// </summary>
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
                dependencies = ProjectReferenceReader.GetDirectReferences(projectPath, _logger);
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

        /// <summary>
        /// Direct and transitive project references (full paths), following every ProjectReference edge from the
        /// project's direct references. Projects are identified by path, so projects with the same file name in
        /// different directories are told apart.
        /// </summary>
        private List<string> GetTransitiveDependencies(string projectPath, string repositoryPath, List<string> directDependencies)
        {
            try
            {
                var self = Path.GetFullPath(projectPath);
                var result = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { self };
                var queue = new Queue<string>();
                foreach (var dependency in directDependencies ?? new List<string>())
                {
                    var full = Path.GetFullPath(dependency);
                    if (seen.Add(full))
                    {
                        result.Add(full);
                        queue.Enqueue(full);
                    }
                }

                while (queue.Count > 0)
                {
                    foreach (var dependency in GetProjectDependencies(queue.Dequeue(), repositoryPath))
                    {
                        if (seen.Add(dependency))
                        {
                            result.Add(dependency);
                            queue.Enqueue(dependency);
                        }
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger("Warning", $"Failed to calculate transitive dependencies for {projectPath}: {ex.Message}");
                // Fallback to direct dependencies only
                return directDependencies;
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

            var projects = ProjectDiscovery.FindProjectFiles(repositoryPath, _logger);

            // Cache the result
            if (_cache != null)
            {
                _cache.SetAllProjects(projects);
            }

            return projects;
        }

        /// <summary>
        /// Fills <see cref="ProjectInfo.AllDependencies"/> (names of direct and transitive dependencies among
        /// <paramref name="projects"/>). Projects are matched by path, so duplicate project names are allowed.
        /// </summary>
        public void BuildDependencyGraph(List<ProjectInfo> projects)
        {
            var graph = new ProjectGraph(projects);
            foreach (var project in graph.Projects)
            {
                var all = new List<string>();
                // Keep direct dependencies first, including ones outside the analyzed set
                foreach (var name in project.DirectDependencies ?? new List<string>())
                {
                    if (!all.Contains(name, StringComparer.OrdinalIgnoreCase))
                        all.Add(name);
                }
                foreach (var dependency in graph.AllDependencies(project))
                {
                    if (!all.Contains(dependency.Name, StringComparer.OrdinalIgnoreCase))
                        all.Add(dependency.Name);
                }
                project.AllDependencies = all;
            }
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
        /// The project's type and versioning settings as MSBuild evaluates them: <see cref="DetectProjectType"/>, refined
        /// with the full property evaluation of <see cref="ProjectReferenceReader"/> (conditions, $(...) values,
        /// Directory.Build.targets): IsPackable, IsTestProject, the project's own Version, and the
        /// MonoRepoSkipTestProjects / MonoRepoSkipNonPackableProjects settings the MSBuild task would receive.
        /// </summary>
        public static ProjectTypeInfo DetectProject(string projectPath, string repositoryRoot = null)
        {
            var type = DetectProjectType(projectPath, repositoryRoot);
            IReadOnlyDictionary<string, string> properties = null;
            try
            {
                properties = ProjectReferenceReader.Evaluate(projectPath)?.Properties;
            }
            catch (Exception)
            {
                // Keep the static detection
            }
            if (properties == null)
            {
                return type;
            }

            string Get(string name) => properties.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

            type.IsTestProject = ParseBool(Get("IsTestProject")) ?? type.IsTestProject;
            type.IsPackable = ParseBool(Get("IsPackable")) ?? type.IsPackable;
            type.SkipTestProjects = ParseBool(Get("MonoRepoSkipTestProjects"));
            type.SkipNonPackableProjects = ParseBool(Get("MonoRepoSkipNonPackableProjects"));

            var version = Get("Version");
            var prefix = Get("VersionPrefix");
            var suffix = Get("VersionSuffix");
            if (version != null)
            {
                type.ProjectVersion = version;
            }
            else if (prefix != null || suffix != null)
            {
                type.ProjectVersion = (prefix ?? "1.0.0") + (suffix != null ? "-" + suffix : string.Empty);
            }

            return type;
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
            public string Version { get; set; }
            public string VersionPrefix { get; set; }
            public string VersionSuffix { get; set; }

            /// <summary>The project uses an SDK whose props default IsPackable to false (Web, Worker, Blazor WebAssembly).</summary>
            public bool NonPackableSdk { get; set; }

            /// <summary>The project uses a test SDK (MSTest.Sdk), which makes it a test project.</summary>
            public bool TestSdk { get; set; }
        }

        /// <summary>
        /// SDKs whose props set IsPackable=false when the project does not set it: web apps and worker services are
        /// deployed, not packed.
        /// </summary>
        private static readonly HashSet<string> NonPackableSdks = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft.NET.Sdk.Web",
            "Microsoft.NET.Sdk.Web.ProjectSystem",
            "Microsoft.NET.Sdk.Worker",
            "Microsoft.NET.Sdk.BlazorWebAssembly"
        };

        private static ProjectTypeInfo Evaluate(IEnumerable<ProjectFileFacts> directoryBuildProps, ProjectFileFacts project)
        {
            bool? isTestProject = null;
            bool? isPackable = null;
            string version = null, versionPrefix = null, versionSuffix = null;
            var referencesTestSdk = project.ReferencesTestSdk;

            foreach (var props in directoryBuildProps)
            {
                isTestProject = props.IsTestProject ?? isTestProject;
                isPackable = props.IsPackable ?? isPackable;
                referencesTestSdk |= props.ReferencesTestSdk;
                version = props.Version ?? version;
                versionPrefix = props.VersionPrefix ?? versionPrefix;
                versionSuffix = props.VersionSuffix ?? versionSuffix;
            }

            version = project.Version ?? version;
            versionPrefix = project.VersionPrefix ?? versionPrefix;
            versionSuffix = project.VersionSuffix ?? versionSuffix;

            // The SDK's props (imported before Directory.Build.props is read for IsPackable, with a "not set"
            // condition) make web/worker projects non-packable unless something sets IsPackable
            if (project.NonPackableSdk && isPackable == null && project.IsPackable == null)
            {
                isPackable = false;
            }

            // NuGet package props are imported after Directory.Build.props and before the project body; MSTest.Sdk
            // brings Microsoft.NET.Test.Sdk itself
            if (referencesTestSdk || project.TestSdk)
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
                IsPackable = isPackable ?? !test,
                // Microsoft.NET.Sdk: Version = VersionPrefix[-VersionSuffix] unless Version is set
                ProjectVersion = !string.IsNullOrWhiteSpace(version)
                    ? version.Trim()
                    : !string.IsNullOrWhiteSpace(versionPrefix) || !string.IsNullOrWhiteSpace(versionSuffix)
                        ? (string.IsNullOrWhiteSpace(versionPrefix) ? "1.0.0" : versionPrefix.Trim()) +
                          (string.IsNullOrWhiteSpace(versionSuffix) ? "" : "-" + versionSuffix.Trim())
                        : null
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

            var sdks = new List<string>();
            var sdkAttribute = (string)root.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, "Sdk", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(sdkAttribute))
            {
                sdks.AddRange(sdkAttribute.Split(';').Select(sdk => sdk.Split('/')[0].Trim()));
            }
            foreach (var element in root.Elements())
            {
                if (NameIs(element, "Sdk"))
                {
                    sdks.Add(((string)element.Attribute("Name") ?? string.Empty).Trim());
                }
                else if (NameIs(element, "Import") && element.Attribute("Sdk") != null)
                {
                    sdks.Add(((string)element.Attribute("Sdk")).Split('/')[0].Trim());
                }
            }
            facts.NonPackableSdk = sdks.Any(sdk => NonPackableSdks.Contains(sdk));
            facts.TestSdk = sdks.Any(sdk => string.Equals(sdk, "MSTest.Sdk", StringComparison.OrdinalIgnoreCase));

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
                        else if (NameIs(property, "Version"))
                        {
                            facts.Version = LiteralOrNull(property.Value) ?? facts.Version;
                        }
                        else if (NameIs(property, "VersionPrefix"))
                        {
                            facts.VersionPrefix = LiteralOrNull(property.Value) ?? facts.VersionPrefix;
                        }
                        else if (NameIs(property, "VersionSuffix"))
                        {
                            facts.VersionSuffix = LiteralOrNull(property.Value) ?? facts.VersionSuffix;
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

        /// <summary>A literal property value, or null when it is empty or uses $(...) (cannot be evaluated here).</summary>
        private static string LiteralOrNull(string value)
        {
            var trimmed = value?.Trim();
            return string.IsNullOrEmpty(trimmed) || trimmed.Contains("$(") ? null : trimmed;
        }

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