using System;
using System.IO;
using System.Linq;
using Mister.Version.Core.Services;
using Xunit;

namespace Mister.Version.Tests
{
    /// <summary>
    /// ProjectReference evaluation without MSBuild: the reference styles found in real monorepos.
    /// </summary>
    public class ProjectReferenceReaderTests : IDisposable
    {
        private readonly string _root;

        public ProjectReferenceReaderTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "mrv-refs-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            ProjectReferenceReader.ClearCache();
        }

        public void Dispose()
        {
            ProjectReferenceReader.ClearCache();
            try { Directory.Delete(_root, true); } catch { }
        }

        private string Write(string relative, string content)
        {
            var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
            return path;
        }

        private string Lib(string name) => Write($"src/{name}/{name}.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        private string[] Refs(string projectPath) =>
            ProjectReferenceReader.GetDirectReferences(projectPath)
                .Select(p => Path.GetFileNameWithoutExtension(p))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

        private static string Project(string items, string properties = "") =>
            $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework>{properties}</PropertyGroup><ItemGroup>{items}</ItemGroup></Project>";

        [Fact]
        public void PlainRelativeReferences_WithBackslashes()
        {
            Lib("Core");
            var app = Write("src/App/App.csproj", Project("<ProjectReference Include=\"..\\Core\\Core.csproj\" />"));
            Assert.Equal(new[] { "Core" }, Refs(app));
        }

        [Fact]
        public void MSBuildThisFileDirectory_AndPropertiesFromDirectoryBuildProps()
        {
            Lib("Lib1");
            Lib("Lib2");
            Write("Directory.Build.props", "<Project><PropertyGroup><RepoRoot>$(MSBuildThisFileDirectory)</RepoRoot></PropertyGroup></Project>");
            var app = Write("src/App/App.csproj", Project(
                "<ProjectReference Include=\"$(MSBuildThisFileDirectory)../Lib1/Lib1.csproj\" />" +
                "<ProjectReference Include=\"$(RepoRoot)src/Lib2/Lib2.csproj\" />"));
            Assert.Equal(new[] { "Lib1", "Lib2" }, Refs(app));
        }

        [Fact]
        public void SemicolonListsAndSingleQuotes()
        {
            Lib("Lib3");
            Lib("Lib4");
            Lib("Lib5");
            var app = Write("src/App/App.csproj", Project(
                "<ProjectReference Include=\"../Lib3/Lib3.csproj;../Lib4/Lib4.csproj\" />" +
                "<ProjectReference Include='../Lib5/Lib5.csproj' />"));
            Assert.Equal(new[] { "Lib3", "Lib4", "Lib5" }, Refs(app));
        }

        [Fact]
        public void ReferencesFromDirectoryBuildTargets_HonorSelfExclusionConditions()
        {
            var shared = Lib("Shared");
            Write("Directory.Build.targets",
                "<Project><ItemGroup Condition=\"'$(MSBuildProjectName)' != 'Shared'\">" +
                "<ProjectReference Include=\"$(MSBuildThisFileDirectory)src/Shared/Shared.csproj\" /></ItemGroup></Project>");
            var app = Write("src/App/App.csproj", Project(""));
            Assert.Equal(new[] { "Shared" }, Refs(app));
            Assert.Empty(Refs(shared));
        }

        [Fact]
        public void DirectoryBuildTargets_ConditionOnTestProjects()
        {
            Lib("TestUtils");
            Write("Directory.Build.targets",
                "<Project><ItemGroup Condition=\"'$(IsTestProject)' == 'true'\">" +
                "<ProjectReference Include=\"$(MSBuildThisFileDirectory)src/TestUtils/TestUtils.csproj\" /></ItemGroup></Project>");
            var tests = Write("tests/App.Tests/App.Tests.csproj", Project("<PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.0.0\" />"));
            var app = Write("src/App/App.csproj", Project(""));
            Assert.Equal(new[] { "TestUtils" }, Refs(tests));
            Assert.Empty(Refs(app));
        }

        [Fact]
        public void TargetFrameworkConditions_AreTreatedAsApplying()
        {
            Lib("Net8Only");
            Lib("Net10Only");
            Lib("Never");
            var app = Write("src/App/App.csproj",
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFrameworks>net8.0;net10.0</TargetFrameworks></PropertyGroup>" +
                "<ItemGroup Condition=\"'$(TargetFramework)' == 'net8.0'\"><ProjectReference Include=\"../Net8Only/Net8Only.csproj\" /></ItemGroup>" +
                "<ItemGroup><ProjectReference Include=\"../Net10Only/Net10Only.csproj\" Condition=\"$([MSBuild]::IsTargetFrameworkCompatible('$(TargetFramework)', 'net10.0'))\" /></ItemGroup>" +
                "<ItemGroup Condition=\"'$(UseNever)' == 'true'\"><ProjectReference Include=\"../Never/Never.csproj\" /></ItemGroup>" +
                "</Project>");
            Assert.Equal(new[] { "Net10Only", "Net8Only" }, Refs(app));
        }

        [Fact]
        public void ChooseWhen_TakesTheMatchingBranch()
        {
            Lib("A");
            Lib("B");
            var app = Write("src/App/App.csproj",
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><Flavor>b</Flavor></PropertyGroup><Choose>" +
                "<When Condition=\"'$(Flavor)' == 'a'\"><ItemGroup><ProjectReference Include=\"../A/A.csproj\" /></ItemGroup></When>" +
                "<Otherwise><ItemGroup><ProjectReference Include=\"../B/B.csproj\" /></ItemGroup></Otherwise></Choose></Project>");
            Assert.Equal(new[] { "B" }, Refs(app));
        }

        [Fact]
        public void ImportedFiles_AndParentDirectoryBuildPropsChain()
        {
            Lib("FromImport");
            Lib("FromParent");
            Write("Directory.Build.props", "<Project><PropertyGroup><Shared>$(MSBuildThisFileDirectory)src/</Shared></PropertyGroup></Project>");
            Write("src/Directory.Build.props",
                "<Project><Import Project=\"$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))\" /></Project>");
            Write("build/refs.props", "<Project><ItemGroup><ProjectReference Include=\"$(Shared)FromImport/FromImport.csproj\" /></ItemGroup></Project>");
            var app = Write("src/App/App.csproj",
                "<Project Sdk=\"Microsoft.NET.Sdk\"><Import Project=\"$(MSBuildThisFileDirectory)../../build/refs.props\" />" +
                "<ItemGroup><ProjectReference Include=\"$(Shared)FromParent/FromParent.csproj\" /></ItemGroup></Project>");
            Assert.Equal(new[] { "FromImport", "FromParent" }, Refs(app));
        }

        [Fact]
        public void WildcardsAndRemove()
        {
            Lib("Plugin.A");
            Lib("Plugin.B");
            Lib("Plugin.Skip");
            var app = Write("src/App/App.csproj", Project(
                "<ProjectReference Include=\"../Plugin.*/*.csproj\" />" +
                "<ProjectReference Remove=\"../Plugin.Skip/Plugin.Skip.csproj\" />"));
            Assert.Equal(new[] { "Plugin.A", "Plugin.B" }, Refs(app));
        }

        [Fact]
        public void PropertyFunctions_NormalizePathAndDirectoryNameOfFileAbove()
        {
            Lib("Core");
            Write("repo.root", "");
            var app = Write("src/App/App.csproj", Project(
                "<ProjectReference Include=\"$([MSBuild]::NormalizePath('$(Root)', 'src', 'Core', 'Core.csproj'))\" />",
                "<Root>$([MSBuild]::GetDirectoryNameOfFileAbove($(MSBuildProjectDirectory), repo.root))</Root>"));
            Assert.Equal(new[] { "Core" }, Refs(app));
        }

        [Fact]
        public void MissingReferences_AreReportedNotReturned()
        {
            var app = Write("src/App/App.csproj", Project("<ProjectReference Include=\"../Gone/Gone.csproj\" />"));
            var evaluation = ProjectReferenceReader.Evaluate(app);
            Assert.Empty(evaluation.References);
            Assert.Single(evaluation.Unresolved);
        }

        [Fact]
        public void MalformedXml_FallsBackToIncludeAttributes()
        {
            Lib("Core");
            var app = Write("src/App/App.csproj", "<Project><ItemGroup><ProjectReference Include=\"../Core/Core.csproj\" /></ItemGroup><Oops></Project>");
            Assert.Equal(new[] { "Core" }, Refs(app));
        }

        [Fact]
        public void Closure_FollowsReferenceOutputAssemblyFalseAndPrivateAssetsEdges()
        {
            Lib("Gen");
            Lib("Priv");
            Write("src/Lib/Lib.csproj", Project(
                "<ProjectReference Include=\"../Gen/Gen.csproj\" OutputItemType=\"Analyzer\" ReferenceOutputAssembly=\"false\" />" +
                "<ProjectReference Include=\"../Priv/Priv.csproj\" PrivateAssets=\"all\" />"));
            var app = Write("src/App/App.csproj", Project("<ProjectReference Include=\"../Lib/Lib.csproj\" />"));

            var closure = ProjectReferenceReader.GetReferenceClosure(new[] { app })
                .Select(p => Path.GetFileNameWithoutExtension(p))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(new[] { "App", "Gen", "Lib", "Priv" }, closure);
        }

        [Fact]
        public void SameFileNameInDifferentDirectories_StaysDistinct()
        {
            Write("src/a/Utils/Utils.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Lib("Helper");
            Write("src/b/Utils/Utils.csproj", Project("<ProjectReference Include=\"../../Helper/Helper.csproj\" />"));
            var app = Write("src/App/App.csproj", Project("<ProjectReference Include=\"../b/Utils/Utils.csproj\" />"));

            var closure = ProjectReferenceReader.GetReferenceClosure(new[] { app });
            Assert.Contains(closure, p => p.EndsWith(Path.Combine("b", "Utils", "Utils.csproj")));
            Assert.Contains(closure, p => p.EndsWith(Path.Combine("Helper", "Helper.csproj")));
            Assert.DoesNotContain(closure, p => p.EndsWith(Path.Combine("a", "Utils", "Utils.csproj")));
        }

        [Fact]
        public void Cache_IsInvalidatedWhenAnImportedFileChanges()
        {
            Lib("First");
            Lib("Second");
            var targets = Write("Directory.Build.targets", "<Project><ItemGroup><ProjectReference Include=\"$(MSBuildThisFileDirectory)src/First/First.csproj\" /></ItemGroup></Project>");
            var app = Write("src/App/App.csproj", Project(""));
            Assert.Equal(new[] { "First" }, Refs(app));

            File.WriteAllText(targets, "<Project><ItemGroup><ProjectReference Include=\"$(MSBuildThisFileDirectory)src/Second/Second.csproj\" /></ItemGroup></Project>");
            File.SetLastWriteTimeUtc(targets, DateTime.UtcNow.AddMinutes(1));
            Assert.Equal(new[] { "Second" }, Refs(app));
        }

        // ------------------------------------------------------------------ review findings

        private ProjectReferenceReader.ReferenceItem Item(string projectPath, string name) =>
            ProjectReferenceReader.Evaluate(projectPath).ReferenceItems.FirstOrDefault(i => Path.GetFileNameWithoutExtension(i.Path) == name);

        [Fact]
        public void StringMethodConditions_AreEvaluated()
        {
            Lib("TestUtils");
            Write("Directory.Build.targets",
                "<Project><ItemGroup Condition=\"$(MSBuildProjectName.EndsWith('.Tests'))\">" +
                "<ProjectReference Include=\"$(MSBuildThisFileDirectory)src/TestUtils/TestUtils.csproj\" /></ItemGroup></Project>");
            var lib = Write("src/Lib/Lib.csproj", Project(""));
            var tests = Write("tests/Lib.Tests/Lib.Tests.csproj", Project(""));
            Assert.Empty(Refs(lib));
            Assert.Equal(new[] { "TestUtils" }, Refs(tests));
            Assert.True(Item(tests, "TestUtils").Certain);
        }

        [Fact]
        public void ItemsSeeFinalPropertyValues_TwoPassEvaluation()
        {
            Lib("Contracts");
            Lib("Logging");
            Write("Directory.Build.props",
                "<Project>" +
                "<ItemGroup Condition=\"'$(UseContracts)' == 'true'\"><ProjectReference Include=\"$(MSBuildThisFileDirectory)src/Contracts/Contracts.csproj\" /></ItemGroup>" +
                "<ItemGroup Condition=\"'$(UseSharedLogging)' != 'false'\"><ProjectReference Include=\"$(MSBuildThisFileDirectory)src/Logging/Logging.csproj\" /></ItemGroup>" +
                "</Project>");
            var app = Write("src/App/App.csproj", Project("", "<UseContracts>true</UseContracts><UseSharedLogging>false</UseSharedLogging>"));
            Assert.Equal(new[] { "Contracts" }, Refs(app));
        }

        [Fact]
        public void SdkDefinedProperties_AreKnown()
        {
            Lib("WebCommon");
            Write("Directory.Build.targets",
                "<Project><ItemGroup Condition=\"'$(UsingMicrosoftNETSdkWeb)' == 'true'\">" +
                "<ProjectReference Include=\"$(MSBuildThisFileDirectory)src/WebCommon/WebCommon.csproj\" /></ItemGroup></Project>");
            var web = Write("src/Web/Web.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />");
            var lib = Write("src/Lib/Lib.csproj", Project(""));
            Assert.Equal(new[] { "WebCommon" }, Refs(web));
            Assert.Empty(Refs(lib));
        }

        [Fact]
        public void ExistsInImportConditions_IsRelativeToTheContainingFile()
        {
            Lib("Core");
            Write("Directory.Build.props",
                "<Project><PropertyGroup><RepoSrc>$(MSBuildThisFileDirectory)src/</RepoSrc></PropertyGroup>" +
                "<ItemGroup Condition=\"'$(MSBuildProjectName)' != 'Core'\"><ProjectReference Include=\"$(RepoSrc)Core/Core.csproj\" /></ItemGroup></Project>");
            Write("src/Directory.Build.props",
                "<Project><Import Project=\"../Directory.Build.props\" Condition=\"Exists('../Directory.Build.props')\" /></Project>");
            var app = Write("src/Group/App/App.csproj", Project(""));
            Assert.Equal(new[] { "Core" }, Refs(app));
        }

        [Fact]
        public void ChooseWithUnknownCondition_MakesItsPropertiesUnknown()
        {
            Lib("Legacy");
            var app = Write("src/App/App.csproj",
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFrameworks>net472;net10.0</TargetFrameworks></PropertyGroup>" +
                "<Choose><When Condition=\"'$(TargetFramework)'=='net472'\"><PropertyGroup><UseLegacy>true</UseLegacy></PropertyGroup></When></Choose>" +
                "<ItemGroup Condition=\"'$(UseLegacy)'=='true'\"><ProjectReference Include=\"../Legacy/Legacy.csproj\" /></ItemGroup></Project>");
            Assert.Equal(new[] { "Legacy" }, Refs(app));
            Assert.False(Item(app, "Legacy").Certain);
        }

        [Fact]
        public void Cache_SeesFilesThatAppearAfterTheFirstEvaluation()
        {
            var app = Write("src/App/App.csproj", Project("<ProjectReference Include=\"../Lib/Lib.csproj\" />"));
            Assert.Empty(Refs(app));

            Lib("Lib");
            Assert.Equal(new[] { "Lib" }, Refs(app));

            Lib("Gen");
            Write("src/App/Directory.Build.targets", "<Project><ItemGroup><ProjectReference Include=\"../Gen/Gen.csproj\" /></ItemGroup></Project>");
            Assert.Equal(new[] { "Gen", "Lib" }, Refs(app));
        }

        [Fact]
        public void WildcardUnderAnEmptyProperty_DoesNotSearchTheFileSystemRoot()
        {
            var app = Write("src/App/App.csproj", Project("<ProjectReference Include=\"$(NotSet)/**/*.csproj\" />"));
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            Assert.Empty(Refs(app));
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
        }

        [Fact]
        public void UnflowedClosure_FollowsGeneratorAndPrivateEdgesInAnyTargetFramework()
        {
            Lib("Gen");
            Lib("GenHelper");
            Lib("Priv");
            Lib("MaybeNet472");
            Lib("Normal");
            Write("src/Gen/Gen.csproj", Project("<ProjectReference Include=\"../GenHelper/GenHelper.csproj\" />"));
            var lib = Write("src/Lib/Lib.csproj",
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFrameworks>net472;net8.0</TargetFrameworks></PropertyGroup><ItemGroup>" +
                "<ProjectReference Include=\"../Gen/Gen.csproj\" OutputItemType=\"Analyzer\" ReferenceOutputAssembly=\"false\" />" +
                "<ProjectReference Include=\"../Priv/Priv.csproj\" PrivateAssets=\"all\" />" +
                "<ProjectReference Include=\"../Normal/Normal.csproj\" />" +
                "<ProjectReference Include=\"../MaybeNet472/MaybeNet472.csproj\" ReferenceOutputAssembly=\"false\" Condition=\"'$(TargetFramework)' == 'net472'\" />" +
                "</ItemGroup></Project>");

            var added = ProjectReferenceReader.GetUnflowedReferenceClosure(new[] { lib })
                .Select(p => Path.GetFileNameWithoutExtension(p))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();
            // Normal is NuGet's job; MaybeNet472 applies to one target framework, and a version is per project
            Assert.Equal(new[] { "Gen", "GenHelper", "MaybeNet472", "Priv" }, added);
        }

        [Fact]
        public void ProjectDiscovery_SkipsNestedRepositoriesAndLinkedDirectories()
        {
            Lib("Core");
            Write(".worktrees/wt/.git", "gitdir: /somewhere/else");
            Write(".worktrees/wt/src/Core/Core.csproj", "<Project />");
            Write("ext/sub/.git", "gitdir: ../../.git/modules/sub");
            Write("ext/sub/src/SubLib/SubLib.csproj", "<Project />");
            Directory.CreateDirectory(Path.Combine(_root, "tools"));
            try
            {
                Directory.CreateSymbolicLink(Path.Combine(_root, "tools", "CoreLink"), Path.Combine(_root, "src", "Core"));
                Directory.CreateSymbolicLink(Path.Combine(_root, "src", "loop"), _root);
            }
            catch (Exception)
            {
                // Creating links can require privileges on Windows; the nested-repository checks still run
            }

            var found = ProjectDiscovery.FindProjectFiles(_root);
            Assert.Single(found);
            Assert.EndsWith(Path.Combine("src", "Core", "Core.csproj"), found[0]);
        }
    }
}
