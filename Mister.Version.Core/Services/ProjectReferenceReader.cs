using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Mister.Version.Core.Services
{
    /// <summary>
    /// Reads the ProjectReference items of a project file without MSBuild, approximating MSBuild evaluation closely
    /// enough for dependency tracking:
    /// <list type="bullet">
    /// <item>two passes like MSBuild: first all properties and imports (Directory.Build.props, the project file,
    /// Directory.Build.targets and what they import, in that order), then all items against the final property
    /// values;</item>
    /// <item>environment variables, the reserved MSBuildProject* / MSBuildThisFile* properties, properties the
    /// project's SDK defines (UsingMicrosoftNETSdkWeb, ...), string methods ($(Name.EndsWith('.Tests'))) and common
    /// property functions ([MSBuild]::NormalizePath, GetDirectoryNameOfFileAbove, GetPathOfFileAbove,
    /// VersionGreaterThan, [System.IO.Path]::Combine, [System.String]::IsNullOrEmpty, ...);</item>
    /// <item>Include lists separated by ';', wildcards, Remove/Exclude/Update, Choose/When, single- or double-quoted
    /// attributes, and conditions (==, !=, &lt;, &gt;, and, or, !, Exists(), HasTrailingSlash()).</item>
    /// </list>
    /// Conditions that depend on values only known during a build (target framework, configuration, runtime
    /// identifier, ...) or that cannot be evaluated are "unknown": such references are included but marked as not
    /// certain (<see cref="ReferenceItem.Certain"/>), so callers decide whether to over-include them.
    /// </summary>
    public static class ProjectReferenceReader
    {
        private const int MaxImportDepth = 32;
        private const int MaxStampedDirectories = 256;

        /// <summary>Properties MSBuild only knows during a build: when undefined, conditions on them are unknown.</summary>
        private static readonly HashSet<string> BuildTimeProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TargetFramework", "TargetFrameworkIdentifier", "TargetFrameworkVersion", "TargetFrameworkMoniker",
            "TargetFrameworkProfile", "TargetPlatformIdentifier", "TargetPlatformVersion", "TargetPlatformMoniker",
            "Configuration", "Platform", "PlatformTarget", "RuntimeIdentifier", "RuntimeIdentifiers",
            "BuildingInsideVisualStudio", "BuildingByReSharper", "DesignTimeBuild", "MSBuildRuntimeType",
            "MSBuildVersion", "VisualStudioVersion", "SolutionDir", "SolutionName", "SolutionPath", "SolutionFileName",
            "NETCoreSdkVersion", "NETCoreSdkRuntimeIdentifier", "BundledNETCoreAppPackageVersion", "_IsPublishing",
            "ContinuousIntegrationBuild", "TF_BUILD", "GITHUB_ACTIONS", "IsCrossTargetingBuild"
        };

        private static readonly ConcurrentDictionary<string, CachedEvaluation> Cache =
            new ConcurrentDictionary<string, CachedEvaluation>(StringComparer.OrdinalIgnoreCase);

        private static readonly ConcurrentDictionary<string, Tuple<DateTime, long, XDocument>> DocumentCache =
            new ConcurrentDictionary<string, Tuple<DateTime, long, XDocument>>(StringComparer.OrdinalIgnoreCase);

        private static readonly ConcurrentDictionary<string, Tuple<DateTime, long, List<string>>> AssetsClosureCache =
            new ConcurrentDictionary<string, Tuple<DateTime, long, List<string>>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>A ProjectReference item of an evaluated project.</summary>
        public sealed class ReferenceItem
        {
            /// <summary>Full path of the referenced project file (it exists).</summary>
            public string Path { get; internal set; }

            /// <summary>
            /// Every condition on the way to this item (Choose branch, ItemGroup, item) evaluated to true. False when
            /// one of them depends on build-time values (e.g. the target framework) or could not be evaluated.
            /// </summary>
            public bool Certain { get; internal set; }

            /// <summary>ReferenceOutputAssembly="false" (source generators, analyzers, build-order references).</summary>
            public bool NoOutputAssembly { get; internal set; }

            /// <summary>PrivateAssets="all": the reference does not flow to consumers of this project.</summary>
            public bool PrivateAssetsAll { get; internal set; }
        }

        /// <summary>The result of reading a project file.</summary>
        public sealed class ProjectEvaluation
        {
            internal ProjectEvaluation(string projectPath)
            {
                ProjectPath = projectPath;
            }

            /// <summary>Full path of the project file.</summary>
            public string ProjectPath { get; }

            /// <summary>Full paths of the existing project files referenced by ProjectReference items (certain or not).</summary>
            public List<string> References { get; } = new List<string>();

            /// <summary>The ProjectReference items with their certainty and metadata.</summary>
            public List<ReferenceItem> ReferenceItems { get; } = new List<ReferenceItem>();

            /// <summary>ProjectReference items that could not be resolved to an existing file (as written, expanded where possible).</summary>
            public List<string> Unresolved { get; } = new List<string>();

            /// <summary>Evaluated properties (final values of properties known at evaluation time).</summary>
            public IReadOnlyDictionary<string, string> Properties { get; internal set; }

            /// <summary>The SDKs named by the project (Sdk attribute, &lt;Sdk&gt; elements or Import Sdk=).</summary>
            public List<string> Sdks { get; } = new List<string>();

            /// <summary>Files read during evaluation (project, Directory.Build.props/targets and imports).</summary>
            public List<string> Files { get; } = new List<string>();

            /// <summary>Paths whose existence or contents the result depends on (files read, files looked for, wildcard directories).</summary>
            internal List<string> Dependencies { get; } = new List<string>();
        }

        private sealed class CachedEvaluation
        {
            public List<Tuple<string, DateTime, long>> Stamps;
            public ProjectEvaluation Evaluation;
            /// <summary>The validation scope (e.g. one MSBuild build) in which the stamps were last checked.</summary>
            public object ValidatedScope;
        }

        /// <summary>Clears the in-memory caches (tests).</summary>
        public static void ClearCache()
        {
            Cache.Clear();
            DocumentCache.Clear();
            AssetsClosureCache.Clear();
        }

        /// <summary>Direct project references (full paths of existing files) of <paramref name="projectPath"/>.</summary>
        public static List<string> GetDirectReferences(string projectPath, Action<string, string> logger = null)
        {
            var evaluation = Evaluate(projectPath, logger);
            return evaluation?.References.ToList() ?? new List<string>();
        }

        /// <summary>
        /// Every project reachable through ProjectReference items from <paramref name="roots"/> (the roots
        /// themselves are included), as full paths. Includes references whose conditions are not certain.
        /// Cycles are tolerated.
        /// </summary>
        public static List<string> GetReferenceClosure(IEnumerable<string> roots, Action<string, string> logger = null)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            foreach (var root in roots ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrEmpty(root))
                    continue;
                var full = System.IO.Path.GetFullPath(root);
                if (seen.Add(full))
                {
                    result.Add(full);
                    queue.Enqueue(full);
                }
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!File.Exists(current))
                    continue;
                foreach (var reference in GetDirectReferences(current, logger))
                {
                    if (seen.Add(reference))
                    {
                        result.Add(reference);
                        queue.Enqueue(reference);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// The projects a build depends on that NuGet's restore closure (project.assets.json) leaves out. NuGet does
        /// not record ReferenceOutputAssembly="false" references (source generators, analyzers, build-order
        /// references) and does not flow PrivateAssets="all" references to consumers, so: from each project of the
        /// build closure (<paramref name="buildClosure"/>, which should include the project itself), follow those
        /// references, and below the projects reached that way, every reference. Conditions are not required to be
        /// certain: a version is per project, so a reference that applies to any target framework or configuration
        /// counts (otherwise the outer build and each target framework would get different versions). Ordinary
        /// references of the build closure are left to NuGet's closure, which already holds the union over target
        /// frameworks.
        /// </summary>
        /// <param name="validationScope">
        /// When given, a cached evaluation already checked in this scope is reused without checking its files
        /// again (the MSBuild task passes one object per build).
        /// </param>
        public static List<string> GetUnflowedReferenceClosure(IEnumerable<string> buildClosure, object validationScope = null, Action<string, string> logger = null)
        {
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var outside = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            var queue = new Queue<string>();
            foreach (var project in buildClosure ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrEmpty(project))
                    continue;
                var full = System.IO.Path.GetFullPath(project);
                if (known.Add(full))
                    queue.Enqueue(full);
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var evaluation = Evaluate(current, logger, validationScope);
                if (evaluation == null)
                    continue;

                var belowUnflowedEdge = outside.Contains(current);
                foreach (var item in evaluation.ReferenceItems)
                {
                    if (!(belowUnflowedEdge || item.NoOutputAssembly || item.PrivateAssetsAll))
                        continue;
                    if (known.Add(item.Path))
                    {
                        outside.Add(item.Path);
                        result.Add(item.Path);
                        queue.Enqueue(item.Path);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Evaluates a project file (see the class remarks). Results are cached per project and reused while none of
        /// the files it depends on changed (including files that were looked for and not found). Returns null when the
        /// project file does not exist.
        /// </summary>
        public static ProjectEvaluation Evaluate(string projectPath, Action<string, string> logger = null) => Evaluate(projectPath, logger, null);

        private static ProjectEvaluation Evaluate(string projectPath, Action<string, string> logger, object validationScope)
        {
            if (string.IsNullOrEmpty(projectPath))
                return null;

            var fullPath = System.IO.Path.GetFullPath(projectPath);
            if (!File.Exists(fullPath))
                return null;

            if (Cache.TryGetValue(fullPath, out var cached))
            {
                if (validationScope != null && ReferenceEquals(cached.ValidatedScope, validationScope))
                    return cached.Evaluation;
                if (IsCurrent(cached.Stamps))
                {
                    cached.ValidatedScope = validationScope;
                    return cached.Evaluation;
                }
            }

            ProjectEvaluation evaluation;
            try
            {
                evaluation = new Evaluator(fullPath, logger).Run();
            }
            catch (Exception ex)
            {
                logger?.Invoke("Warning", $"Could not read project references of {fullPath}: {ex.Message}");
                evaluation = new ProjectEvaluation(fullPath) { Properties = new Dictionary<string, string>() };
                evaluation.Files.Add(fullPath);
                evaluation.Dependencies.Add(fullPath);
            }

            if (Cache.Count > 20000)
                Cache.Clear();
            Cache[fullPath] = new CachedEvaluation
            {
                Evaluation = evaluation,
                Stamps = evaluation.Dependencies.Distinct(StringComparer.OrdinalIgnoreCase).Select(Stamp).ToList(),
                ValidatedScope = validationScope
            };
            return evaluation;
        }

        /// <summary>
        /// Every "type": "project" library of a NuGet project.assets.json: the transitive project references NuGet
        /// restore resolved (TFM conditions evaluated). Edges NuGet does not flow (ReferenceOutputAssembly=false,
        /// PrivateAssets=all) are not included; combine with <see cref="GetReferenceClosure"/>.
        /// </summary>
        public static List<string> ReadAssetsClosure(string assetsFile, string projectDirectory)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(assetsFile))
                return result;

            try
            {
                var path = Path.IsPathRooted(assetsFile) ? assetsFile : Path.Combine(projectDirectory, assetsFile);
                var info = new FileInfo(path);
                if (!info.Exists)
                    return result;

                if (AssetsClosureCache.TryGetValue(info.FullName, out var cached) &&
                    cached.Item1 == info.LastWriteTimeUtc && cached.Item2 == info.Length)
                {
                    return cached.Item3;
                }

                using (var stream = File.OpenRead(info.FullName))
                using (var document = System.Text.Json.JsonDocument.Parse(stream))
                {
                    if (document.RootElement.TryGetProperty("libraries", out var libraries) &&
                        libraries.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        foreach (var library in libraries.EnumerateObject())
                        {
                            var value = library.Value;
                            if (value.ValueKind != System.Text.Json.JsonValueKind.Object ||
                                !value.TryGetProperty("type", out var type) || type.GetString() != "project")
                            {
                                continue;
                            }

                            string relative = null;
                            if (value.TryGetProperty("msbuildProject", out var msbuildProject))
                                relative = msbuildProject.GetString();
                            else if (value.TryGetProperty("path", out var libraryPath))
                                relative = libraryPath.GetString();

                            if (!string.IsNullOrEmpty(relative))
                            {
                                result.Add(Path.GetFullPath(Path.Combine(projectDirectory, relative.Replace('\\', Path.DirectorySeparatorChar))));
                            }
                        }
                    }
                }

                AssetsClosureCache[info.FullName] = Tuple.Create(info.LastWriteTimeUtc, info.Length, result);
            }
            catch (Exception)
            {
                // Callers fall back to the other sources of references
            }

            return result;
        }

        /// <summary>Snapshot of a dependency path: last write time and length, or MinValue/-1 when it does not exist.</summary>
        private static Tuple<string, DateTime, long> Stamp(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var info = new FileInfo(path);
                    return Tuple.Create(path, info.LastWriteTimeUtc, info.Length);
                }
                if (Directory.Exists(path))
                    return Tuple.Create(path, Directory.GetLastWriteTimeUtc(path), -2L);
            }
            catch (Exception)
            {
                // Treated as missing
            }
            return Tuple.Create(path, DateTime.MinValue, -1L);
        }

        private static bool IsCurrent(List<Tuple<string, DateTime, long>> stamps)
        {
            foreach (var stamp in stamps)
            {
                var now = Stamp(stamp.Item1);
                if (now.Item2 != stamp.Item2 || now.Item3 != stamp.Item3)
                    return false;
            }
            return true;
        }

        internal static XDocument LoadDocument(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return null;

            if (DocumentCache.TryGetValue(info.FullName, out var cached) &&
                cached.Item1 == info.LastWriteTimeUtc && cached.Item2 == info.Length)
            {
                return cached.Item3;
            }

            XDocument document;
            try
            {
                document = XDocument.Load(info.FullName);
            }
            catch (XmlException)
            {
                document = null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }

            DocumentCache[info.FullName] = Tuple.Create(info.LastWriteTimeUtc, info.Length, document);
            return document;
        }

        /// <summary>A value that may be unknown at evaluation time (depends on build-time properties or unsupported syntax).</summary>
        private struct Expanded
        {
            public string Value;
            public bool Unknown;

            public static Expanded Of(string value) => new Expanded { Value = value ?? string.Empty };
            public static readonly Expanded UnknownValue = new Expanded { Value = string.Empty, Unknown = true };
        }

        /// <summary>An ItemGroup found in pass 1, evaluated in pass 2.</summary>
        private sealed class PendingItemGroup
        {
            public XElement Element;
            public string File;
            public bool Certain;
        }

        private sealed class Evaluator
        {
            private readonly string _projectPath;
            private readonly string _projectDirectory;
            private readonly Action<string, string> _logger;
            private readonly Dictionary<string, string> _properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _unknownProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly List<ReferenceItem> _items = new List<ReferenceItem>();
            private readonly List<PendingItemGroup> _itemGroups = new List<PendingItemGroup>();
            private readonly ProjectEvaluation _result;
            private readonly Stack<string> _files = new Stack<string>();
            private readonly HashSet<string> _importing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private int _stampedDirectories;

            public Evaluator(string projectPath, Action<string, string> logger)
            {
                _projectPath = projectPath;
                _projectDirectory = Path.GetDirectoryName(projectPath);
                _logger = logger;
                _result = new ProjectEvaluation(projectPath);
            }

            public ProjectEvaluation Run()
            {
                SeedProperties();
                _result.Dependencies.Add(_projectPath);

                // Pass 1: properties and imports, in MSBuild's order
                var projectDocument = LoadDocument(_projectPath);
                if (projectDocument?.Root != null)
                {
                    CollectSdks(projectDocument.Root);
                    SeedSdkProperties();
                }

                var propsFile = FindFileAbove(_projectDirectory, "Directory.Build.props");
                if (propsFile != null && !IsFalse("ImportDirectoryBuildProps"))
                {
                    _properties["DirectoryBuildPropsPath"] = propsFile;
                    EvaluateFile(propsFile, 0);
                }

                if (projectDocument?.Root != null)
                {
                    EvaluateFile(_projectPath, 0);
                }
                else
                {
                    _result.Files.Add(_projectPath);
                    EvaluateWithRegexFallback(_projectPath);
                }

                SeedSdkDefaults();

                var targetsFile = FindFileAbove(_projectDirectory, "Directory.Build.targets");
                if (targetsFile != null && !IsFalse("ImportDirectoryBuildTargets"))
                {
                    _properties["DirectoryBuildTargetsPath"] = targetsFile;
                    EvaluateFile(targetsFile, 0);
                }

                // Pass 2: items, against the final property values
                foreach (var group in _itemGroups)
                {
                    _files.Push(group.File);
                    try
                    {
                        EvaluateItems(group.Element, group.Certain);
                    }
                    finally
                    {
                        _files.Pop();
                    }
                }

                foreach (var item in _items.Where(i => !string.Equals(i.Path, _projectPath, StringComparison.OrdinalIgnoreCase)))
                {
                    _result.ReferenceItems.Add(item);
                    _result.References.Add(item.Path);
                }
                _result.Properties = new Dictionary<string, string>(_properties, StringComparer.OrdinalIgnoreCase);
                return _result;
            }

            private bool IsFalse(string name) =>
                _properties.TryGetValue(name, out var value) && string.Equals(value?.Trim(), "false", StringComparison.OrdinalIgnoreCase);

            private void SeedProperties()
            {
                try
                {
                    foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
                    {
                        var name = entry.Key as string;
                        if (!string.IsNullOrEmpty(name) && IsValidPropertyName(name) && !BuildTimeProperties.Contains(name))
                            _properties[name] = entry.Value as string ?? string.Empty;
                    }
                }
                catch (Exception)
                {
                    // Environment not readable: ignore
                }

                var directory = _projectDirectory;
                _properties["MSBuildProjectFullPath"] = _projectPath;
                _properties["MSBuildProjectDirectory"] = directory;
                _properties["MSBuildProjectFile"] = Path.GetFileName(_projectPath);
                _properties["MSBuildProjectName"] = Path.GetFileNameWithoutExtension(_projectPath);
                _properties["MSBuildProjectExtension"] = Path.GetExtension(_projectPath);
                var root = Path.GetPathRoot(directory) ?? string.Empty;
                _properties["MSBuildProjectDirectoryNoRoot"] = directory.Substring(Math.Min(root.Length, directory.Length));
                _properties["OS"] = Path.DirectorySeparatorChar == '\\' ? "Windows_NT" : "Unix";
            }

            /// <summary>Properties the project's SDK props set before Directory.Build.props is imported.</summary>
            private void SeedSdkProperties()
            {
                foreach (var sdk in _result.Sdks)
                {
                    if (sdk.StartsWith("Microsoft.NET.Sdk", StringComparison.OrdinalIgnoreCase))
                        _properties["UsingMicrosoftNETSdk"] = "true";
                    if (sdk.Equals("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase))
                    {
                        _properties["UsingMicrosoftNETSdkWeb"] = "true";
                        _properties["UsingMicrosoftNETSdkRazor"] = "true";
                    }
                    else if (sdk.Equals("Microsoft.NET.Sdk.Razor", StringComparison.OrdinalIgnoreCase))
                        _properties["UsingMicrosoftNETSdkRazor"] = "true";
                    else if (sdk.Equals("Microsoft.NET.Sdk.Worker", StringComparison.OrdinalIgnoreCase))
                        _properties["UsingMicrosoftNETSdkWorker"] = "true";
                    else if (sdk.Equals("Microsoft.NET.Sdk.BlazorWebAssembly", StringComparison.OrdinalIgnoreCase))
                    {
                        _properties["UsingMicrosoftNETSdkBlazorWebAssembly"] = "true";
                        _properties["UsingMicrosoftNETSdkRazor"] = "true";
                    }
                    else if (sdk.Equals("Microsoft.Build.Traversal", StringComparison.OrdinalIgnoreCase))
                        _properties["UsingMicrosoftTraversalSdk"] = "true";
                    else if (sdk.Equals("Microsoft.Build.NoTargets", StringComparison.OrdinalIgnoreCase))
                        _properties["UsingMicrosoftNoTargetsSdk"] = "true";
                }
            }

            /// <summary>Defaults the SDK targets apply when the project did not set them (read by Directory.Build.targets and items).</summary>
            private void SeedSdkDefaults()
            {
                if (!_properties.ContainsKey("OutputType") && !_unknownProperties.Contains("OutputType"))
                {
                    var exe = _result.Sdks.Any(s => s.Equals("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase) ||
                                                    s.Equals("Microsoft.NET.Sdk.Worker", StringComparison.OrdinalIgnoreCase) ||
                                                    s.Equals("Microsoft.NET.Sdk.BlazorWebAssembly", StringComparison.OrdinalIgnoreCase));
                    _properties["OutputType"] = exe ? "Exe" : "Library";
                }

                if ((!_properties.ContainsKey("IsTestProject") && !_unknownProperties.Contains("IsTestProject")) ||
                    (!_properties.ContainsKey("IsPackable") && !_unknownProperties.Contains("IsPackable")))
                {
                    var type = ProjectAnalyzer.DetectProjectType(_projectPath);
                    if (!_properties.ContainsKey("IsTestProject") && !_unknownProperties.Contains("IsTestProject"))
                        _properties["IsTestProject"] = type.IsTestProject ? "true" : "false";
                    if (!_properties.ContainsKey("IsPackable") && !_unknownProperties.Contains("IsPackable"))
                        _properties["IsPackable"] = type.IsPackable ? "true" : "false";
                }
            }

            private static bool IsValidPropertyName(string name)
            {
                if (name.Length == 0 || !(char.IsLetter(name[0]) || name[0] == '_'))
                    return false;
                foreach (var c in name)
                {
                    if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-'))
                        return false;
                }
                return true;
            }

            /// <summary>The nearest file named <paramref name="fileName"/> at or above a directory; every path looked at is a dependency.</summary>
            private string FindFileAbove(string directory, string fileName)
            {
                for (var current = directory; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                {
                    var candidate = Path.Combine(current, fileName);
                    _result.Dependencies.Add(candidate);
                    if (File.Exists(candidate))
                        return candidate;
                }
                return null;
            }

            private void EvaluateFile(string path, int depth)
            {
                var full = Path.GetFullPath(path);
                if (depth > MaxImportDepth || !_importing.Add(full))
                    return;

                try
                {
                    if (!_result.Files.Contains(full, StringComparer.OrdinalIgnoreCase))
                        _result.Files.Add(full);
                    _result.Dependencies.Add(full);

                    var document = LoadDocument(full);
                    if (document?.Root == null)
                        return;

                    _files.Push(full);
                    try
                    {
                        EvaluateChildren(document.Root, depth, certain: true);
                    }
                    finally
                    {
                        _files.Pop();
                    }
                }
                finally
                {
                    _importing.Remove(full);
                }
            }

            /// <summary>Malformed XML: fall back to the ProjectReference Include attributes found by a regex.</summary>
            private void EvaluateWithRegexFallback(string path)
            {
                string content;
                try
                {
                    content = File.ReadAllText(path);
                }
                catch (Exception)
                {
                    return;
                }

                _files.Push(path);
                try
                {
                    foreach (Match match in Regex.Matches(content, @"<ProjectReference\s[^>]*?Include\s*=\s*(?:""([^""]*)""|'([^']*)')",
                                 RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    {
                        var include = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                        foreach (var resolved in ResolveItemSpecs(include))
                            AddItem(resolved, certain: false, noOutputAssembly: false, privateAssetsAll: false);
                    }
                }
                finally
                {
                    _files.Pop();
                }
            }

            private void CollectSdks(XElement root)
            {
                var sdkAttribute = Attribute(root, "Sdk");
                if (!string.IsNullOrWhiteSpace(sdkAttribute))
                    _result.Sdks.AddRange(sdkAttribute.Split(';').Select(s => s.Split('/')[0].Trim()).Where(s => s.Length > 0));

                foreach (var element in root.Elements())
                {
                    string sdk = null;
                    if (NameIs(element, "Sdk"))
                        sdk = Attribute(element, "Name");
                    else if (NameIs(element, "Import"))
                        sdk = Attribute(element, "Sdk");
                    if (!string.IsNullOrWhiteSpace(sdk))
                    {
                        sdk = sdk.Split('/')[0].Trim();
                        if (!_result.Sdks.Contains(sdk, StringComparer.OrdinalIgnoreCase))
                            _result.Sdks.Add(sdk);
                    }
                }
            }

            // ------------------------------------------------------------------ pass 1

            private void EvaluateChildren(XElement parent, int depth, bool certain)
            {
                foreach (var element in parent.Elements())
                {
                    if (NameIs(element, "PropertyGroup"))
                    {
                        var condition = EvaluateCondition(element, CurrentDirectory);
                        if (condition == true && certain)
                            EvaluateProperties(element);
                        else if (condition != false)
                            MarkUnknown(element); // the group may or may not apply
                    }
                    else if (NameIs(element, "ItemGroup"))
                    {
                        _itemGroups.Add(new PendingItemGroup { Element = element, File = CurrentFile, Certain = certain });
                    }
                    else if (NameIs(element, "Import"))
                    {
                        EvaluateImport(element, depth);
                    }
                    else if (NameIs(element, "ImportGroup"))
                    {
                        if (EvaluateCondition(element, CurrentDirectory) != false)
                        {
                            foreach (var import in element.Elements().Where(e => NameIs(e, "Import")))
                                EvaluateImport(import, depth);
                        }
                    }
                    else if (NameIs(element, "Choose"))
                    {
                        EvaluateChoose(element, depth, certain);
                    }
                    // Target, UsingTask, ProjectExtensions, Sdk: nothing to evaluate
                }
            }

            private void EvaluateChoose(XElement choose, int depth, bool certain)
            {
                var anyUnknown = false;
                foreach (var branch in choose.Elements())
                {
                    if (NameIs(branch, "When"))
                    {
                        var condition = EvaluateCondition(branch, _projectDirectory);
                        if (condition == false)
                            continue;

                        if (condition == true && !anyUnknown)
                        {
                            EvaluateChildren(branch, depth, certain);
                            return;
                        }

                        // The branch might be the one taken (its condition is unknown, or an earlier unknown branch
                        // might win): its properties become unknown, its items are uncertain
                        anyUnknown = true;
                        EvaluateChildren(branch, depth, certain: false);
                        if (condition == true)
                            return; // later branches are never taken
                    }
                    else if (NameIs(branch, "Otherwise"))
                    {
                        EvaluateChildren(branch, depth, certain && !anyUnknown);
                    }
                }
            }

            /// <summary>Marks every property set below <paramref name="element"/> (including nested Choose) as unknown.</summary>
            private void MarkUnknown(XElement element)
            {
                foreach (var group in element.DescendantsAndSelf().Where(e => NameIs(e, "PropertyGroup")))
                {
                    foreach (var property in group.Elements())
                    {
                        _properties.Remove(property.Name.LocalName);
                        _unknownProperties.Add(property.Name.LocalName);
                    }
                }
            }

            private void EvaluateImport(XElement import, int depth)
            {
                if (!string.IsNullOrWhiteSpace(Attribute(import, "Sdk")))
                    return; // SDK props/targets: not part of the repository
                if (EvaluateCondition(import, CurrentDirectory) == false)
                    return;

                var project = Attribute(import, "Project");
                if (string.IsNullOrWhiteSpace(project))
                    return;

                foreach (var part in project.Split(';'))
                {
                    var expanded = Expand(part.Trim());
                    if (expanded.Unknown || string.IsNullOrWhiteSpace(expanded.Value))
                        continue;

                    var value = expanded.Value.Trim();
                    if (value.IndexOfAny(new[] { '*', '?' }) >= 0)
                    {
                        foreach (var file in ExpandWildcard(value, CurrentDirectory))
                            EvaluateFile(file, depth + 1);
                        continue;
                    }

                    var path = ToFullPath(value, CurrentDirectory);
                    if (path == null)
                        continue;
                    _result.Dependencies.Add(path);
                    if (File.Exists(path))
                        EvaluateFile(path, depth + 1);
                }
            }

            private void EvaluateProperties(XElement group)
            {
                foreach (var property in group.Elements())
                {
                    var condition = EvaluateCondition(property, _projectDirectory);
                    if (condition == false)
                        continue;

                    var name = property.Name.LocalName;
                    if (condition == null)
                    {
                        _properties.Remove(name);
                        _unknownProperties.Add(name);
                        continue;
                    }

                    var expanded = Expand(property.Value);
                    if (expanded.Unknown)
                    {
                        // The value depends on the build: later conditions on it are unknown
                        _properties.Remove(name);
                        _unknownProperties.Add(name);
                        continue;
                    }

                    _properties[name] = expanded.Value.Trim();
                    _unknownProperties.Remove(name);
                }
            }

            // ------------------------------------------------------------------ pass 2

            private void EvaluateItems(XElement group, bool certain)
            {
                var groupCondition = EvaluateCondition(group, _projectDirectory);
                if (groupCondition == false)
                    return;
                certain &= groupCondition == true;

                foreach (var item in group.Elements().Where(e => NameIs(e, "ProjectReference")))
                {
                    var itemCondition = EvaluateCondition(item, _projectDirectory);
                    if (itemCondition == false)
                        continue;
                    var itemCertain = certain && itemCondition == true;

                    var include = Attribute(item, "Include");
                    var remove = Attribute(item, "Remove");
                    var update = Attribute(item, "Update");
                    if (!string.IsNullOrWhiteSpace(include))
                    {
                        var excluded = string.IsNullOrWhiteSpace(Attribute(item, "Exclude"))
                            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                            : new HashSet<string>(ResolveItemSpecs(Attribute(item, "Exclude")), StringComparer.OrdinalIgnoreCase);
                        var noOutputAssembly = IsMetadata(item, "ReferenceOutputAssembly", "false");
                        var privateAssetsAll = MetadataContains(item, "PrivateAssets", "all");
                        foreach (var path in ResolveItemSpecs(include))
                        {
                            if (!excluded.Contains(path))
                                AddItem(path, itemCertain, noOutputAssembly, privateAssetsAll);
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(remove))
                    {
                        var removed = new HashSet<string>(ResolveItemSpecs(remove), StringComparer.OrdinalIgnoreCase);
                        // An uncertain Remove may not happen: keep the items, as uncertain
                        foreach (var existing in _items.Where(i => removed.Contains(i.Path)).ToList())
                        {
                            if (itemCertain)
                                _items.Remove(existing);
                            else
                                existing.Certain = false;
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(update) && itemCertain)
                    {
                        var updated = new HashSet<string>(ResolveItemSpecs(update), StringComparer.OrdinalIgnoreCase);
                        foreach (var existing in _items.Where(i => updated.Contains(i.Path)))
                        {
                            if (HasMetadata(item, "ReferenceOutputAssembly"))
                                existing.NoOutputAssembly = IsMetadata(item, "ReferenceOutputAssembly", "false");
                            if (HasMetadata(item, "PrivateAssets"))
                                existing.PrivateAssetsAll = MetadataContains(item, "PrivateAssets", "all");
                        }
                    }
                }
            }

            private void AddItem(string path, bool certain, bool noOutputAssembly, bool privateAssetsAll)
            {
                _result.Dependencies.Add(path);
                if (!File.Exists(path))
                {
                    _result.Unresolved.Add(path);
                    _logger?.Invoke("Warning", $"Referenced project not found: {path} (referenced by {_projectPath})");
                    return;
                }

                var existing = _items.FirstOrDefault(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.Certain |= certain;
                    return;
                }

                _items.Add(new ReferenceItem { Path = path, Certain = certain, NoOutputAssembly = noOutputAssembly, PrivateAssetsAll = privateAssetsAll });
            }

            private string MetadataValue(XElement item, string name)
            {
                var value = Attribute(item, name) ?? item.Elements().FirstOrDefault(e => NameIs(e, name))?.Value;
                if (value == null)
                    return null;
                var expanded = Expand(value);
                return expanded.Unknown ? null : expanded.Value.Trim();
            }

            private bool HasMetadata(XElement item, string name) =>
                Attribute(item, name) != null || item.Elements().Any(e => NameIs(e, name));

            private bool IsMetadata(XElement item, string name, string value) =>
                string.Equals(MetadataValue(item, name), value, StringComparison.OrdinalIgnoreCase);

            private bool MetadataContains(XElement item, string name, string value) =>
                (MetadataValue(item, name) ?? string.Empty).Split(';').Any(v => string.Equals(v.Trim(), value, StringComparison.OrdinalIgnoreCase));

            /// <summary>Item specs (';'-separated, expanded, wildcards resolved) as full paths, relative to the project directory.</summary>
            private IEnumerable<string> ResolveItemSpecs(string specs)
            {
                foreach (var raw in specs.Split(';'))
                {
                    var spec = raw.Trim();
                    if (spec.Length == 0)
                        continue;

                    if (spec.IndexOf("@(", StringComparison.Ordinal) >= 0 || spec.IndexOf("%(", StringComparison.Ordinal) >= 0)
                    {
                        _result.Unresolved.Add(spec);
                        _logger?.Invoke("Debug", $"ProjectReference '{spec}' in {CurrentFile} uses item or metadata references and is not tracked");
                        continue;
                    }

                    var expanded = Expand(spec);
                    if (expanded.Unknown)
                    {
                        _result.Unresolved.Add(spec);
                        _logger?.Invoke("Warning", $"ProjectReference '{spec}' in {CurrentFile} depends on values only known during a build and is not tracked");
                        continue;
                    }

                    // An expanded property can itself hold a ';' list
                    foreach (var part in expanded.Value.Split(';'))
                    {
                        var value = part.Trim();
                        if (value.Length == 0)
                            continue;

                        if (value.IndexOfAny(new[] { '*', '?' }) >= 0)
                        {
                            foreach (var file in ExpandWildcard(value, _projectDirectory))
                                yield return file;
                            continue;
                        }

                        var full = ToFullPath(value, _projectDirectory);
                        if (full != null)
                            yield return full;
                    }
                }
            }

            private string CurrentFile => _files.Count > 0 ? _files.Peek() : _projectPath;

            private string CurrentDirectory => Path.GetDirectoryName(CurrentFile);

            private static string ToFullPath(string value, string baseDirectory)
            {
                try
                {
                    var normalized = value.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                    return Path.GetFullPath(Path.IsPathRooted(normalized) ? normalized : Path.Combine(baseDirectory, normalized));
                }
                catch (Exception)
                {
                    return null;
                }
            }

            /// <summary>Directory names never searched by wildcards (huge and never hold projects of the repository).</summary>
            private static readonly HashSet<string> WildcardSkippedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".git", "node_modules", "bin", "obj"
            };

            private IEnumerable<string> ExpandWildcard(string pattern, string baseDirectory)
            {
                var normalized = pattern.Replace('\\', '/');
                var segments = normalized.Split('/');
                var fixedSegments = segments.TakeWhile(s => s.IndexOfAny(new[] { '*', '?' }) < 0).ToList();
                var fixedPart = string.Join("/", fixedSegments);
                string root;
                try
                {
                    root = Path.GetFullPath(normalized.StartsWith("/", StringComparison.Ordinal) || Path.IsPathRooted(fixedPart)
                        ? (fixedPart.Length == 0 ? "/" : fixedPart)
                        : Path.Combine(baseDirectory, fixedPart));
                }
                catch (Exception)
                {
                    return Enumerable.Empty<string>();
                }

                // A wildcard rooted at the file system root (e.g. an empty property: "$(Unset)/**/*.csproj") would
                // enumerate the whole disk
                if (string.Equals(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                        (Path.GetPathRoot(root) ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase))
                {
                    _logger?.Invoke("Debug", $"Wildcard '{pattern}' starts at the file system root and is not expanded");
                    return Enumerable.Empty<string>();
                }

                StampDirectory(root);
                if (!Directory.Exists(root))
                    return Enumerable.Empty<string>();

                var rest = string.Join("/", segments.Skip(fixedSegments.Count));
                var regex = new Regex("^" + GlobToRegex(rest) + "$",
                    (Path.DirectorySeparatorChar == '\\' ? RegexOptions.IgnoreCase : RegexOptions.None) | RegexOptions.CultureInvariant);
                var recursive = rest.Contains("**") || rest.Contains("/");

                var results = new List<string>();
                var pending = new Stack<string>();
                pending.Push(root);
                while (pending.Count > 0)
                {
                    var directory = pending.Pop();
                    try
                    {
                        foreach (var file in Directory.EnumerateFiles(directory))
                        {
                            var relative = file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                            if (regex.IsMatch(relative))
                                results.Add(Path.GetFullPath(file));
                        }

                        if (recursive)
                        {
                            foreach (var sub in Directory.EnumerateDirectories(directory))
                            {
                                if (WildcardSkippedDirectories.Contains(Path.GetFileName(sub)))
                                    continue;
                                StampDirectory(sub);
                                pending.Push(sub);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Unreadable directory: skip
                    }
                }

                return results;
            }

            /// <summary>Records a directory whose contents the result depends on (added or removed entries change its time).</summary>
            private void StampDirectory(string directory)
            {
                if (_stampedDirectories++ < MaxStampedDirectories)
                    _result.Dependencies.Add(directory);
            }

            private static string GlobToRegex(string glob)
            {
                var sb = new StringBuilder();
                for (var i = 0; i < glob.Length; i++)
                {
                    var c = glob[i];
                    if (c == '*')
                    {
                        if (i + 1 < glob.Length && glob[i + 1] == '*')
                        {
                            // "**/" matches zero or more directories
                            if (i + 2 < glob.Length && glob[i + 2] == '/')
                            {
                                sb.Append("(?:.*/)?");
                                i += 2;
                            }
                            else
                            {
                                sb.Append(".*");
                                i++;
                            }
                        }
                        else
                        {
                            sb.Append("[^/]*");
                        }
                    }
                    else if (c == '?')
                    {
                        sb.Append("[^/]");
                    }
                    else
                    {
                        sb.Append(Regex.Escape(c.ToString()));
                    }
                }
                return sb.ToString();
            }

            // ------------------------------------------------------------------ conditions

            /// <summary>
            /// true / false, or null when the condition cannot be evaluated here. Relative paths in Exists() resolve
            /// against <paramref name="baseDirectory"/>: the containing file's directory for Import, ImportGroup and
            /// PropertyGroup conditions, the project directory otherwise (as MSBuild does).
            /// </summary>
            private bool? EvaluateCondition(XElement element, string baseDirectory)
            {
                var condition = Attribute(element, "Condition");
                if (string.IsNullOrWhiteSpace(condition))
                    return true;

                try
                {
                    var parser = new ConditionParser(condition, this, baseDirectory);
                    var result = parser.ParseExpression();
                    return parser.AtEnd ? result.AsBool() : (bool?)null;
                }
                catch (Exception)
                {
                    return null;
                }
            }

            private sealed class Operand
            {
                public string Text;
                public bool? Bool;
                public bool Unknown;

                public bool? AsBool()
                {
                    if (Unknown)
                        return null;
                    if (Bool.HasValue)
                        return Bool;
                    if (string.Equals(Text?.Trim(), "true", StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (string.Equals(Text?.Trim(), "false", StringComparison.OrdinalIgnoreCase))
                        return false;
                    return null;
                }
            }

            /// <summary>Recursive-descent parser for MSBuild conditions: or &gt; and &gt; comparison &gt; unary.</summary>
            private sealed class ConditionParser
            {
                private readonly string _text;
                private readonly Evaluator _evaluator;
                private readonly string _baseDirectory;
                private int _position;

                public ConditionParser(string text, Evaluator evaluator, string baseDirectory)
                {
                    _text = text;
                    _evaluator = evaluator;
                    _baseDirectory = baseDirectory;
                }

                public bool AtEnd
                {
                    get
                    {
                        SkipWhitespace();
                        return _position >= _text.Length;
                    }
                }

                public Operand ParseExpression()
                {
                    var left = ParseAnd();
                    while (TryKeyword("or"))
                    {
                        var right = ParseAnd();
                        var l = left.AsBool();
                        var r = right.AsBool();
                        left = l == true || r == true
                            ? new Operand { Bool = true }
                            : l == false && r == false ? new Operand { Bool = false } : new Operand { Unknown = true };
                    }
                    return left;
                }

                private Operand ParseAnd()
                {
                    var left = ParseComparison();
                    while (TryKeyword("and"))
                    {
                        var right = ParseComparison();
                        var l = left.AsBool();
                        var r = right.AsBool();
                        left = l == false || r == false
                            ? new Operand { Bool = false }
                            : l == true && r == true ? new Operand { Bool = true } : new Operand { Unknown = true };
                    }
                    return left;
                }

                private Operand ParseComparison()
                {
                    var left = ParseUnary();
                    SkipWhitespace();
                    foreach (var op in new[] { "==", "!=", "<=", ">=", "<", ">" })
                    {
                        if (string.CompareOrdinal(_text, _position, op, 0, op.Length) == 0)
                        {
                            _position += op.Length;
                            var right = ParseUnary();
                            if (left.Unknown || right.Unknown)
                                return new Operand { Unknown = true };
                            return new Operand { Bool = Compare(op, left.Text ?? BoolText(left), right.Text ?? BoolText(right)) };
                        }
                    }
                    return left;
                }

                private static string BoolText(Operand operand) => operand.Bool.HasValue ? (operand.Bool.Value ? "true" : "false") : string.Empty;

                private static bool? Compare(string op, string left, string right)
                {
                    if (op == "==")
                        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
                    if (op == "!=")
                        return !string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

                    if (!double.TryParse(left, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var l) ||
                        !double.TryParse(right, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r))
                    {
                        if (!System.Version.TryParse(left, out var lv) || !System.Version.TryParse(right, out var rv))
                            return null;
                        var c = lv.CompareTo(rv);
                        return op == "<" ? c < 0 : op == ">" ? c > 0 : op == "<=" ? c <= 0 : c >= 0;
                    }

                    return op == "<" ? l < r : op == ">" ? l > r : op == "<=" ? l <= r : l >= r;
                }

                private Operand ParseUnary()
                {
                    SkipWhitespace();
                    if (_position >= _text.Length)
                        throw new FormatException("Unexpected end of condition");

                    var c = _text[_position];
                    if (c == '!')
                    {
                        _position++;
                        var operand = ParseUnary();
                        var value = operand.AsBool();
                        return value.HasValue ? new Operand { Bool = !value.Value } : new Operand { Unknown = true };
                    }

                    if (c == '(')
                    {
                        _position++;
                        var inner = ParseExpression();
                        SkipWhitespace();
                        Expect(')');
                        return inner;
                    }

                    if (c == '\'' || c == '"')
                    {
                        var literal = ReadQuoted(c);
                        var expanded = _evaluator.Expand(literal);
                        return expanded.Unknown ? new Operand { Unknown = true } : new Operand { Text = expanded.Value };
                    }

                    if (c == '$' && _position + 1 < _text.Length && _text[_position + 1] == '(')
                    {
                        var start = _position;
                        _position = FindClosingParen(_text, _position + 1) + 1;
                        var expanded = _evaluator.Expand(_text.Substring(start, _position - start));
                        return expanded.Unknown ? new Operand { Unknown = true } : new Operand { Text = expanded.Value };
                    }

                    if (c == '@' || c == '%')
                        throw new FormatException("Item references in conditions are not supported");

                    var identifier = ReadIdentifier();
                    if (identifier.Length == 0)
                    {
                        // Bare number or other literal
                        var start = _position;
                        while (_position < _text.Length && !char.IsWhiteSpace(_text[_position]) && "=!<>()".IndexOf(_text[_position]) < 0)
                            _position++;
                        if (_position == start)
                            throw new FormatException("Unexpected character in condition");
                        return new Operand { Text = _text.Substring(start, _position - start) };
                    }

                    SkipWhitespace();
                    if (_position < _text.Length && _text[_position] == '(')
                    {
                        _position++;
                        var args = new List<Operand>();
                        SkipWhitespace();
                        if (_position < _text.Length && _text[_position] != ')')
                        {
                            args.Add(ParseUnary());
                            SkipWhitespace();
                            while (_position < _text.Length && _text[_position] == ',')
                            {
                                _position++;
                                args.Add(ParseUnary());
                                SkipWhitespace();
                            }
                        }
                        Expect(')');
                        return CallFunction(identifier, args);
                    }

                    if (string.Equals(identifier, "true", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(identifier, "on", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(identifier, "yes", StringComparison.OrdinalIgnoreCase))
                        return new Operand { Bool = true };
                    if (string.Equals(identifier, "false", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(identifier, "off", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(identifier, "no", StringComparison.OrdinalIgnoreCase))
                        return new Operand { Bool = false };

                    return new Operand { Text = identifier };
                }

                private Operand CallFunction(string name, List<Operand> args)
                {
                    if (args.Any(a => a.Unknown))
                        return new Operand { Unknown = true };

                    if (string.Equals(name, "Exists", StringComparison.OrdinalIgnoreCase) && args.Count == 1)
                    {
                        var value = (args[0].Text ?? string.Empty).Trim();
                        if (value.Length == 0)
                            return new Operand { Bool = false };
                        var full = ToFullPath(value, _baseDirectory ?? _evaluator._projectDirectory);
                        if (full != null)
                            _evaluator._result.Dependencies.Add(full);
                        return new Operand { Bool = full != null && (File.Exists(full) || Directory.Exists(full)) };
                    }

                    if (string.Equals(name, "HasTrailingSlash", StringComparison.OrdinalIgnoreCase) && args.Count == 1)
                    {
                        var value = args[0].Text ?? string.Empty;
                        return new Operand { Bool = value.EndsWith("/", StringComparison.Ordinal) || value.EndsWith("\\", StringComparison.Ordinal) };
                    }

                    return new Operand { Unknown = true };
                }

                private string ReadQuoted(char quote)
                {
                    _position++; // opening quote
                    var start = _position;
                    var depth = 0;
                    while (_position < _text.Length)
                    {
                        var c = _text[_position];
                        if (c == '(')
                            depth++;
                        else if (c == ')')
                            depth--;
                        else if (c == quote && depth <= 0)
                            break;
                        _position++;
                    }
                    if (_position >= _text.Length)
                        throw new FormatException("Unterminated string in condition");
                    var value = _text.Substring(start, _position - start);
                    _position++; // closing quote
                    return value;
                }

                private string ReadIdentifier()
                {
                    var start = _position;
                    while (_position < _text.Length && (char.IsLetterOrDigit(_text[_position]) || _text[_position] == '_' || _text[_position] == '.'))
                        _position++;
                    // A bare number is not an identifier
                    if (_position > start && char.IsDigit(_text[start]))
                    {
                        _position = start;
                        return string.Empty;
                    }
                    return _text.Substring(start, _position - start);
                }

                private bool TryKeyword(string keyword)
                {
                    SkipWhitespace();
                    if (_position + keyword.Length <= _text.Length &&
                        string.Compare(_text, _position, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) == 0 &&
                        (_position + keyword.Length == _text.Length || !char.IsLetterOrDigit(_text[_position + keyword.Length])))
                    {
                        _position += keyword.Length;
                        return true;
                    }
                    return false;
                }

                private void Expect(char c)
                {
                    SkipWhitespace();
                    if (_position >= _text.Length || _text[_position] != c)
                        throw new FormatException($"Expected '{c}' in condition");
                    _position++;
                }

                private void SkipWhitespace()
                {
                    while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
                        _position++;
                }
            }

            // ------------------------------------------------------------------ property expansion

            /// <summary>Expands $(Property) and supported property functions in <paramref name="text"/>.</summary>
            private Expanded Expand(string text)
            {
                if (string.IsNullOrEmpty(text) || text.IndexOf("$(", StringComparison.Ordinal) < 0)
                    return Expanded.Of(text);

                var sb = new StringBuilder();
                var position = 0;
                while (position < text.Length)
                {
                    var start = text.IndexOf("$(", position, StringComparison.Ordinal);
                    if (start < 0)
                    {
                        sb.Append(text, position, text.Length - position);
                        break;
                    }

                    sb.Append(text, position, start - position);
                    int end;
                    try
                    {
                        end = FindClosingParen(text, start + 1);
                    }
                    catch (FormatException)
                    {
                        return Expanded.UnknownValue;
                    }

                    var inner = text.Substring(start + 2, end - start - 2).Trim();
                    var value = EvaluatePropertyExpression(inner);
                    if (value.Unknown)
                        return Expanded.UnknownValue;
                    sb.Append(value.Value);
                    position = end + 1;
                }

                return Expanded.Of(sb.ToString());
            }

            /// <summary>Name, Name.Method(...)..., or [Type]::Member(...).Method(...)...</summary>
            private Expanded EvaluatePropertyExpression(string inner)
            {
                Expanded value;
                string chain;
                if (inner.StartsWith("[", StringComparison.Ordinal))
                {
                    var typeEnd = inner.IndexOf("]::", StringComparison.Ordinal);
                    if (typeEnd < 0)
                        return Expanded.UnknownValue;
                    var type = inner.Substring(1, typeEnd - 1).Trim();
                    var rest = inner.Substring(typeEnd + 3);
                    var nameMatch = Regex.Match(rest, @"^\s*(\w+)\s*", RegexOptions.CultureInvariant);
                    if (!nameMatch.Success)
                        return Expanded.UnknownValue;
                    var member = nameMatch.Groups[1].Value;
                    var after = rest.Substring(nameMatch.Length);
                    List<string> args = null;
                    if (after.StartsWith("(", StringComparison.Ordinal))
                    {
                        int close;
                        try
                        {
                            close = FindClosingParen(after, 0);
                        }
                        catch (FormatException)
                        {
                            return Expanded.UnknownValue;
                        }
                        args = ExpandArguments(after.Substring(1, close - 1), out var unknownArgs);
                        if (unknownArgs)
                            return Expanded.UnknownValue;
                        after = after.Substring(close + 1);
                    }

                    try
                    {
                        value = EvaluateStaticMember(type, member, args);
                    }
                    catch (Exception)
                    {
                        return Expanded.UnknownValue;
                    }
                    chain = after;
                }
                else
                {
                    var nameMatch = Regex.Match(inner, @"^([A-Za-z_][\w\-]*)", RegexOptions.CultureInvariant);
                    if (!nameMatch.Success)
                        return Expanded.UnknownValue;
                    value = LookupProperty(nameMatch.Groups[1].Value);
                    chain = inner.Substring(nameMatch.Length);
                }

                if (value.Unknown || string.IsNullOrWhiteSpace(chain))
                    return value;
                return ApplyMethodChain(value.Value, chain);
            }

            private Expanded LookupProperty(string name)
            {
                if (_files.Count > 0)
                {
                    var file = _files.Peek();
                    if (string.Equals(name, "MSBuildThisFileDirectory", StringComparison.OrdinalIgnoreCase))
                        return Expanded.Of(EnsureTrailingSlash(Path.GetDirectoryName(file)));
                    if (string.Equals(name, "MSBuildThisFileFullPath", StringComparison.OrdinalIgnoreCase))
                        return Expanded.Of(file);
                    if (string.Equals(name, "MSBuildThisFile", StringComparison.OrdinalIgnoreCase))
                        return Expanded.Of(Path.GetFileName(file));
                    if (string.Equals(name, "MSBuildThisFileName", StringComparison.OrdinalIgnoreCase))
                        return Expanded.Of(Path.GetFileNameWithoutExtension(file));
                    if (string.Equals(name, "MSBuildThisFileExtension", StringComparison.OrdinalIgnoreCase))
                        return Expanded.Of(Path.GetExtension(file));
                    if (string.Equals(name, "MSBuildThisFileDirectoryNoRoot", StringComparison.OrdinalIgnoreCase))
                    {
                        var directory = EnsureTrailingSlash(Path.GetDirectoryName(file));
                        var root = Path.GetPathRoot(directory) ?? string.Empty;
                        return Expanded.Of(directory.Substring(Math.Min(root.Length, directory.Length)));
                    }
                }

                if (_properties.TryGetValue(name, out var value))
                    return Expanded.Of(value);
                if (_unknownProperties.Contains(name) || BuildTimeProperties.Contains(name))
                    return Expanded.UnknownValue;
                return Expanded.Of(string.Empty);
            }

            /// <summary>Applies ".Method(args)" / ".Property" calls on a string, as MSBuild property functions do.</summary>
            private Expanded ApplyMethodChain(string value, string chain)
            {
                object current = value;
                var position = 0;
                while (position < chain.Length)
                {
                    while (position < chain.Length && char.IsWhiteSpace(chain[position]))
                        position++;
                    if (position >= chain.Length)
                        break;
                    if (chain[position] != '.')
                        return Expanded.UnknownValue;
                    position++;

                    var nameMatch = Regex.Match(chain.Substring(position), @"^(\w+)\s*", RegexOptions.CultureInvariant);
                    if (!nameMatch.Success)
                        return Expanded.UnknownValue;
                    var method = nameMatch.Groups[1].Value;
                    position += nameMatch.Length;

                    List<string> args = null;
                    if (position < chain.Length && chain[position] == '(')
                    {
                        int close;
                        try
                        {
                            close = FindClosingParen(chain, position);
                        }
                        catch (FormatException)
                        {
                            return Expanded.UnknownValue;
                        }
                        args = ExpandArguments(chain.Substring(position + 1, close - position - 1), out var unknownArgs);
                        if (unknownArgs)
                            return Expanded.UnknownValue;
                        position = close + 1;
                    }

                    current = ApplyStringMethod(Convert.ToString(current, System.Globalization.CultureInfo.InvariantCulture), method, args);
                    if (current == null)
                        return Expanded.UnknownValue;
                }

                return Expanded.Of(Format(current));
            }

            private static string Format(object value) =>
                value is bool b ? (b ? "True" : "False") : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);

            /// <summary>The string instance members MSBuild projects commonly call; null when unsupported.</summary>
            private static object ApplyStringMethod(string value, string method, List<string> args)
            {
                var count = args?.Count ?? 0;
                string Arg(int i) => args[i];
                int IntArg(int i) => int.Parse(args[i].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                char[] Chars(int i) => args[i].Length == 0 ? null : args[i].ToCharArray();

                switch (method)
                {
                    case "Length": return args == null ? (object)value.Length : null;
                    case "Trim": return count == 0 ? value.Trim() : value.Trim(Chars(0));
                    case "TrimEnd": return count == 0 ? value.TrimEnd() : value.TrimEnd(Chars(0));
                    case "TrimStart": return count == 0 ? value.TrimStart() : value.TrimStart(Chars(0));
                    case "ToLower":
                    case "ToLowerInvariant": return value.ToLowerInvariant();
                    case "ToUpper":
                    case "ToUpperInvariant": return value.ToUpperInvariant();
                    case "ToString": return value;
                    case "EndsWith": return count >= 1 ? value.EndsWith(Arg(0), StringComparison.Ordinal) : (object)null;
                    case "StartsWith": return count >= 1 ? value.StartsWith(Arg(0), StringComparison.Ordinal) : (object)null;
                    case "Contains": return count == 1 ? value.IndexOf(Arg(0), StringComparison.Ordinal) >= 0 : (object)null;
                    case "Equals": return count >= 1 ? string.Equals(value, Arg(0), StringComparison.Ordinal) : (object)null;
                    case "IndexOf": return count == 1 ? value.IndexOf(Arg(0), StringComparison.Ordinal) : (object)null;
                    case "LastIndexOf": return count == 1 ? value.LastIndexOf(Arg(0), StringComparison.Ordinal) : (object)null;
                    case "Replace": return count == 2 && Arg(0).Length > 0 ? value.Replace(Arg(0), Arg(1)) : null;
                    case "Substring":
                        if (count == 1) return value.Substring(IntArg(0));
                        if (count == 2) return value.Substring(IntArg(0), IntArg(1));
                        return null;
                    case "PadLeft": return count == 1 ? value.PadLeft(IntArg(0)) : null;
                    case "PadRight": return count == 1 ? value.PadRight(IntArg(0)) : null;
                    default: return null;
                }
            }

            /// <summary>Expands a property function's argument list (quoted strings, numbers, nested $()).</summary>
            private List<string> ExpandArguments(string text, out bool unknown)
            {
                unknown = false;
                var result = new List<string>();
                foreach (var raw in SplitArguments(text))
                {
                    var arg = raw.Trim();
                    if (arg.Length >= 2 && (arg[0] == '\'' || arg[0] == '"' || arg[0] == '`') && arg[arg.Length - 1] == arg[0])
                        arg = arg.Substring(1, arg.Length - 2);
                    var expanded = Expand(arg);
                    if (expanded.Unknown)
                    {
                        unknown = true;
                        return result;
                    }
                    result.Add(expanded.Value);
                }
                return result;
            }

            private Expanded EvaluateStaticMember(string type, string member, List<string> args)
            {
                if (string.Equals(type, "MSBuild", StringComparison.OrdinalIgnoreCase))
                    return args == null ? Expanded.UnknownValue : EvaluateMSBuildFunction(member, args);
                if (IsPathType(type))
                {
                    if (args == null)
                        return member == "DirectorySeparatorChar" ? Expanded.Of(Path.DirectorySeparatorChar.ToString()) : Expanded.UnknownValue;
                    return EvaluatePathFunction(member, args);
                }
                if (string.Equals(type, "System.String", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(type, "String", StringComparison.OrdinalIgnoreCase))
                {
                    if (args == null)
                        return member == "Empty" ? Expanded.Of(string.Empty) : Expanded.UnknownValue;
                    switch (member)
                    {
                        case "IsNullOrEmpty": return args.Count == 1 ? Expanded.Of(Format(string.IsNullOrEmpty(args[0]))) : Expanded.UnknownValue;
                        case "IsNullOrWhiteSpace": return args.Count == 1 ? Expanded.Of(Format(string.IsNullOrWhiteSpace(args[0]))) : Expanded.UnknownValue;
                        case "Equals": return args.Count == 2 ? Expanded.Of(Format(string.Equals(args[0], args[1], StringComparison.Ordinal))) : Expanded.UnknownValue;
                        case "Copy": return args.Count == 1 ? Expanded.Of(args[0]) : Expanded.UnknownValue;
                        case "Concat": return Expanded.Of(string.Concat(args));
                        default: return Expanded.UnknownValue;
                    }
                }
                if ((string.Equals(type, "System.IO.File", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(type, "System.IO.Directory", StringComparison.OrdinalIgnoreCase)) &&
                    member == "Exists" && args != null && args.Count == 1)
                {
                    var full = ToFullPath(args[0], _projectDirectory);
                    if (full != null)
                        _result.Dependencies.Add(full);
                    var exists = full != null && (type.EndsWith("File", StringComparison.OrdinalIgnoreCase) ? File.Exists(full) : Directory.Exists(full));
                    return Expanded.Of(Format(exists));
                }
                return Expanded.UnknownValue;
            }

            private static bool IsPathType(string type) =>
                string.Equals(type, "System.IO.Path", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "IO.Path", StringComparison.OrdinalIgnoreCase);

            private Expanded EvaluateMSBuildFunction(string method, List<string> args)
            {
                switch (method)
                {
                    case "NormalizePath":
                        return args.Count == 0 ? Expanded.UnknownValue : Expanded.Of(NormalizePath(CombinePaths(args)));
                    case "NormalizeDirectory":
                        return args.Count == 0 ? Expanded.UnknownValue : Expanded.Of(EnsureTrailingSlash(NormalizePath(CombinePaths(args))));
                    case "EnsureTrailingSlash":
                        return args.Count == 1 ? Expanded.Of(args[0].Length == 0 ? args[0] : EnsureTrailingSlash(args[0])) : Expanded.UnknownValue;
                    case "GetDirectoryNameOfFileAbove":
                        if (args.Count != 2)
                            return Expanded.UnknownValue;
                        return Expanded.Of(GetDirectoryNameOfFileAbove(ToFullPath(args[0], CurrentDirectory), args[1]) ?? string.Empty);
                    case "GetPathOfFileAbove":
                        {
                            if (args.Count < 1 || args.Count > 2)
                                return Expanded.UnknownValue;
                            var start = args.Count == 2 && args[1].Length > 0 ? ToFullPath(args[1], CurrentDirectory) : CurrentDirectory;
                            var directory = GetDirectoryNameOfFileAbove(start, args[0]);
                            return Expanded.Of(directory == null ? string.Empty : Path.Combine(directory, args[0]));
                        }
                    case "MakeRelative":
                        if (args.Count != 2)
                            return Expanded.UnknownValue;
                        return Expanded.Of(PathUtils.GetRelativePath(args[0], args[1]));
                    case "ValueOrDefault":
                        return args.Count == 2 ? Expanded.Of(string.IsNullOrEmpty(args[0]) ? args[1] : args[0]) : Expanded.UnknownValue;
                    case "VersionEquals":
                    case "VersionNotEquals":
                    case "VersionGreaterThan":
                    case "VersionGreaterThanOrEquals":
                    case "VersionLessThan":
                    case "VersionLessThanOrEquals":
                        {
                            if (args.Count != 2 || !TryParseVersion(args[0], out var a) || !TryParseVersion(args[1], out var b))
                                return Expanded.UnknownValue;
                            var c = a.CompareTo(b);
                            var r = method == "VersionEquals" ? c == 0 : method == "VersionNotEquals" ? c != 0 :
                                method == "VersionGreaterThan" ? c > 0 : method == "VersionGreaterThanOrEquals" ? c >= 0 :
                                method == "VersionLessThan" ? c < 0 : c <= 0;
                            return Expanded.Of(Format(r));
                        }
                    case "IsOSPlatform":
                    case "IsOsPlatform":
                        if (args.Count != 1)
                            return Expanded.UnknownValue;
                        return Expanded.Of(IsOsPlatform(args[0]) ? "true" : "false");
                    default:
                        return Expanded.UnknownValue;
                }
            }

            private static bool IsOsPlatform(string platform)
            {
#if NET472
                return string.Equals(platform, "Windows", StringComparison.OrdinalIgnoreCase);
#else
                return System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Create(platform.ToUpperInvariant()));
#endif
            }

            private Expanded EvaluatePathFunction(string method, List<string> args)
            {
                switch (method)
                {
                    case "Combine":
                        return args.Count == 0 ? Expanded.UnknownValue : Expanded.Of(CombinePaths(args));
                    case "GetFullPath":
                        return args.Count == 1 ? Expanded.Of(ToFullPath(args[0], _projectDirectory) ?? args[0]) : Expanded.UnknownValue;
                    case "GetDirectoryName":
                        return args.Count == 1 ? Expanded.Of(Path.GetDirectoryName(args[0]) ?? string.Empty) : Expanded.UnknownValue;
                    case "GetFileName":
                        return args.Count == 1 ? Expanded.Of(Path.GetFileName(args[0])) : Expanded.UnknownValue;
                    case "GetFileNameWithoutExtension":
                        return args.Count == 1 ? Expanded.Of(Path.GetFileNameWithoutExtension(args[0])) : Expanded.UnknownValue;
                    default:
                        return Expanded.UnknownValue;
                }
            }

            private string CombinePaths(List<string> parts)
            {
                var result = string.Empty;
                foreach (var part in parts)
                {
                    if (string.IsNullOrEmpty(part))
                        continue;
                    var normalized = part.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                    result = result.Length == 0 || Path.IsPathRooted(normalized) ? normalized : Path.Combine(result, normalized);
                }
                return result;
            }

            private string NormalizePath(string path)
            {
                var full = ToFullPath(path, _projectDirectory) ?? path;
                return path.EndsWith("/", StringComparison.Ordinal) || path.EndsWith("\\", StringComparison.Ordinal)
                    ? EnsureTrailingSlash(full)
                    : full;
            }

            private static string GetDirectoryNameOfFileAbove(string startDirectory, string fileName)
            {
                if (string.IsNullOrEmpty(startDirectory))
                    return null;
                for (var current = startDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                     !string.IsNullOrEmpty(current);
                     current = Path.GetDirectoryName(current))
                {
                    if (File.Exists(Path.Combine(current, fileName)))
                        return current;
                }
                return null;
            }

            private static string EnsureTrailingSlash(string path)
            {
                if (string.IsNullOrEmpty(path))
                    return path;
                return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                       path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                    ? path
                    : path + Path.DirectorySeparatorChar;
            }

            private static bool TryParseVersion(string text, out System.Version version)
            {
                var trimmed = (text ?? string.Empty).Trim().TrimStart('v', 'V');
                var dash = trimmed.IndexOfAny(new[] { '-', '+' });
                if (dash >= 0)
                    trimmed = trimmed.Substring(0, dash);
                if (trimmed.IndexOf('.') < 0)
                    trimmed += ".0";
                return System.Version.TryParse(trimmed, out version);
            }

            private static IEnumerable<string> SplitArguments(string text)
            {
                var depth = 0;
                var quote = '\0';
                var start = 0;
                for (var i = 0; i < text.Length; i++)
                {
                    var c = text[i];
                    if (quote != '\0')
                    {
                        if (c == quote)
                            quote = '\0';
                        continue;
                    }
                    if (c == '\'' || c == '"' || c == '`')
                        quote = c;
                    else if (c == '(')
                        depth++;
                    else if (c == ')')
                        depth--;
                    else if (c == ',' && depth == 0)
                    {
                        yield return text.Substring(start, i - start);
                        start = i + 1;
                    }
                }
                if (text.Trim().Length > 0)
                    yield return text.Substring(start);
            }
        }

        /// <summary>Index of the ')' matching the '(' at <paramref name="openIndex"/>, honoring quotes.</summary>
        private static int FindClosingParen(string text, int openIndex)
        {
            var depth = 0;
            var quote = '\0';
            for (var i = openIndex; i < text.Length; i++)
            {
                var c = text[i];
                if (quote != '\0')
                {
                    if (c == quote)
                        quote = '\0';
                    continue;
                }
                if (c == '\'' || c == '"' || c == '`')
                    quote = c;
                else if (c == '(')
                    depth++;
                else if (c == ')')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }
            throw new FormatException("Unbalanced parentheses");
        }

        private static bool NameIs(XElement element, string name) =>
            string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase);

        private static string Attribute(XElement element, string name) =>
            element.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))?.Value;
    }
}
