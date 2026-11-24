using System;
using System.Collections.Generic;
using System.IO;
using Mister.Version.Core.Models;
using Mister.Version.Core.Services;
using Xunit;

namespace Mister.Version.Tests
{
    /// <summary>
    /// IsTestProject / IsPackable detection used by the CLI (version, report, changelog) must match what MSBuild
    /// evaluates and passes to the MSBuild task.
    /// </summary>
    public class ProjectTypeDetectionTests : IDisposable
    {
        private readonly string _root;

        public ProjectTypeDetectionTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "mrv-projtype-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch
            {
                // best effort
            }
        }

        private string Write(string relative, string content)
        {
            var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
            return path;
        }

        private static string Project(string body) =>
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>" + body + "</Project>";

        [Fact]
        public void ConsoleApp_IsPackableByDefault()
        {
            // The SDK default is IsPackable=true for every project type, console apps included
            var content = Project("<PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>");

            Assert.True(ProjectAnalyzer.IsPackable(content));
            Assert.False(ProjectAnalyzer.IsTestProject(content));
        }

        [Fact]
        public void ExplicitIsPackableFalse_IsNotPackable()
        {
            var content = Project("<PropertyGroup><OutputType>Exe</OutputType><IsPackable>false</IsPackable></PropertyGroup>");

            Assert.False(ProjectAnalyzer.IsPackable(content));
        }

        [Theory]
        [InlineData("xunit")]
        [InlineData("xunit.v3")]
        [InlineData("NUnit")]
        [InlineData("MSTest.TestFramework")]
        public void LibraryReferencingOnlyATestFramework_IsAPackableLibrary(string package)
        {
            // e.g. a shared test-helpers package: test framework packages do not set IsTestProject
            var content = Project($"<ItemGroup><PackageReference Include=\"{package}\" Version=\"1.0.0\" /></ItemGroup>");

            Assert.False(ProjectAnalyzer.IsTestProject(content));
            Assert.True(ProjectAnalyzer.IsPackable(content));
        }

        [Fact]
        public void TestSdkReference_IsANonPackableTestProject()
        {
            // Microsoft.NET.Test.Sdk's build props set IsTestProject=true; NuGet then defaults IsPackable to false
            var content = Project("<ItemGroup><PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.0.0\" /><PackageReference Include=\"xunit\" Version=\"2.9.0\" /></ItemGroup>");

            Assert.True(ProjectAnalyzer.IsTestProject(content));
            Assert.False(ProjectAnalyzer.IsPackable(content));
        }

        [Fact]
        public void ExplicitIsTestProject_IsATestProject_AndExplicitIsPackableWins()
        {
            Assert.True(ProjectAnalyzer.IsTestProject(Project("<PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup>")));
            Assert.False(ProjectAnalyzer.IsPackable(Project("<PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup>")));
            Assert.True(ProjectAnalyzer.IsPackable(Project("<PropertyGroup><IsTestProject>true</IsTestProject><IsPackable>true</IsPackable></PropertyGroup>")));
        }

        [Fact]
        public void IsTestProjectFalseInProjectBody_OverridesTestSdk()
        {
            // The project body is evaluated after the package props
            var content = Project("<PropertyGroup><IsTestProject>false</IsTestProject></PropertyGroup><ItemGroup><PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.0.0\" /></ItemGroup>");

            Assert.False(ProjectAnalyzer.IsTestProject(content));
        }

        [Fact]
        public void ConditionalPropertiesAndItems_AreIgnored()
        {
            var content = Project(
                "<PropertyGroup Condition=\"'$(Configuration)' == 'Release'\"><IsPackable>false</IsPackable></PropertyGroup>" +
                "<PropertyGroup><IsTestProject Condition=\"'$(Foo)' == 'true'\">true</IsTestProject></PropertyGroup>" +
                "<ItemGroup Condition=\"'$(Foo)' == 'true'\"><PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.0.0\" /></ItemGroup>");

            Assert.True(ProjectAnalyzer.IsPackable(content));
            Assert.False(ProjectAnalyzer.IsTestProject(content));
        }

        [Fact]
        public void PropertyValuesAreCaseInsensitive_AndLastValueWins()
        {
            var content = Project("<PropertyGroup><ISPACKABLE> False </ISPACKABLE></PropertyGroup><PropertyGroup><IsPackable>true</IsPackable></PropertyGroup>");
            Assert.True(ProjectAnalyzer.IsPackable(content));

            content = Project("<PropertyGroup><IsPackable>true</IsPackable></PropertyGroup><PropertyGroup><ispackable>FALSE</ispackable></PropertyGroup>");
            Assert.False(ProjectAnalyzer.IsPackable(content));
        }

        [Fact]
        public void LegacyProjectWithXmlNamespace_IsUnderstood()
        {
            var content = "<?xml version=\"1.0\" encoding=\"utf-8\"?><Project ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">" +
                          "<PropertyGroup><IsPackable>false</IsPackable></PropertyGroup></Project>";

            Assert.False(ProjectAnalyzer.IsPackable(content));
        }

        [Fact]
        public void MalformedXml_FallsBackToPatternMatching()
        {
            var content = "<Project><PropertyGroup><IsPackable>false</IsPackable></PropertyGroup><ItemGroup><PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"1\"></ItemGroup>";

            Assert.False(ProjectAnalyzer.IsPackable(content));
            Assert.True(ProjectAnalyzer.IsTestProject(content));
        }

        [Fact]
        public void DirectoryBuildProps_IsPackableFalse_AppliesToProjectsBelowIt()
        {
            Write("src/Tools/Directory.Build.props", "<Project><PropertyGroup><IsPackable>false</IsPackable></PropertyGroup></Project>");
            var tool = Write("src/Tools/Tool/Tool.csproj", Project(""));
            var overridden = Write("src/Tools/Packed/Packed.csproj", Project("<PropertyGroup><IsPackable>true</IsPackable></PropertyGroup>"));
            var lib = Write("src/Lib/Lib.csproj", Project(""));

            Assert.False(ProjectAnalyzer.DetectProjectType(tool, _root).IsPackable);
            Assert.True(ProjectAnalyzer.DetectProjectType(overridden, _root).IsPackable);
            Assert.True(ProjectAnalyzer.DetectProjectType(lib, _root).IsPackable);
        }

        [Fact]
        public void DirectoryBuildProps_TestSdkReferenceOrIsTestProject_MakesTestProjects()
        {
            Write("tests/Directory.Build.props", "<Project><ItemGroup><PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.0.0\" /></ItemGroup></Project>");
            var test = Write("tests/Lib.Tests/Lib.Tests.csproj", Project(""));

            var type = ProjectAnalyzer.DetectProjectType(test, _root);
            Assert.True(type.IsTestProject);
            Assert.False(type.IsPackable);

            Write("spec/Directory.Build.props", "<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>");
            var spec = Write("spec/Lib.Spec/Lib.Spec.csproj", Project(""));
            Assert.True(ProjectAnalyzer.DetectProjectType(spec, _root).IsTestProject);
        }

        [Fact]
        public void DirectoryBuildProps_OnlyTheNearestApplies_UnlessItImportsItsParent()
        {
            Write("Directory.Build.props", "<Project><PropertyGroup><IsPackable>false</IsPackable></PropertyGroup></Project>");

            // Nearest props without an import: the root props is not imported by MSBuild
            Write("a/Directory.Build.props", "<Project><PropertyGroup><Authors>me</Authors></PropertyGroup></Project>");
            var notChained = Write("a/P/P.csproj", Project(""));
            Assert.True(ProjectAnalyzer.DetectProjectType(notChained, _root).IsPackable);

            // Nearest props imports its parent: both apply, the nearer one last
            Write("b/Directory.Build.props",
                "<Project><Import Project=\"$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))\" />" +
                "<PropertyGroup><Authors>me</Authors></PropertyGroup></Project>");
            var chained = Write("b/P/P.csproj", Project(""));
            Assert.False(ProjectAnalyzer.DetectProjectType(chained, _root).IsPackable);

            Write("c/Directory.Build.props",
                "<Project><Import Project=\"../Directory.Build.props\" /><PropertyGroup><IsPackable>true</IsPackable></PropertyGroup></Project>");
            var chainedOverride = Write("c/P/P.csproj", Project(""));
            Assert.True(ProjectAnalyzer.DetectProjectType(chainedOverride, _root).IsPackable);

            Assert.Equal(new List<string> { Path.Combine(_root, "Directory.Build.props"), Path.Combine(_root, "b", "Directory.Build.props") },
                ProjectAnalyzer.GetDirectoryBuildPropsChain(chained, _root));
        }

        [Fact]
        public void DirectoryBuildProps_AboveTheRepositoryRoot_IsIgnored()
        {
            Write("Directory.Build.props", "<Project><PropertyGroup><IsPackable>false</IsPackable></PropertyGroup></Project>");
            var project = Write("repo/src/P/P.csproj", Project(""));

            Assert.True(ProjectAnalyzer.DetectProjectType(project, Path.Combine(_root, "repo")).IsPackable);
            Assert.False(ProjectAnalyzer.DetectProjectType(project, _root).IsPackable);
        }

        [Fact]
        public void AnalyzeProjectWith_PassesTheDetectedTypeAndTransitiveDependencies()
        {
            var core = Write("src/Core/Core.csproj", Project(""));
            var lib = Write("src/Lib/Lib.csproj", Project("<ItemGroup><ProjectReference Include=\"..\\Core\\Core.csproj\" /></ItemGroup>"));
            var app = Write("src/App/App.csproj", Project("<PropertyGroup><OutputType>Exe</OutputType></PropertyGroup><ItemGroup><ProjectReference Include=\"../Lib/Lib.csproj\" /></ItemGroup>"));

            var analyzer = new ProjectAnalyzer(null, null);
            ProjectTypeInfo seenType = null;
            List<string> seenDependencies = null;
            var info = analyzer.AnalyzeProjectWith(app, _root, (path, type, dependencies) =>
            {
                seenType = type;
                seenDependencies = dependencies;
                return new VersionResult { Version = "1.2.3" };
            });

            Assert.True(seenType.IsPackable);
            Assert.False(seenType.IsTestProject);
            Assert.Equal(new[] { Path.GetFullPath(lib), Path.GetFullPath(core) }, seenDependencies);
            Assert.Equal("1.2.3", info.Version.Version);
            Assert.Equal(new[] { "Lib" }, info.DirectDependencies);
            Assert.True(info.IsPackable);
        }
    }
}
