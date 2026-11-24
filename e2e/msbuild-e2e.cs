#:property TargetFramework=net10.0
#:property Nullable=enable
#:property ImplicitUsings=enable
#:property PublishAot=false

// End-to-end MSBuild integration test for the Mister.Version NuGet package.
//
// Packs Mister.Version from this repository with a unique version, consumes it from a throwaway
// git monorepo, and asserts on what actually ends up in the built assemblies and packages.
// Runs identically on Windows, macOS and Linux:
//
//     dotnet run e2e/msbuild-e2e.cs              (from the repository root)
//     dotnet run e2e/msbuild-e2e.cs -- --keep    (keep the temp directory for inspection)
//     dotnet run e2e/msbuild-e2e.cs -- --msbuild "C:\...\MSBuild.exe"
//         build/pack the monorepo with Visual Studio's MSBuild (.NET Framework task assembly)
//
// Exit code is the number of failed checks (0 = success).

using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;

var keep = args.Contains("--keep");
var msbuildIdx = Array.IndexOf(args, "--msbuild");
var msbuildExe = msbuildIdx >= 0 && msbuildIdx + 1 < args.Length ? args[msbuildIdx + 1] : null;
var repoRoot = FindRepoRoot(Environment.CurrentDirectory)
    ?? throw new InvalidOperationException("Run from inside the mister.version repository (Mister.Version.sln not found).");

var work = Path.Combine(Path.GetTempPath(), "mrv-e2e-" + Guid.NewGuid().ToString("N")[..8]);
var feed = Path.Combine(work, "feed");
var mono = Path.Combine(work, "mono");
Directory.CreateDirectory(feed);
Directory.CreateDirectory(mono);

var failures = new List<string>();
var timings = new List<(string, TimeSpan)>();
var pkgVersion = $"0.0.0-e2e.{DateTime.UtcNow:yyyyMMddHHmmss}";

Console.WriteLine($"Repo:    {repoRoot}");
Console.WriteLine($"Work:    {work}");
Console.WriteLine($"OS/arch: {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}");
Console.WriteLine($"Builder: {msbuildExe ?? "dotnet"}");

try
{
    // ---------------------------------------------------------------- pack the task package
    Step("Pack Mister.Version");
    var pack = Dotnet(repoRoot, "pack", Path.Combine("Mister.Version", "Mister.Version.csproj"),
        "-c", "Release", "-o", feed, $"-p:Version={pkgVersion}", $"-p:PackageVersion={pkgVersion}");
    Require(pack.ExitCode == 0, "pack Mister.Version", pack.Output);

    // ---------------------------------------------------------------- create the monorepo
    Step("Create monorepo");
    CreateMonorepo(mono, feed, pkgVersion);
    Git(mono, "init", "-q");
    Git(mono, "checkout", "-q", "-b", "main");
    Git(mono, "config", "user.email", "e2e@example.com");
    Git(mono, "config", "user.name", "e2e");
    Git(mono, "config", "commit.gpgsign", "false");
    Git(mono, "config", "tag.gpgsign", "false");
    Git(mono, "add", "-A");
    Git(mono, "commit", "-q", "-m", "initial");
    Git(mono, "tag", "v1.0.0");
    File.AppendAllText(Path.Combine(mono, "src", "Core", "Core.cs"), "\n// fix\n");
    Git(mono, "commit", "-q", "-am", "fix: core change");

    var restore = msbuildExe == null ? Dotnet(mono, "restore") : Run(mono, msbuildExe, "Mono.slnx", "-nologo", "-v:m", "-t:Restore");
    Require(restore.ExitCode == 0, "restore monorepo", restore.Output);

    // ---------------------------------------------------------------- cold build
    Step("Cold build");
    var sw = Stopwatch.StartNew();
    var build = Build("Release");
    timings.Add(("cold build", sw.Elapsed));
    Require(build.ExitCode == 0, "cold build succeeds", build.Output);

    var coreNet8 = Ver(mono, "Core", "net8.0");
    var coreNet10 = Ver(mono, "Core", "net10.0");
    var lib = Ver(mono, "Lib", "net10.0");
    var app = Ver(mono, "App", "net10.0");
    Console.WriteLine($"  Core(net8.0)={coreNet8}  Core(net10.0)={coreNet10}  Lib={lib}  App={app}");

    Check(coreNet8 == "1.0.1", $"Core is 1.0.1 after a fix commit on top of v1.0.0 (got {coreNet8})");
    Check(coreNet8 == coreNet10, $"multi-targeted Core has the same version for every TFM ({coreNet8} vs {coreNet10})");
    Check(lib == "1.0.1", $"Lib is bumped because its dependency Core changed (got {lib})");
    Check(AsmVer(mono, "Core", "net10.0") == "1.0.1.0", $"Core AssemblyVersion is 1.0.1.0 (got {AsmVer(mono, "Core", "net10.0")})");
    Check(!File.Exists(Path.Combine(mono, "tests", "Core.Tests", "obj", "Release", "net10.0", "Core.Tests.mrv.g.cs")),
        "test projects are not versioned");

    // ---------------------------------------------------------------- incremental (no-op) build
    Step("No-op incremental build");
    var before = IntermediateAssemblies(mono).ToDictionary(p => p, File.GetLastWriteTimeUtc);
    sw.Restart();
    var noop = Build("Release");
    timings.Add(("no-op build", sw.Elapsed));
    Require(noop.ExitCode == 0, "no-op build succeeds", noop.Output);
    var recompiled = before.Where(kv => File.GetLastWriteTimeUtc(kv.Key) != kv.Value).Select(kv => Path.GetFileName(kv.Key)).ToList();
    Check(recompiled.Count == 0, $"no-op build does not recompile anything (recompiled: {string.Join(", ", recompiled)})");

    // ---------------------------------------------------------------- pack consistency
    Step("Pack");
    var outDir = Path.Combine(work, "out");
    var packOut = msbuildExe == null
        ? Dotnet(mono, "pack", "--no-restore", "-c", "Release", "-o", outDir)
        : Run(mono, msbuildExe, "Mono.slnx", "-nologo", "-v:m", "-t:Pack", "-p:Configuration=Release", $"-p:PackageOutputPath={outDir}");
    Require(packOut.ExitCode == 0, "pack monorepo", packOut.Output);
    Check(!packOut.Output.Contains("NU5104"), "pack emits no NU5104 (stable package depending on prerelease)");
    var corePkg = Nuspec(outDir, "Core");
    var libPkg = Nuspec(outDir, "Lib");
    Console.WriteLine($"  Core.nupkg={corePkg.Version}  Lib.nupkg={libPkg.Version}  Lib->Core dep={libPkg.Deps.GetValueOrDefault("Core")}");
    Check(corePkg.Version == coreNet8, $"multi-targeted Core package version matches its assembly version ({corePkg.Version} vs {coreNet8})");
    Check(libPkg.Version == lib, $"Lib package version matches its assembly version ({libPkg.Version} vs {lib})");
    Check(libPkg.Deps.GetValueOrDefault("Core") == corePkg.Version,
        $"Lib's dependency on Core uses Core's package version ({libPkg.Deps.GetValueOrDefault("Core")} vs {corePkg.Version})");

    // ---------------------------------------------------------------- tag on HEAD without new commits
    // The build server / MSBuild nodes are reused between invocations; a new tag must still be seen.
    Step("Tag HEAD and rebuild (node reuse)");
    Git(mono, "tag", "v2.0.0");
    var tagged = Build("Release");
    Require(tagged.ExitCode == 0, "build after tagging", tagged.Output);
    Check(Ver(mono, "Core", "net8.0") == "2.0.0", $"Core picks up the new v2.0.0 tag on HEAD (got {Ver(mono, "Core", "net8.0")})");
    Check(Ver(mono, "Lib", "net10.0") == "2.0.0", $"Lib picks up the new v2.0.0 tag on HEAD (got {Ver(mono, "Lib", "net10.0")})");

    // ---------------------------------------------------------------- change only Lib
    Step("Commit touching only Lib");
    File.AppendAllText(Path.Combine(mono, "src", "Lib", "Lib.cs"), "\n// lib fix\n");
    Git(mono, "commit", "-q", "-am", "fix: lib change");
    var libOnly = Build("Release");
    Require(libOnly.ExitCode == 0, "build after Lib change", libOnly.Output);
    Check(Ver(mono, "Lib", "net10.0") == "2.0.1", $"Lib is bumped to 2.0.1 (got {Ver(mono, "Lib", "net10.0")})");
    Check(Ver(mono, "Core", "net8.0") == "2.0.0", $"Core stays at 2.0.0 (got {Ver(mono, "Core", "net8.0")})");

    // ---------------------------------------------------------------- single project build
    Step("Build a single project directly");
    var single = Build("Release", Path.Combine("src", "Lib", "Lib.csproj"));
    Require(single.ExitCode == 0, "single-project build", single.Output);
    Check(Ver(mono, "Lib", "net10.0") == "2.0.1", $"single-project build yields the same Lib version (got {Ver(mono, "Lib", "net10.0")})");

    // ---------------------------------------------------------------- pack --no-build
    Step("Pack without building");
    var outNoBuild = Path.Combine(work, "out-nobuild");
    var packNoBuild = msbuildExe == null
        ? Dotnet(mono, "pack", "--no-build", "-c", "Release", "-o", outNoBuild)
        : Run(mono, msbuildExe, "Mono.slnx", "-nologo", "-v:m", "-t:Pack", "-p:NoBuild=true", "-p:Configuration=Release", $"-p:PackageOutputPath={outNoBuild}");
    Require(packNoBuild.ExitCode == 0, "pack --no-build", packNoBuild.Output);
    var coreNoBuild = Nuspec(outNoBuild, "Core");
    var libNoBuild = Nuspec(outNoBuild, "Lib");
    Check(coreNoBuild.Version == "2.0.0" && libNoBuild.Version == "2.0.1" && libNoBuild.Deps.GetValueOrDefault("Core") == "2.0.0",
        $"pack --no-build yields Core 2.0.0, Lib 2.0.1 -> Core 2.0.0 (got {coreNoBuild.Version}, {libNoBuild.Version} -> {libNoBuild.Deps.GetValueOrDefault("Core")})");

    // ---------------------------------------------------------------- GetTargetPath is not hijacked
    if (msbuildExe == null)
    {
        Step("GetTargetPath (used by IDEs and BuildProjectReferences=false)");
        var gtp = Dotnet(mono, "msbuild", Path.Combine("src", "Lib", "Lib.csproj"), "-nologo", "-p:Configuration=Release", "-getTargetResult:GetTargetPath");
        var identities = System.Text.RegularExpressions.Regex.Matches(gtp.Output, "\"Identity\"\\s*:").Count;
        Check(gtp.ExitCode == 0 && identities == 1, $"GetTargetPath returns exactly one item (got {identities})");
        Check(gtp.Output.Contains("\"ReferenceAssembly\""), "GetTargetPath keeps the SDK's ReferenceAssembly metadata");
    }

    // ---------------------------------------------------------------- CLI parity
    Step("CLI agrees with MSBuild");
    var cliProject = Path.Combine(repoRoot, "Mister.Version.CLI", "Mister.Version.CLI.csproj");
    var cliBuild = Dotnet(repoRoot, "build", cliProject, "-c", "Release", "-nologo", "-v:q");
    Require(cliBuild.ExitCode == 0, "build CLI", cliBuild.Output);
    var cliDll = Path.Combine(repoRoot, "Mister.Version.CLI", "bin", "Release", "net8.0", "mr-version.dll");
    foreach (var (project, expected) in new[] { ("Core", Ver(mono, "Core", "net8.0")), ("Lib", Ver(mono, "Lib", "net10.0")) })
    {
        var cli = RunWithRollForward(mono, "dotnet", cliDll, "version", "--repo", mono,
            "--project", Path.Combine(mono, "src", project, project + ".csproj"));
        var cliVersion = cli.Output.Split('\n').Select(l => l.Trim())
            .Where(l => l.StartsWith("Version:", StringComparison.Ordinal)).Select(l => l["Version:".Length..].Trim()).LastOrDefault() ?? "";
        Check(cli.ExitCode == 0 && cliVersion == expected, $"CLI 'version' for {project} matches MSBuild ({cliVersion} vs {expected})");
    }

    // ---------------------------------------------------------------- versioning disabled
    Step("Versioning disabled");
    var off = Build("Debug", "-p:MonoRepoVersioningEnabled=false");
    Require(off.ExitCode == 0, "build with MonoRepoVersioningEnabled=false", off.Output);
    var offVersion = Ver(mono, "Core", "net8.0", "Debug");
    Check(offVersion == "1.0.0", $"MonoRepoVersioningEnabled=false leaves the SDK default version (got {offVersion})");
}
catch (FatalException ex)
{
    failures.Add(ex.Message);
}
finally
{
    Dotnet(mono, "build-server", "shutdown");
    Console.WriteLine();
    foreach (var (name, t) in timings) Console.WriteLine($"  time {name,-14} {t.TotalSeconds,6:F1}s");
    if (!keep) TryDelete(work);
    else Console.WriteLine($"Kept {work}");
}

Console.WriteLine();
Console.WriteLine(failures.Count == 0 ? "E2E: ALL CHECKS PASSED" : $"E2E: {failures.Count} CHECK(S) FAILED");
foreach (var f in failures) Console.WriteLine("  FAIL: " + f);
return failures.Count;

// ==================================================================================== helpers

(int ExitCode, string Output) Build(string configuration, params string[] extra) => msbuildExe == null
    ? Dotnet(mono, new[] { "build", "--no-restore", "-c", configuration }.Concat(extra).ToArray())
    : Run(mono, msbuildExe, (extra.Any(e => e.EndsWith("proj")) ? Array.Empty<string>() : new[] { "Mono.slnx" })
        .Concat(new[] { "-nologo", "-v:m", $"-p:Configuration={configuration}" }).Concat(extra).ToArray());

void Step(string name) => Console.WriteLine($"\n== {name}");

void Check(bool ok, string what)
{
    Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {what}");
    if (!ok) failures.Add(what);
}

void Require(bool ok, string what, string output)
{
    if (ok) return;
    Console.WriteLine(Tail(output, 60));
    throw new FatalException(what);
}

static string Tail(string s, int lines) => string.Join('\n', s.Split('\n').TakeLast(lines));

static string? FindRepoRoot(string start)
{
    for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "Mister.Version.sln"))) return d.FullName;
    return null;
}

static (int ExitCode, string Output) Run(string cwd, string file, params string[] arguments)
{
    var psi = new ProcessStartInfo(file) { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var a in arguments) psi.ArgumentList.Add(a);
    psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
    psi.Environment["DOTNET_NOLOGO"] = "1";
    psi.Environment["MSBUILDDISABLENODEREUSE"] = null; // keep default node reuse: it is part of what we test
    using var p = Process.Start(psi)!;
    var stdout = p.StandardOutput.ReadToEndAsync();
    var stderr = p.StandardError.ReadToEndAsync();
    p.WaitForExit();
    return (p.ExitCode, stdout.Result + stderr.Result);
}

static (int ExitCode, string Output) Dotnet(string cwd, params string[] arguments) => Run(cwd, "dotnet", arguments);

// The CLI targets net8.0; allow it to run on a newer runtime when only that is installed.
static (int ExitCode, string Output) RunWithRollForward(string cwd, string file, params string[] arguments)
{
    Environment.SetEnvironmentVariable("DOTNET_ROLL_FORWARD", "Major");
    try { return Run(cwd, file, arguments); }
    finally { Environment.SetEnvironmentVariable("DOTNET_ROLL_FORWARD", null); }
}

static void Git(string cwd, params string[] arguments)
{
    var r = Run(cwd, "git", arguments);
    if (r.ExitCode != 0) throw new FatalException($"git {string.Join(' ', arguments)} failed: {r.Output}");
}

static string Ver(string mono, string project, string tfm, string configuration = "Release")
{
    var dll = Path.Combine(mono, "src", project, "bin", configuration, tfm, project + ".dll");
    if (!File.Exists(dll)) return "<missing>";
    var v = FileVersionInfo.GetVersionInfo(dll).ProductVersion ?? "<none>";
    var plus = v.IndexOf('+');
    return plus >= 0 ? v[..plus] : v;
}

static string AsmVer(string mono, string project, string tfm)
{
    var dll = Path.Combine(mono, "src", project, "bin", "Release", tfm, project + ".dll");
    return File.Exists(dll) ? System.Reflection.AssemblyName.GetAssemblyName(dll).Version?.ToString() ?? "<none>" : "<missing>";
}

static IEnumerable<string> IntermediateAssemblies(string mono) =>
    Directory.EnumerateFiles(mono, "*.dll", SearchOption.AllDirectories)
        .Where(p => p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}")
                    && !p.Contains($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}")
                    && !p.Contains($"{Path.DirectorySeparatorChar}refint{Path.DirectorySeparatorChar}"));

static (string Version, Dictionary<string, string> Deps) Nuspec(string outDir, string id)
{
    var nupkg = Directory.GetFiles(outDir, id + ".*.nupkg")
        .FirstOrDefault(f => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f), $@"^{id}\.\d"));
    if (nupkg == null) return ("<missing>", new());
    using var zip = ZipFile.OpenRead(nupkg);
    var entry = zip.Entries.First(e => e.FullName.EndsWith(".nuspec"));
    using var s = entry.Open();
    var doc = XDocument.Load(s);
    var version = doc.Descendants().First(e => e.Name.LocalName == "version").Value;
    var deps = doc.Descendants().Where(e => e.Name.LocalName == "dependency")
        .GroupBy(e => (string)e.Attribute("id")!)
        .ToDictionary(g => g.Key, g => ((string)g.First().Attribute("version")!).Trim('[', ']', '(', ')', ' ').Split(',')[0]);
    return (version, deps);
}

static void TryDelete(string dir)
{
    try { Directory.Delete(dir, true); } catch { /* best effort; git objects can be read-only on Windows */ }
}

static void CreateMonorepo(string root, string feed, string pkgVersion)
{
    void W(string rel, string content)
    {
        var path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    W("nuget.config", $"""
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <packageSources>
            <clear />
            <add key="e2e" value="{feed}" />
            <add key="nuget" value="https://api.nuget.org/v3/index.json" />
          </packageSources>
        </configuration>
        """);
    W("Directory.Build.props", $"""
        <Project>
          <ItemGroup>
            <PackageReference Include="Mister.Version" Version="{pkgVersion}" PrivateAssets="all" />
          </ItemGroup>
        </Project>
        """);
    W(".gitignore", "bin/\nobj/\n");
    W("src/Core/Core.csproj", """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
            <IsPackable>true</IsPackable>
          </PropertyGroup>
        </Project>
        """);
    W("src/Core/Core.cs", "namespace Core; public static class C { public static int V => 1; }\n");
    W("src/Lib/Lib.csproj", """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <IsPackable>true</IsPackable>
          </PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="../Core/Core.csproj" />
          </ItemGroup>
        </Project>
        """);
    W("src/Lib/Lib.cs", "namespace Lib; public static class L { public static int V => Core.C.V + 1; }\n");
    W("src/App/App.csproj", """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <IsPackable>true</IsPackable>
          </PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="../Lib/Lib.csproj" />
          </ItemGroup>
        </Project>
        """);
    W("src/App/Program.cs", "System.Console.WriteLine(Lib.L.V);\n");
    W("tests/Core.Tests/Core.Tests.csproj", """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <IsPackable>false</IsPackable>
            <IsTestProject>true</IsTestProject>
          </PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="../../src/Core/Core.csproj" />
          </ItemGroup>
        </Project>
        """);
    W("tests/Core.Tests/X.cs", "namespace T; public class X { }\n");
    W("Mono.slnx", """
        <Solution>
          <Project Path="src/Core/Core.csproj" />
          <Project Path="src/Lib/Lib.csproj" />
          <Project Path="src/App/App.csproj" />
          <Project Path="tests/Core.Tests/Core.Tests.csproj" />
        </Solution>
        """);
}

sealed class FatalException(string message) : Exception(message);
