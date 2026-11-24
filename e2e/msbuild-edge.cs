#:property TargetFramework=net10.0
#:property Nullable=enable
#:property ImplicitUsings=enable
#:property PublishAot=false

// Edge-case end-to-end tests for the Mister.Version MSBuild integration and CLI parity.
//
// Each scenario builds a small throwaway git repository with the package packed from this repository and
// checks the versions that end up in assemblies/packages, and that `mr-version` (which CI tagging and reports
// use) computes the same versions. Scenarios cover project layouts and SDK features found to break versioning:
// multi-targeting outer builds, source-generator / PrivateAssets references, references written with MSBuild
// properties or added by Directory.Build.targets, Web SDK projects, duplicate project names, submodules,
// symbolic links, nested worktrees, numeric feature branches, non-packable projects and references outside
// the repository.
//
//     dotnet run e2e/msbuild-edge.cs                 (from the repository root)
//     dotnet run e2e/msbuild-edge.cs -- --keep       (keep the temp directory)
//     dotnet run e2e/msbuild-edge.cs -- --only web   (scenarios whose name contains "web")
//
// Exit code is the number of failed checks.

using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

var keep = args.Contains("--keep");
var onlyIdx = Array.IndexOf(args, "--only");
var only = onlyIdx >= 0 && onlyIdx + 1 < args.Length ? args[onlyIdx + 1] : null;
var repoRoot = FindRepoRoot(Environment.CurrentDirectory) ?? throw new InvalidOperationException("Run from inside the mister.version repository.");
var work = Path.Combine(Path.GetTempPath(), "mrv-edge-" + Guid.NewGuid().ToString("N")[..8]);
var feed = Path.Combine(work, "feed");
Directory.CreateDirectory(feed);
var pkgVersion = $"0.0.0-edge.{DateTime.UtcNow:yyyyMMddHHmmss}";
var cli = Path.Combine(repoRoot, "Mister.Version.CLI", "bin", "Release", "net8.0", "mr-version.dll");
var failures = new List<string>();
var isWindows = OperatingSystem.IsWindows();

Console.WriteLine($"Repo: {repoRoot}\nWork: {work}\nOS/arch: {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}");

try
{
    Step("Pack Mister.Version and build the CLI");
    var pack = Dotnet(repoRoot, "pack", Path.Combine("Mister.Version", "Mister.Version.csproj"), "-c", "Release", "-o", feed,
        $"-p:Version={pkgVersion}", $"-p:PackageVersion={pkgVersion}", "-p:MonoRepoVersioningEnabled=false");
    Require(pack.ExitCode == 0, "pack Mister.Version", pack.Output);
    var cliBuild = Dotnet(repoRoot, "build", Path.Combine("Mister.Version.CLI", "Mister.Version.CLI.csproj"), "-c", "Release", "-nologo", "-v:q");
    Require(cliBuild.ExitCode == 0, "build the CLI", cliBuild.Output);

    Scenario("outer-build", () =>
    {
        var r = NewRepo("outer");
        Proj(r, "src/Multi/Multi.csproj", tfm: "net8.0;net10.0");
        File.WriteAllText(Path.Combine(r, "Directory.Build.targets"),
            "<Project><Target Name=\"WriteOuterVersion\" AfterTargets=\"Build\" Condition=\"'$(TargetFramework)' == ''\">" +
            "<WriteLinesToFile File=\"$(MSBuildProjectDirectory)/obj/outer-version.txt\" Lines=\"$(Version)\" Overwrite=\"true\" />" +
            "</Target></Project>");
        Commit(r, "init"); Tag(r, "v2.3.4"); Touch(r, "src/Multi/C.cs"); Commit(r, "fix: multi");
        Require(Build(r).ExitCode == 0, "build", Build(r).Output);
        var outer = File.Exists(Path.Combine(r, "src/Multi/obj/outer-version.txt")) ? File.ReadAllText(Path.Combine(r, "src/Multi/obj/outer-version.txt")).Trim() : "<missing>";
        Check(Info(r, "src/Multi", "net10.0") == "2.3.5", $"inner builds stamp 2.3.5 (got {Info(r, "src/Multi", "net10.0")})");
        Check(outer == "2.3.5", $"outer build sees $(Version) 2.3.5 after Build (got {outer})");
    });

    Scenario("generator-and-private-references", () =>
    {
        var r = NewRepo("gen");
        Proj(r, "src/Gen/Gen.csproj", tfm: "netstandard2.0");
        Proj(r, "src/Lib/Lib.csproj", items: "<ProjectReference Include=\"../Gen/Gen.csproj\" OutputItemType=\"Analyzer\" ReferenceOutputAssembly=\"false\" />");
        Proj(r, "src/App/App.csproj", items: "<ProjectReference Include=\"../Lib/Lib.csproj\" />");
        Proj(r, "src/Priv/Priv.csproj");
        Proj(r, "src/B/B.csproj", items: "<ProjectReference Include=\"../Priv/Priv.csproj\" PrivateAssets=\"all\" />");
        Proj(r, "src/TopB/TopB.csproj", items: "<ProjectReference Include=\"../B/B.csproj\" />");
        Commit(r, "init"); Tag(r, "v1.0.0");
        Touch(r, "src/Gen/C.cs"); Touch(r, "src/Priv/C.cs"); Commit(r, "fix: generator and private dependency");
        var build = Build(r);
        Require(build.ExitCode == 0, "build", build.Output);
        foreach (var p in new[] { "Gen", "Lib", "App", "Priv", "B", "TopB" })
        {
            var tfm = p == "Gen" ? "netstandard2.0" : "net10.0";
            Check(Info(r, $"src/{p}", tfm) == "1.0.1", $"{p} is 1.0.1 (got {Info(r, $"src/{p}", tfm)})");
            Check(CliVersion(r, $"src/{p}/{p}.csproj") == "1.0.1", $"CLI: {p} is 1.0.1 (got {CliVersion(r, $"src/{p}/{p}.csproj")})");
        }
    });

    Scenario("property-based-references", () =>
    {
        var r = NewRepo("props");
        File.WriteAllText(Path.Combine(r, "Directory.Build.props"), File.ReadAllText(Path.Combine(r, "Directory.Build.props"))
            .Replace("<Project>", "<Project><PropertyGroup><RepoRoot>$(MSBuildThisFileDirectory)</RepoRoot></PropertyGroup>"));
        File.WriteAllText(Path.Combine(r, "Directory.Build.targets"),
            "<Project><ItemGroup Condition=\"'$(MSBuildProjectName)' == 'App'\"><ProjectReference Include=\"$(RepoRoot)src/Lib5/Lib5.csproj\" /></ItemGroup></Project>");
        for (var i = 1; i <= 5; i++) Proj(r, $"src/Lib{i}/Lib{i}.csproj");
        Proj(r, "src/App/App.csproj", items:
            "<ProjectReference Include=\"$(MSBuildThisFileDirectory)../Lib1/Lib1.csproj\" />" +
            "<ProjectReference Include=\"$(RepoRoot)src/Lib2/Lib2.csproj\" />" +
            "<ProjectReference Include=\"../Lib3/Lib3.csproj;../Lib4/Lib4.csproj\" />");
        Commit(r, "init");
        for (var i = 1; i <= 5; i++)
        {
            Tag(r, $"v1.{i}.0");
            Touch(r, $"src/Lib{i}/C.cs"); Commit(r, $"fix: lib{i}");
            var build = Build(r, "src/App/App.csproj");
            Require(build.ExitCode == 0, "build App", build.Output);
            Check(Info(r, "src/App", "net10.0") == $"1.{i}.1", $"Lib{i} change bumps App to 1.{i}.1 (got {Info(r, "src/App", "net10.0")})");
            Check(CliVersion(r, "src/App/App.csproj") == $"1.{i}.1", $"CLI: Lib{i} change bumps App to 1.{i}.1 (got {CliVersion(r, "src/App/App.csproj")})");
        }
    });

    Scenario("web-sdk", () =>
    {
        var r = NewRepo("web");
        Proj(r, "src/Core/Core.csproj");
        Proj(r, "src/Web/Web.csproj", sdk: "Microsoft.NET.Sdk.Web", items: "<ProjectReference Include=\"../Core/Core.csproj\" />",
            code: "var app = Microsoft.AspNetCore.Builder.WebApplication.Create(); app.Run();");
        Commit(r, "init"); Tag(r, "v2.0.0"); Touch(r, "src/Core/C.cs"); Commit(r, "fix: core");
        var build = Build(r);
        Require(build.ExitCode == 0, "build", build.Output);
        Check(Info(r, "src/Core", "net10.0") == "2.0.1", $"Core is 2.0.1 (got {Info(r, "src/Core", "net10.0")})");
        // Web SDK projects are not packable by default, so the build does not version them
        Check(Info(r, "src/Web", "net10.0") == "1.0.0", $"Web keeps its own version 1.0.0 (got {Info(r, "src/Web", "net10.0")})");
        Check(CliVersion(r, "src/Web/Web.csproj") == "1.0.0", $"CLI agrees Web is not versioned (got {CliVersion(r, "src/Web/Web.csproj")})");
    });

    Scenario("duplicate-project-names", () =>
    {
        var r = NewRepo("dup");
        Proj(r, "src/a/Utils/Utils.csproj", props: "<PackageId>A.Utils</PackageId><AssemblyName>A.Utils</AssemblyName>");
        Proj(r, "src/b/Helper/Helper.csproj");
        Proj(r, "src/b/Utils/Utils.csproj", props: "<PackageId>B.Utils</PackageId><AssemblyName>B.Utils</AssemblyName>",
            items: "<ProjectReference Include=\"../Helper/Helper.csproj\" />");
        Commit(r, "init"); Tag(r, "v1.0.0"); Touch(r, "src/b/Helper/C.cs"); Commit(r, "fix: helper");
        // (a solution cannot hold two projects with the same name: build them one by one)
        foreach (var project in new[] { "src/a/Utils/Utils.csproj", "src/b/Utils/Utils.csproj" })
        {
            var build = Build(r, project);
            Require(build.ExitCode == 0, $"build {project}", build.Output);
        }
        Check(Info(r, "src/a/Utils", "net10.0", "A.Utils") == "1.0.0", $"a/Utils stays 1.0.0 (got {Info(r, "src/a/Utils", "net10.0", "A.Utils")})");
        Check(Info(r, "src/b/Utils", "net10.0", "B.Utils") == "1.0.1", $"b/Utils is 1.0.1 (got {Info(r, "src/b/Utils", "net10.0", "B.Utils")})");
        Check(CliVersion(r, "src/a/Utils/Utils.csproj") == "1.0.0", "CLI: a/Utils 1.0.0");
        Check(CliVersion(r, "src/b/Utils/Utils.csproj") == "1.0.1", "CLI: b/Utils 1.0.1");
        var report = Cli(r, "report", "-r", ".", "-o", "json");
        Check(report.ExitCode == 0 && report.Output.Contains("src/a/Utils") && report.Output.Contains("src/b/Utils"), "CLI report lists both Utils projects");
    });

    Scenario("submodule-reference", () =>
    {
        var lib = Path.Combine(work, "sublib");
        Directory.CreateDirectory(lib);
        InitGit(lib);
        Proj(lib, "src/SubLib/SubLib.csproj", feedless: true);
        Commit(lib, "lib init");

        var r = NewRepo("super");
        Proj(r, "src/App/App.csproj", items: "<ProjectReference Include=\"../../ext/sub/src/SubLib/SubLib.csproj\" />");
        Git(r, "-c", "protocol.file.allow=always", "submodule", "add", "-q", lib, "ext/sub");
        Commit(r, "init with submodule"); Tag(r, "v1.0.0");
        Touch(lib, "src/SubLib/C.cs"); Commit(lib, "lib change");
        Git(Path.Combine(r, "ext", "sub"), "-c", "protocol.file.allow=always", "pull", "-q", lib, "HEAD");
        Commit(r, "bump submodule");
        var build = Build(r, "src/App/App.csproj");
        Require(build.ExitCode == 0, "build App", build.Output);
        Check(Info(r, "src/App", "net10.0") == "1.0.1", $"moving the submodule pointer bumps App to 1.0.1 (got {Info(r, "src/App", "net10.0")})");
        Check(CliVersion(r, "src/App/App.csproj") == "1.0.1", $"CLI: App 1.0.1 (got {CliVersion(r, "src/App/App.csproj")})");
        var report = Cli(r, "report", "-r", ".", "-o", "csv");
        Check(report.ExitCode == 0 && !report.Output.Split('\n').Any(line => line.StartsWith("SubLib,")), "CLI report skips projects inside the submodule");
    });

    if (!isWindows)
    {
        Scenario("symlink-into-repository", () =>
        {
            var r = NewRepo("link");
            Proj(r, "src/Leaf/Leaf.csproj");
            Proj(r, "src/Other/Other.csproj");
            Commit(r, "init"); Tag(r, "v1.0.0"); Touch(r, "src/Other/C.cs"); Commit(r, "fix: other");
            var link = Path.Combine(work, "leaflink");
            Directory.CreateSymbolicLink(link, Path.Combine(r, "src", "Leaf"));
            // Directory.Build.props is not visible above the link: reference the package from the project
            File.WriteAllText(Path.Combine(r, "src/Leaf/Leaf.csproj"), File.ReadAllText(Path.Combine(r, "src/Leaf/Leaf.csproj"))
                .Replace("</Project>", $"<ItemGroup><PackageReference Include=\"Mister.Version\" Version=\"{pkgVersion}\" PrivateAssets=\"all\" /></ItemGroup></Project>"));
            File.WriteAllText(Path.Combine(r, "Directory.Build.props"), "<Project />");
            Commit(r, "standalone leaf");
            Tag(r, "v1.1.0"); Touch(r, "src/Other/C.cs"); Commit(r, "fix: other again");
            File.Copy(Path.Combine(r, "nuget.config"), Path.Combine(work, "nuget.config"), overwrite: true);
            var build = Dotnet(work, "build", Path.Combine(link, "Leaf.csproj"), "-c", "Release", "-nologo");
            Require(build.ExitCode == 0, "build through the link", build.Output);
            Check(Info(r, "src/Leaf", "net10.0") == "1.1.0", $"Leaf built through a link is 1.1.0 like the real path (got {Info(r, "src/Leaf", "net10.0")})");
        });
    }

    Scenario("nested-worktree", () =>
    {
        var r = NewRepo("wt");
        Proj(r, "src/Core/Core.csproj");
        Proj(r, "src/App/App.csproj", items: "<ProjectReference Include=\"../Core/Core.csproj\" />");
        File.AppendAllText(Path.Combine(r, ".gitignore"), ".worktrees/\n");
        Commit(r, "init"); Tag(r, "v1.0.0");
        Git(r, "worktree", "add", "-q", "-b", "feature/wt", ".worktrees/wt");
        Touch(r, "src/Core/C.cs"); Commit(r, "fix: core");
        Check(CliVersion(r, "src/App/App.csproj") == "1.0.1", $"CLI: App 1.0.1 with a worktree nested in the checkout (got {CliVersion(r, "src/App/App.csproj")})");
        var report = Cli(r, "report", "-r", ".", "-o", "csv");
        Check(report.ExitCode == 0, "CLI report succeeds with a nested worktree");
        var build = Build(r, "src/App/App.csproj");
        Require(build.ExitCode == 0, "build App", build.Output);
        Check(Info(r, "src/App", "net10.0") == "1.0.1", $"App is 1.0.1 (got {Info(r, "src/App", "net10.0")})");
    });

    Scenario("numeric-feature-branch-pack", () =>
    {
        var r = NewRepo("numeric");
        Proj(r, "src/Core/Core.csproj");
        Commit(r, "init"); Tag(r, "v1.0.0");
        Git(r, "checkout", "-q", "-b", "feature/0042");
        Touch(r, "src/Core/C.cs"); Commit(r, "fix: ticket 42");
        var outDir = Path.Combine(r, "out");
        var packResult = Dotnet(r, "pack", "src/Core/Core.csproj", "-c", "Release", "-o", outDir, "-nologo");
        Check(packResult.ExitCode == 0, "pack succeeds on feature/0042");
        Check(Nuspec(outDir, "Core") == "1.0.1-42.1", $"package version is valid SemVer 1.0.1-42.1 (got {Nuspec(outDir, "Core")})");
    });

    Scenario("non-packable-own-version", () =>
    {
        var r = NewRepo("nonpack");
        Proj(r, "src/Internal/Internal.csproj", props: "<IsPackable>false</IsPackable><Version>5.0.0</Version>");
        Proj(r, "src/Lib/Lib.csproj", items: "<ProjectReference Include=\"../Internal/Internal.csproj\" />");
        Commit(r, "init"); Tag(r, "v2.0.0"); Touch(r, "src/Lib/C.cs"); Commit(r, "fix: lib");
        var build = Build(r);
        Require(build.ExitCode == 0, "build", build.Output);
        Check(Info(r, "src/Internal", "net10.0") == "5.0.0", $"Internal keeps 5.0.0 (got {Info(r, "src/Internal", "net10.0")})");
        Check(CliVersion(r, "src/Internal/Internal.csproj") == "5.0.0", $"CLI reports Internal 5.0.0 (got {CliVersion(r, "src/Internal/Internal.csproj")})");
    });

    Scenario("reference-outside-repository", () =>
    {
        var r = NewRepo("outside");
        var ext = Path.Combine(work, "outside-ext");
        Proj(ext, "Ext/Ext.csproj", feedless: true);
        Proj(r, "src/App/App.csproj", items: $"<ProjectReference Include=\"{Path.Combine(ext, "Ext", "Ext.csproj")}\" />");
        Commit(r, "init"); Tag(r, "v1.0.0"); Touch(r, "src/App/C.cs"); Commit(r, "fix: app");
        var build = Build(r, "src/App/App.csproj");
        Require(build.ExitCode == 0, "build", build.Output);
        Check(build.Output.Contains("outside the repository"), "a warning says the reference outside the repository is not tracked");
        Check(Info(r, "src/App", "net10.0") == "1.0.1", $"App is 1.0.1 (got {Info(r, "src/App", "net10.0")})");
    });
}
catch (FatalException ex)
{
    failures.Add(ex.Message);
    Console.WriteLine($"  FATAL {ex.Message}");
}
finally
{
    Dotnet(work, "build-server", "shutdown");
    if (keep) Console.WriteLine($"Kept {work}");
    else TryDelete(work);
}

Console.WriteLine();
Console.WriteLine(failures.Count == 0 ? "EDGE: ALL CHECKS PASSED" : $"EDGE: {failures.Count} FAILED");
foreach (var f in failures) Console.WriteLine($"  - {f}");
return failures.Count;

// ============================================================================ helpers

void Scenario(string name, Action body)
{
    if (only != null && !name.Contains(only, StringComparison.OrdinalIgnoreCase)) return;
    Step(name);
    var sw = Stopwatch.StartNew();
    try { body(); }
    catch (FatalException ex) { failures.Add($"[{name}] {ex.Message}"); Console.WriteLine($"  FAIL  {ex.Message}"); }
    Console.WriteLine($"  ({sw.Elapsed.TotalSeconds:F1}s)");
}

string NewRepo(string name)
{
    var r = Path.Combine(work, name);
    Directory.CreateDirectory(r);
    File.WriteAllText(Path.Combine(r, "nuget.config"),
        $"<configuration><packageSources><clear /><add key=\"local\" value=\"{feed}\" /><add key=\"nuget\" value=\"https://api.nuget.org/v3/index.json\" /></packageSources></configuration>");
    File.WriteAllText(Path.Combine(r, "Directory.Build.props"),
        $"<Project><ItemGroup><PackageReference Include=\"Mister.Version\" Version=\"{pkgVersion}\" PrivateAssets=\"all\" /></ItemGroup></Project>");
    File.WriteAllText(Path.Combine(r, ".gitignore"), "bin/\nobj/\nout/\n*.slnx\n");
    InitGit(r);
    return r;
}

void Proj(string root, string rel, string sdk = "Microsoft.NET.Sdk", string tfm = "net10.0", string props = "", string items = "", string? code = null, bool feedless = false)
{
    var path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var tfmProperty = tfm.Contains(';') ? $"<TargetFrameworks>{tfm}</TargetFrameworks>" : $"<TargetFramework>{tfm}</TargetFramework>";
    File.WriteAllText(path, $"<Project Sdk=\"{sdk}\"><PropertyGroup>{tfmProperty}<LangVersion>latest</LangVersion>{props}</PropertyGroup><ItemGroup>{items}</ItemGroup></Project>");
    var name = Path.GetFileNameWithoutExtension(path).Replace('.', '_');
    File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, sdk.EndsWith(".Web") ? "Program.cs" : "C.cs"),
        code ?? $"namespace N_{name}_{Guid.NewGuid():N} {{ public static class C {{ public static int V => 1; }} }}");
    _ = feedless;
}

void Touch(string root, string rel) => File.AppendAllText(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)), $"\n// change {Guid.NewGuid():N}\n");

void Commit(string root, string message) { Git(root, "add", "-A"); Git(root, "commit", "-q", "--allow-empty", "-m", message); }

void Tag(string root, string tag) => Git(root, "tag", tag);

void InitGit(string root)
{
    Git(root, "init", "-q");
    Git(root, "checkout", "-q", "-b", "main");
    Git(root, "config", "user.email", "edge@example.com");
    Git(root, "config", "user.name", "edge");
    Git(root, "config", "commit.gpgsign", "false");
    Git(root, "config", "tag.gpgsign", "false");
}

(int ExitCode, string Output) Build(string root, string? project = null)
{
    if (project != null)
        return Dotnet(root, "build", project, "-c", "Release", "-nologo");

    // A solution with every project of the repository (not those in worktrees or nested repositories)
    var projects = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
        .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'))
        .Where(p => !p.StartsWith(".worktrees/") && !p.StartsWith("ext/") && !p.Contains("/bin/") && !p.Contains("/obj/"))
        .OrderBy(p => p, StringComparer.Ordinal);
    File.WriteAllText(Path.Combine(root, "All.slnx"),
        "<Solution>" + string.Concat(projects.Select(p => $"<Project Path=\"{p}\" />")) + "</Solution>");
    return Dotnet(root, "build", "All.slnx", "-c", "Release", "-nologo");
}

(int ExitCode, string Output) Cli(string root, params string[] arguments) => CliRun(root, arguments, stdoutOnly: false);

(int ExitCode, string Output) CliRun(string root, string[] arguments, bool stdoutOnly)
{
    var all = new List<string> { cli };
    all.AddRange(arguments);
    var r = Run(root, "dotnet", all.ToArray(), rollForward: true);
    return (r.ExitCode, stdoutOnly ? r.Stdout : r.Output);
}

string CliVersion(string root, string project)
{
    var r = CliRun(root, new[] { "version", "-r", ".", "-p", project, "-j" }, stdoutOnly: true);
    if (r.ExitCode != 0) return $"<cli exit {r.ExitCode}>";
    try
    {
        using var doc = JsonDocument.Parse(r.Output);
        return doc.RootElement.GetProperty("version").GetString() ?? "<null>";
    }
    catch (Exception) { return "<bad json>"; }
}

static string Info(string root, string projectDir, string tfm, string? assemblyName = null)
{
    var dir = Path.Combine(root, projectDir.Replace('/', Path.DirectorySeparatorChar), "bin", "Release", tfm);
    var name = assemblyName ?? Path.GetFileName(projectDir.TrimEnd('/'));
    var dll = Path.Combine(dir, name + ".dll");
    if (!File.Exists(dll)) return "<missing>";
    var v = FileVersionInfo.GetVersionInfo(dll).ProductVersion ?? "<none>";
    var plus = v.IndexOf('+');
    return plus >= 0 ? v[..plus] : v;
}

static string Nuspec(string outDir, string id)
{
    if (!Directory.Exists(outDir)) return "<missing>";
    var nupkg = Directory.GetFiles(outDir, id + ".*.nupkg").FirstOrDefault(f => !f.EndsWith(".snupkg"));
    if (nupkg == null) return "<missing>";
    using var zip = ZipFile.OpenRead(nupkg);
    using var s = zip.Entries.First(e => e.FullName.EndsWith(".nuspec")).Open();
    return XDocument.Load(s).Descendants().First(e => e.Name.LocalName == "version").Value;
}

void Step(string name) => Console.WriteLine($"\n== {name}");

void Check(bool ok, string what)
{
    Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {what}");
    if (!ok) failures.Add(what);
}

void Require(bool ok, string what, string output)
{
    if (ok) return;
    Console.WriteLine(string.Join('\n', output.Split('\n').TakeLast(40)));
    throw new FatalException(what);
}

static string? FindRepoRoot(string start)
{
    for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "Mister.Version.sln"))) return d.FullName;
    return null;
}

static (int ExitCode, string Output, string Stdout) Run(string cwd, string file, string[] arguments, bool rollForward = false)
{
    var psi = new ProcessStartInfo(file) { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var a in arguments) psi.ArgumentList.Add(a);
    psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
    psi.Environment["DOTNET_NOLOGO"] = "1";
    psi.Environment["MSBUILDTERMINALLOGGER"] = "off";
    if (rollForward) psi.Environment["DOTNET_ROLL_FORWARD"] = "Major"; // the CLI targets net8.0
    using var p = Process.Start(psi)!;
    var stdout = p.StandardOutput.ReadToEndAsync();
    var stderr = p.StandardError.ReadToEndAsync();
    p.WaitForExit();
    return (p.ExitCode, stdout.Result + stderr.Result, stdout.Result);
}

static (int ExitCode, string Output) Dotnet(string cwd, params string[] arguments)
{
    var r = Run(cwd, "dotnet", arguments);
    return (r.ExitCode, r.Output);
}

static void Git(string cwd, params string[] arguments)
{
    var r = Run(cwd, "git", arguments);
    if (r.ExitCode != 0) throw new FatalException($"git {string.Join(' ', arguments)} failed: {r.Output}");
}

static void TryDelete(string dir)
{
    try
    {
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(dir, true);
    }
    catch (Exception) { }
}

sealed class FatalException(string message) : Exception(message);
