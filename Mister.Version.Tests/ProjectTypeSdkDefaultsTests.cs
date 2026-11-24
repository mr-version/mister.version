using System;
using System.IO;
using LibGit2Sharp;
using Mister.Version.Core.Services;
using Xunit;

namespace Mister.Version.Tests
{
    /// <summary>
    /// The CLI's static project-type detection matches what MSBuild evaluates for SDK defaults, and projects that
    /// are not versioned report the version they are actually built with.
    /// </summary>
    [Collection("VersionResultCache")]
    public class ProjectTypeSdkDefaultsTests : IDisposable
    {
        private readonly string _root;

        public ProjectTypeSdkDefaultsTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "mrv-sdkdefaults-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try
            {
                foreach (var f in Directory.GetFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(_root, true);
            }
            catch { }
        }

        private string Write(string relative, string content)
        {
            var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
            return path;
        }

        [Theory]
        [InlineData("<Project Sdk=\"Microsoft.NET.Sdk.Web\" />", false)]
        [InlineData("<Project Sdk=\"Microsoft.NET.Sdk.Worker\" />", false)]
        [InlineData("<Project Sdk=\"Microsoft.NET.Sdk.BlazorWebAssembly\" />", false)]
        [InlineData("<Project><Sdk Name=\"Microsoft.NET.Sdk.Web\" /></Project>", false)]
        [InlineData("<Project><Import Project=\"Sdk.props\" Sdk=\"Microsoft.NET.Sdk.Worker\" /></Project>", false)]
        [InlineData("<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><IsPackable>true</IsPackable></PropertyGroup></Project>", true)]
        [InlineData("<Project Sdk=\"Microsoft.NET.Sdk.Razor\" />", true)]
        [InlineData("<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>", true)]
        public void SdkIsPackableDefaults(string project, bool packable)
        {
            var path = Write("src/App/App.csproj", project);
            Assert.Equal(packable, ProjectAnalyzer.DetectProjectType(path, _root).IsPackable);
        }

        [Fact]
        public void WebProject_MadePackableInDirectoryBuildProps_IsPackable()
        {
            Write("Directory.Build.props", "<Project><PropertyGroup><IsPackable>true</IsPackable></PropertyGroup></Project>");
            var path = Write("src/Web/Web.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />");
            Assert.True(ProjectAnalyzer.DetectProjectType(path, _root).IsPackable);
        }

        [Theory]
        [InlineData("<Version>5.0.0</Version>", "5.0.0")]
        [InlineData("<VersionPrefix>2.5.0</VersionPrefix><VersionSuffix>beta</VersionSuffix>", "2.5.0-beta")]
        [InlineData("<VersionPrefix>2.5.0</VersionPrefix>", "2.5.0")]
        [InlineData("<Version>$(SomethingElse)</Version>", null)]
        [InlineData("", null)]
        public void ProjectVersion_IsReadFromTheProject(string properties, string expected)
        {
            var path = Write("src/Tool/Tool.csproj", $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>{properties}</PropertyGroup></Project>");
            Assert.Equal(expected, ProjectAnalyzer.DetectProjectType(path, _root).ProjectVersion);
        }

        [Fact]
        public void NonPackableProject_ReportsItsOwnVersion_NotOneZeroZero()
        {
            Repository.Init(_root);
            using var repo = new Repository(_root);
            var sig = new Signature("T", "t@e.com", DateTimeOffset.Now);
            var tool = Write("src/Internal/Internal.csproj",
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><IsPackable>false</IsPackable><Version>5.0.0</Version></PropertyGroup></Project>");
            Commands.Stage(repo, "*");
            repo.Commit("init", sig, sig);
            repo.ApplyTag("v2.0.0");

            var type = ProjectAnalyzer.DetectProjectType(tool, _root);
            using var service = new VersioningService(_root, (level, message) => { });
            var result = service.CalculateProjectVersion(new VersioningRequest
            {
                RepoRoot = _root,
                ProjectPath = tool,
                IsPackable = type.IsPackable,
                IsTestProject = type.IsTestProject,
                ProjectVersion = type.ProjectVersion,
                Dependencies = new System.Collections.Generic.List<string>()
            });

            Assert.True(result.Skipped);
            Assert.Equal("5.0.0", result.Version);
            Assert.Contains("keeps its own version", result.ChangeReason);
        }
    }
}
