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
    /// <item>evaluation order Directory.Build.props (and what it imports), the project file (and its imports), then
    /// Directory.Build.targets (and what it imports);</item>
    /// <item>properties from those files, environment variables and the reserved MSBuildProject* / MSBuildThisFile*
    /// properties, including common property functions ([MSBuild]::NormalizePath, GetDirectoryNameOfFileAbove,
    /// GetPathOfFileAbove, [System.IO.Path]::Combine, ...);</item>
    /// <item>Include lists separated by ';', wildcards, Remove, single- or double-quoted attributes;</item>
    /// <item>simple conditions ('$(A)' == 'b', !=, and, or, !, Exists(), HasTrailingSlash()).</item>
    /// </list>
    /// Conditions that depend on values only known during a build (target framework, configuration, runtime
    /// identifier, ...) or that cannot be evaluated count as true for items, so a project reference that applies to
    /// any target framework is tracked: over-including a dependency at worst bumps a version, missing one ships a
    /// changed dependency under an old version.
    /// </summary>
    public static class ProjectReferenceReader
    {
        private const int MaxImportDepth = 32;

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

        /// <summary>The result of reading a project file.</summary>
        public sealed class ProjectEvaluation
        {
            internal ProjectEvaluation(string projectPath)
            {
                ProjectPath = projectPath;
            }

            /// <summary>Full path of the project file.</summary>
            public string ProjectPath { get; }

            /// <summary>Full paths of the existing project files referenced by ProjectReference items.</summary>
            public List<string> References { get; } = new List<string>();

            /// <summary>ProjectReference items that could not be resolved to an existing file (as written, expanded where possible).</summary>
            public List<string> Unresolved { get; } = new List<string>();

            /// <summary>Evaluated properties (only those defined unconditionally or under conditions known to be true).</summary>
            public IReadOnlyDictionary<string, string> Properties { get; internal set; }

            /// <summary>The SDK named by the project (Sdk attribute, &lt;Sdk&gt; element or Import Sdk=), if any.</summary>
            public List<string> Sdks { get; } = new List<string>();

            /// <summary>Files read during evaluation (project, Directory.Build.props/targets and imports).</summary>
            public List<string> Files { get; } = new List<string>();
        }

        private sealed class CachedEvaluation
        {
            public List<Tuple<string, DateTime>> Stamps;
            public ProjectEvaluation Evaluation;
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
        /// themselves are included), as full paths. Cycles are tolerated.
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
                var full = Path.GetFullPath(root);
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
        /// Evaluates a project file (see the class remarks). Results are cached per project and reused while none of
        /// the files read changed. Returns null when the project file does not exist.
        /// </summary>
        public static ProjectEvaluation Evaluate(string projectPath, Action<string, string> logger = null)
        {
            if (string.IsNullOrEmpty(projectPath))
                return null;

            var fullPath = Path.GetFullPath(projectPath);
            if (!File.Exists(fullPath))
                return null;

            if (Cache.TryGetValue(fullPath, out var cached) && IsCurrent(cached.Stamps))
                return cached.Evaluation;

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
            }

            Cache[fullPath] = new CachedEvaluation
            {
                Evaluation = evaluation,
                Stamps = evaluation.Files.Select(f => Tuple.Create(f, SafeLastWriteTime(f))).ToList()
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

        private static bool IsCurrent(List<Tuple<string, DateTime>> stamps)
        {
            foreach (var stamp in stamps)
            {
                if (SafeLastWriteTime(stamp.Item1) != stamp.Item2)
                    return false;
            }
            return true;
        }

        private static DateTime SafeLastWriteTime(string path)
        {
            try
            {
                return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            }
            catch (Exception)
            {
                return DateTime.MinValue;
            }
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

        private sealed class Evaluator
        {
            private readonly string _projectPath;
            private readonly string _projectDirectory;
            private readonly Action<string, string> _logger;
            private readonly Dictionary<string, string> _properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            private readonly List<string> _references = new List<string>();
            private readonly HashSet<string> _referenceSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly ProjectEvaluation _result;
            private readonly Stack<string> _files = new Stack<string>();
            private readonly HashSet<string> _importing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

                var propsFile = FindFileAbove(_projectDirectory, "Directory.Build.props");
                if (propsFile != null && !IsFalse(_properties, "ImportDirectoryBuildProps"))
                {
                    _properties["DirectoryBuildPropsPath"] = propsFile;
                    EvaluateFile(propsFile, 0);
                }

                EvaluateFile(_projectPath, 0, isProject: true);

                // SDK defaults that Directory.Build.targets conditions commonly test, when not set explicitly
                if (!_properties.ContainsKey("OutputType"))
                    _properties["OutputType"] = "Library";
                if (!_properties.ContainsKey("IsTestProject") || !_properties.ContainsKey("IsPackable"))
                {
                    var type = ProjectAnalyzer.DetectProjectType(_projectPath);
                    if (!_properties.ContainsKey("IsTestProject"))
                        _properties["IsTestProject"] = type.IsTestProject ? "true" : "false";
                    if (!_properties.ContainsKey("IsPackable"))
                        _properties["IsPackable"] = type.IsPackable ? "true" : "false";
                }

                var targetsFile = FindFileAbove(_projectDirectory, "Directory.Build.targets");
                if (targetsFile != null && !IsFalse(_properties, "ImportDirectoryBuildTargets"))
                {
                    _properties["DirectoryBuildTargetsPath"] = targetsFile;
                    EvaluateFile(targetsFile, 0);
                }

                _result.References.AddRange(_references.Where(r => !string.Equals(r, _projectPath, StringComparison.OrdinalIgnoreCase)));
                _result.Properties = new Dictionary<string, string>(_properties, StringComparer.OrdinalIgnoreCase);
                return _result;
            }

            private static bool IsFalse(Dictionary<string, string> properties, string name) =>
                properties.TryGetValue(name, out var value) && string.Equals(value?.Trim(), "false", StringComparison.OrdinalIgnoreCase);

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

            private static string FindFileAbove(string directory, string fileName)
            {
                for (var current = directory; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                {
                    var candidate = Path.Combine(current, fileName);
                    if (File.Exists(candidate))
                        return candidate;
                }
                return null;
            }

            private void EvaluateFile(string path, int depth, bool isProject = false)
            {
                var full = Path.GetFullPath(path);
                if (depth > MaxImportDepth || !_importing.Add(full))
                    return;

                try
                {
                    if (!_result.Files.Contains(full, StringComparer.OrdinalIgnoreCase))
                        _result.Files.Add(full);

                    var document = LoadDocument(full);
                    if (document?.Root == null)
                    {
                        if (isProject)
                            EvaluateWithRegexFallback(full);
                        return;
                    }

                    _files.Push(full);
                    try
                    {
                        if (isProject)
                            CollectSdks(document.Root);
                        EvaluateChildren(document.Root, depth);
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
                        AddReferences(include, null);
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
                    if (NameIs(element, "Sdk"))
                    {
                        var name = Attribute(element, "Name");
                        if (!string.IsNullOrWhiteSpace(name))
                            _result.Sdks.Add(name.Trim());
                    }
                    else if (NameIs(element, "Import"))
                    {
                        var sdk = Attribute(element, "Sdk");
                        if (!string.IsNullOrWhiteSpace(sdk) && !_result.Sdks.Contains(sdk.Trim(), StringComparer.OrdinalIgnoreCase))
                            _result.Sdks.Add(sdk.Trim());
                    }
                }
            }

            private void EvaluateChildren(XElement parent, int depth)
            {
                foreach (var element in parent.Elements())
                {
                    if (NameIs(element, "PropertyGroup"))
                    {
                        var condition = EvaluateCondition(element);
                        if (condition == true)
                        {
                            EvaluateProperties(element);
                        }
                        else if (condition == null)
                        {
                            // The group may or may not apply: its properties' values are unknown from here on
                            foreach (var property in element.Elements())
                            {
                                _properties.Remove(property.Name.LocalName);
                                _unknownProperties.Add(property.Name.LocalName);
                            }
                        }
                    }
                    else if (NameIs(element, "ItemGroup"))
                    {
                        // Unknown group conditions still contribute items (see class remarks)
                        if (EvaluateCondition(element) != false)
                            EvaluateItems(element);
                    }
                    else if (NameIs(element, "Import"))
                    {
                        EvaluateImport(element, depth);
                    }
                    else if (NameIs(element, "ImportGroup"))
                    {
                        if (EvaluateCondition(element) != false)
                        {
                            foreach (var import in element.Elements().Where(e => NameIs(e, "Import")))
                                EvaluateImport(import, depth);
                        }
                    }
                    else if (NameIs(element, "Choose"))
                    {
                        EvaluateChoose(element, depth);
                    }
                    // Target, UsingTask, ProjectExtensions, Sdk: nothing to evaluate
                }
            }

            private void EvaluateChoose(XElement choose, int depth)
            {
                var anyUnknown = false;
                foreach (var branch in choose.Elements())
                {
                    if (NameIs(branch, "When"))
                    {
                        var condition = EvaluateCondition(branch);
                        if (condition == false)
                            continue;

                        if (condition == true && !anyUnknown)
                        {
                            EvaluateChildren(branch, depth);
                            return;
                        }

                        // The branch might be the one taken (its condition is unknown, or an earlier unknown branch
                        // might win): take its items, not its properties
                        anyUnknown = true;
                        foreach (var itemGroup in branch.Elements().Where(e => NameIs(e, "ItemGroup")))
                        {
                            if (EvaluateCondition(itemGroup) != false)
                                EvaluateItems(itemGroup);
                        }

                        if (condition == true)
                            return; // later branches are never taken
                    }
                    else if (NameIs(branch, "Otherwise"))
                    {
                        if (!anyUnknown)
                        {
                            EvaluateChildren(branch, depth);
                        }
                        else
                        {
                            foreach (var itemGroup in branch.Elements().Where(e => NameIs(e, "ItemGroup")))
                            {
                                if (EvaluateCondition(itemGroup) != false)
                                    EvaluateItems(itemGroup);
                            }
                        }
                    }
                }
            }

            private void EvaluateImport(XElement import, int depth)
            {
                if (!string.IsNullOrWhiteSpace(Attribute(import, "Sdk")))
                    return; // SDK props/targets: not part of the repository
                if (EvaluateCondition(import) == false)
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
                    if (path != null && File.Exists(path))
                        EvaluateFile(path, depth + 1);
                }
            }

            private void EvaluateProperties(XElement group)
            {
                foreach (var property in group.Elements())
                {
                    var condition = EvaluateCondition(property);
                    if (condition == false)
                        continue;
                    if (condition == null)
                    {
                        _properties.Remove(property.Name.LocalName);
                        _unknownProperties.Add(property.Name.LocalName);
                        continue;
                    }

                    var expanded = Expand(property.Value);
                    if (expanded.Unknown)
                    {
                        // The value depends on the build: later conditions on it are unknown
                        _properties.Remove(property.Name.LocalName);
                        _unknownProperties.Add(property.Name.LocalName);
                        continue;
                    }

                    _properties[property.Name.LocalName] = expanded.Value.Trim();
                    _unknownProperties.Remove(property.Name.LocalName);
                }
            }

            private readonly HashSet<string> _unknownProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            private void EvaluateItems(XElement group)
            {
                foreach (var item in group.Elements().Where(e => NameIs(e, "ProjectReference")))
                {
                    if (EvaluateCondition(item) == false)
                        continue;

                    var include = Attribute(item, "Include");
                    var remove = Attribute(item, "Remove");
                    if (!string.IsNullOrWhiteSpace(include))
                    {
                        AddReferences(include, Attribute(item, "Exclude"));
                    }
                    else if (!string.IsNullOrWhiteSpace(remove))
                    {
                        foreach (var path in ResolveItemSpecs(remove))
                        {
                            if (_referenceSet.Remove(path))
                                _references.RemoveAll(r => string.Equals(r, path, StringComparison.OrdinalIgnoreCase));
                        }
                    }
                }
            }

            private void AddReferences(string include, string exclude)
            {
                var excluded = string.IsNullOrWhiteSpace(exclude)
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(ResolveItemSpecs(exclude), StringComparer.OrdinalIgnoreCase);

                foreach (var path in ResolveItemSpecs(include))
                {
                    if (excluded.Contains(path))
                        continue;

                    if (!File.Exists(path))
                    {
                        _result.Unresolved.Add(path);
                        _logger?.Invoke("Warning", $"Referenced project not found: {path} (referenced by {_projectPath})");
                        continue;
                    }

                    if (_referenceSet.Add(path))
                        _references.Add(path);
                }
            }

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

            private static IEnumerable<string> ExpandWildcard(string pattern, string baseDirectory)
            {
                var normalized = pattern.Replace('\\', '/');
                var segments = normalized.Split('/');
                var fixedSegments = segments.TakeWhile(s => s.IndexOfAny(new[] { '*', '?' }) < 0).ToList();
                var fixedPart = string.Join("/", fixedSegments);
                string root;
                try
                {
                    root = Path.GetFullPath(Path.IsPathRooted(fixedPart) || normalized.StartsWith("/", StringComparison.Ordinal)
                        ? (fixedPart.Length == 0 ? "/" : fixedPart)
                        : Path.Combine(baseDirectory, fixedPart));
                }
                catch (Exception)
                {
                    yield break;
                }

                if (!Directory.Exists(root))
                    yield break;

                var rest = string.Join("/", segments.Skip(fixedSegments.Count));
                var regex = new Regex("^" + GlobToRegex(rest) + "$",
                    (Path.DirectorySeparatorChar == '\\' ? RegexOptions.IgnoreCase : RegexOptions.None) | RegexOptions.CultureInvariant);
                var recursive = rest.Contains("**") || rest.Contains("/");

                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(root, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).ToList();
                }
                catch (Exception)
                {
                    yield break;
                }

                foreach (var file in files)
                {
                    var relative = file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                    if (regex.IsMatch(relative))
                        yield return Path.GetFullPath(file);
                }
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

            /// <summary>true / false, or null when the condition cannot be evaluated here.</summary>
            private bool? EvaluateCondition(XElement element)
            {
                var condition = Attribute(element, "Condition");
                if (string.IsNullOrWhiteSpace(condition))
                    return true;

                try
                {
                    var parser = new ConditionParser(condition, this);
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
                private int _position;

                public ConditionParser(string text, Evaluator evaluator)
                {
                    _text = text;
                    _evaluator = evaluator;
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
                        var full = ToFullPath(value, _evaluator._projectDirectory);
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

            private Expanded EvaluatePropertyExpression(string inner)
            {
                if (inner.StartsWith("[", StringComparison.Ordinal))
                    return EvaluateStaticFunction(inner);

                // $(Name) or $(Name.Method(...)): only plain names and a few string methods are supported
                var dot = inner.IndexOf('.');
                var name = dot < 0 ? inner : inner.Substring(0, dot);
                if (!IsValidPropertyName(name))
                    return Expanded.UnknownValue;

                var value = LookupProperty(name);
                if (value.Unknown || dot < 0)
                    return value;

                return ApplyStringMethods(value.Value, inner.Substring(dot));
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

            private static Expanded ApplyStringMethods(string value, string chain)
            {
                // Supports .Trim(), .TrimEnd('x'), .TrimStart('x'), .ToLower(), .ToUpper(), .ToLowerInvariant(), .ToUpperInvariant()
                var match = Regex.Match(chain, @"^(?:\.(Trim|TrimEnd|TrimStart|ToLower|ToUpper|ToLowerInvariant|ToUpperInvariant)\(\s*(?:'([^']*)'|""([^""]*)"")?\s*\))+$",
                    RegexOptions.CultureInvariant);
                if (!match.Success)
                    return Expanded.UnknownValue;

                foreach (Match call in Regex.Matches(chain, @"\.(\w+)\(\s*(?:'([^']*)'|""([^""]*)"")?\s*\)", RegexOptions.CultureInvariant))
                {
                    var method = call.Groups[1].Value;
                    var argument = call.Groups[2].Success ? call.Groups[2].Value : call.Groups[3].Success ? call.Groups[3].Value : null;
                    var chars = string.IsNullOrEmpty(argument) ? null : argument.ToCharArray();
                    switch (method)
                    {
                        case "Trim": value = chars == null ? value.Trim() : value.Trim(chars); break;
                        case "TrimEnd": value = chars == null ? value.TrimEnd() : value.TrimEnd(chars); break;
                        case "TrimStart": value = chars == null ? value.TrimStart() : value.TrimStart(chars); break;
                        case "ToLower":
                        case "ToLowerInvariant": value = value.ToLowerInvariant(); break;
                        case "ToUpper":
                        case "ToUpperInvariant": value = value.ToUpperInvariant(); break;
                        default: return Expanded.UnknownValue;
                    }
                }

                return Expanded.Of(value);
            }

            private Expanded EvaluateStaticFunction(string inner)
            {
                // [Type]::Method(args)
                var match = Regex.Match(inner, @"^\[\s*([\w\.]+)\s*\]::(\w+)\s*\((.*)\)\s*$", RegexOptions.Singleline | RegexOptions.CultureInvariant);
                if (!match.Success)
                {
                    // [Type]::Property, e.g. [System.IO.Path]::DirectorySeparatorChar
                    var property = Regex.Match(inner, @"^\[\s*([\w\.]+)\s*\]::(\w+)\s*$", RegexOptions.CultureInvariant);
                    if (property.Success && IsPathType(property.Groups[1].Value) &&
                        property.Groups[2].Value == "DirectorySeparatorChar")
                        return Expanded.Of(Path.DirectorySeparatorChar.ToString());
                    return Expanded.UnknownValue;
                }

                var type = match.Groups[1].Value;
                var method = match.Groups[2].Value;
                var args = new List<string>();
                foreach (var raw in SplitArguments(match.Groups[3].Value))
                {
                    var arg = raw.Trim();
                    if (arg.Length >= 2 && (arg[0] == '\'' || arg[0] == '"' || arg[0] == '`') && arg[arg.Length - 1] == arg[0])
                        arg = arg.Substring(1, arg.Length - 2);
                    var expanded = Expand(arg);
                    if (expanded.Unknown)
                        return Expanded.UnknownValue;
                    args.Add(expanded.Value);
                }

                try
                {
                    if (string.Equals(type, "MSBuild", StringComparison.OrdinalIgnoreCase))
                        return EvaluateMSBuildFunction(method, args);
                    if (IsPathType(type))
                        return EvaluatePathFunction(method, args);
                }
                catch (Exception)
                {
                    return Expanded.UnknownValue;
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
