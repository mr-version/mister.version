#:property TargetFramework=net10.0
#:property Nullable=enable
#:property ImplicitUsings=enable
#:property PublishAot=false

// Large-solution stress test for the Mister.Version MSBuild integration.
//
// Generates a deterministic monorepo with N projects (default 80) in layers, where every project references
// 1-4 projects from lower layers (many diamonds and long transitive chains), a mix of multi-targeted
// (net8.0;net10.0) and single-targeted libraries, Exe apps, non-packable apps with an explicit <Version>, and test
// projects. Project names Lib1..LibN live side by side in src/, so Lib1 / Lib10..Lib19 exercise directory-prefix
// boundaries at scale.
//
// Each round changes specific projects and verifies that EXACTLY the projects that (transitively) depend on a
// change are bumped (the expected set is computed independently from the generated graph), plus, for every
// project, TFM and edge: assembly version == package version, nuspec dependency versions == the dependency's
// package version, compiled assembly references == the dependency's AssemblyVersion, and CLI == MSBuild.
//
//     dotnet run e2e/msbuild-stress.cs -- [--projects 80] [--seed 1] [--keep]
//
// Exit code is the number of failed checks.

using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

int Arg(string name, int fallback) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? int.Parse(args[i + 1]) : fallback; }
var projectCount = Arg("--projects", 80);
var seed = Arg("--seed", 1);
var keep = args.Contains("--keep");

var repoRoot = FindRepoRoot(Environment.CurrentDirectory) ?? throw new InvalidOperationException("Run from inside the mister.version repository.");
var work = Path.Combine(Path.GetTempPath(), "mrv-stress-" + Guid.NewGuid().ToString("N")[..8]);
var feed = Path.Combine(work, "feed");
var mono = Path.Combine(work, "mono");
Directory.CreateDirectory(feed);
Directory.CreateDirectory(mono);
var pkgVersion = $"0.0.0-stress.{DateTime.UtcNow:yyyyMMddHHmmss}";
var cli = Path.Combine(repoRoot, "Mister.Version.CLI", "bin", "Release", "net8.0", "mr-version.dll");
var failures = new List<string>();
var timings = new List<(string, TimeSpan, string)>();
var checks = 0;

Console.WriteLine($"Repo: {repoRoot}\nWork: {work}\nProjects: {projectCount}, seed {seed}, {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}");

// ============================================================================ graph
var rng = new Random(seed);
const int Layers = 8;
var perLayer = Math.Max(1, projectCount / Layers);
var projects = new List<Proj>();
for (var i = 1; i <= projectCount; i++)
{
    var layer = Math.Min(Layers - 1, (i - 1) / perLayer);
    var p = new Proj($"Lib{i}", layer);
    if (layer <= 4 && rng.NextDouble() < 0.4) p.Multi = true;

    if (layer > 0)
    {
        var candidates = projects.Where(c => c.Layer < layer && c.Layer >= Math.Max(0, layer - 3) && !c.IsApp).ToList();
        var want = rng.Next(1, 5);
        for (var k = 0; k < want && candidates.Count > 0; k++)
        {
            var pool = p.Multi ? candidates.Where(c => c.Multi).ToList() : candidates;
            if (pool.Count == 0) { p.Multi = false; pool = candidates; }
            var dep = pool[rng.Next(pool.Count)];
            if (!p.Deps.Contains(dep)) p.Deps.Add(dep);
        }
        if (p.Multi && p.Deps.Any(d => !d.Multi)) p.Multi = false;
    }

    if (layer == Layers - 1 && rng.NextDouble() < 0.5)
    {
        p.IsApp = true;
        p.Multi = false;
        if (rng.NextDouble() < 0.5) { p.NonPackable = true; p.ExplicitVersion = $"9.9.{i}"; }
    }
    projects.Add(p);
}
var testProjects = new List<Proj>();
for (var t = 1; t <= Math.Max(2, projectCount / 10); t++)
{
    var tp = new Proj($"Lib{t}.Tests", -1) { IsTest = true };
    var libs = projects.Where(p => !p.IsApp).ToList();
    for (var k = 0; k < rng.Next(1, 4); k++) { var d = libs[rng.Next(libs.Count)]; if (!tp.Deps.Contains(d)) tp.Deps.Add(d); }
    testProjects.Add(tp);
}
var all = projects.Concat(testProjects).ToList();
var versioned = projects.Where(p => !p.NonPackable).ToList();

Console.WriteLine($"Graph: {projects.Count} projects ({projects.Count(p => p.Multi)} multi-targeted, {projects.Count(p => p.IsApp)} apps, " +
                  $"{projects.Count(p => p.NonPackable)} non-packable), {testProjects.Count} test projects, " +
                  $"{all.Sum(p => p.Deps.Count)} direct references, max depth {projects.Max(Depth)}, " +
                  $"avg transitive deps {projects.Average(p => Closure(p).Count):F1}");

try
{
    // ======================================================================== setup
    Step("Pack Mister.Version");
    var pack = Dotnet(repoRoot, "pack", Path.Combine("Mister.Version", "Mister.Version.csproj"), "-c", "Release", "-o", feed, $"-p:Version={pkgVersion}", $"-p:PackageVersion={pkgVersion}");
    Require(pack.ExitCode == 0, "pack Mister.Version", pack.Output);

    Step("Create repository");
    WriteRepo();
    Git("init", "-q"); Git("checkout", "-q", "-b", "main");
    Git("config", "user.email", "e2e@example.com"); Git("config", "user.name", "e2e");
    Git("config", "commit.gpgsign", "false"); Git("config", "tag.gpgsign", "false");
    Git("add", "-A"); Git("commit", "-q", "-m", "initial"); Git("tag", "v1.0.0");
    var restore = Timed("restore", () => Dotnet(mono, "restore", "Stress.slnx"));
    Require(restore.ExitCode == 0, "restore", restore.Output);

    var cliBuild = Dotnet(repoRoot, "build", Path.Combine(repoRoot, "Mister.Version.CLI", "Mister.Version.CLI.csproj"), "-c", "Release", "-nologo", "-v:q");
    Require(cliBuild.ExitCode == 0, "build CLI", cliBuild.Output);

    // ======================================================================== baseline cost without versioning
    Step("Baseline: build with versioning disabled (for overhead comparison)");
    var off = Timed("build (versioning off)", () => Dotnet(mono, "build", "Stress.slnx", "--no-restore", "-c", "Debug", "-p:MonoRepoVersioningEnabled=false", "-clp:PerformanceSummary"));
    Require(off.ExitCode == 0, "build with versioning disabled", off.Output);

    // ======================================================================== round 0
    Step("Round 0: tagged v1.0.0");
    var v0 = BuildPackVerify("r0", cold: true);
    ExpectVersions("r0", v0, p => "1.0.0");

    Step("No-op rebuild");
    var noop = Timed("no-op build", () => Dotnet(mono, "build", "Stress.slnx", "--no-restore", "-c", "Release", "-clp:PerformanceSummary"));
    Require(noop.ExitCode == 0, "no-op build", noop.Output);
    Check(!Regex.IsMatch(noop.Output, @"\d+ ms\s+Csc\s+\d+ calls"), "no-op build runs no compiler (Csc)");
    TaskTime("no-op build", noop.Output);

    // ======================================================================== round 1: change a low-level project
    var r1Changed = new[] { projects[0] }; // Lib1: siblings Lib10..Lib19 must not be affected by directory prefix
    Step($"Round 1: change {string.Join(", ", r1Changed.Select(p => p.Name))}");
    Change(r1Changed, "fix: round 1");
    var v1 = BuildPackVerify("r1");
    ExpectVersions("r1", v1, p => Affected(p, r1Changed) ? "1.0.1" : "1.0.0");

    // ======================================================================== round 2: new release, three changes
    Git("tag", "v1.1.0");
    var low = projects.Where(p => p.Layer == 1).OrderBy(p => p.Name).First();
    var mid = projects.Where(p => p.Layer == 4 && !p.IsApp).OrderBy(p => p.Name).Last();
    var high = projects.Where(p => p.Layer == Layers - 2).OrderBy(p => p.Name).First();
    var r2Changed = new[] { low, mid, high };
    Step($"Round 2: tag v1.1.0, change {string.Join(", ", r2Changed.Select(p => p.Name))}");
    Change(r2Changed, "fix: round 2");
    var v2 = BuildPackVerify("r2");
    ExpectVersions("r2", v2, p => Affected(p, r2Changed) ? "1.1.1" : "1.1.0");

    // ======================================================================== round 3: project-specific tag
    var tagged = projects.Where(p => p.Layer == 5 && !p.IsApp && p.Deps.Count > 0).OrderBy(p => p.Name).First();
    var taggedDep = tagged.Deps[0];
    Git("tag", $"{tagged.Name}/v5.0.0");
    var r3Changed = r2Changed.Append(taggedDep).ToArray();
    Step($"Round 3: tag {tagged.Name}/v5.0.0, then change its dependency {taggedDep.Name}");
    Change(new[] { taggedDep }, "fix: round 3");
    var v3 = BuildPackVerify("r3");
    ExpectVersions("r3", v3, p => p == tagged ? "5.0.1" : Affected(p, r3Changed) ? "1.1.1" : "1.1.0");

    // ======================================================================== round 4: test-only change
    Step("Round 4: change only a test project");
    File.AppendAllText(Path.Combine(mono, "tests", testProjects[0].Name, "T.cs"), "\n// test change\n");
    Git("commit", "-q", "-am", "test: round 4");
    var v4 = BuildPackVerify("r4");
    ExpectVersions("r4", v4, p => v3.GetValueOrDefault(p.Name) ?? "?");

    // ======================================================================== round 5: new major, app-only change
    Git("tag", "v2.0.0");
    var app = projects.First(p => p.IsApp && !p.NonPackable);
    Step($"Round 5: tag v2.0.0, change only app {app.Name}");
    Change(new[] { app }, "fix: round 5");
    var v5 = BuildPackVerify("r5");
    ExpectVersions("r5", v5, p => p == app ? "2.0.1" : p == tagged ? "5.0.1" : "2.0.0");

    // ======================================================================== build variations
    Step("Variation: static graph build (-graph) gives the same versions");
    var graph = Dotnet(mono, "build", "Stress.slnx", "--no-restore", "-c", "Release", "-graph");
    Check(graph.ExitCode == 0, "graph build succeeds");
    ExpectVersions("graph", ReadAssemblyVersions(), p => v5[p.Name]);

    Step("Variation: single top project with --no-dependencies after a full build");
    var top = projects.Last(p => !p.NonPackable);
    var nodeps = Dotnet(mono, "build", Path.Combine("src", top.Name, top.Name + ".csproj"), "--no-restore", "-c", "Release", "--no-dependencies");
    Check(nodeps.ExitCode == 0, "--no-dependencies build succeeds");
    Check(VerAt(top, top.Tfms[0]) == v5[top.Name], $"--no-dependencies keeps {top.Name} at {v5[top.Name]} (got {VerAt(top, top.Tfms[0])})");

    Step("Variation: fresh processes, single node (-m:1 -nodeReuse:false)");
    Dotnet(mono, "build-server", "shutdown");
    var m1 = Dotnet(mono, "build", "Stress.slnx", "--no-restore", "-c", "Release", "-m:1", "-nodeReuse:false");
    Check(m1.ExitCode == 0, "-m:1 build succeeds");
    ExpectVersions("m1", ReadAssemblyVersions(), p => v5[p.Name]);
}
catch (FatalException ex)
{
    failures.Add(ex.Message);
}
finally
{
    Dotnet(mono, "build-server", "shutdown");
    Console.WriteLine();
    foreach (var (name, t, note) in timings) Console.WriteLine($"  time {name,-28} {t.TotalSeconds,7:F1}s {note}");
    if (!keep) TryDelete(work); else Console.WriteLine($"Kept {work}");
}

Console.WriteLine();
Console.WriteLine(failures.Count == 0 ? $"STRESS: ALL {checks} CHECKS PASSED" : $"STRESS: {failures.Count} OF {checks} CHECK(S) FAILED");
foreach (var f in failures.Take(60)) Console.WriteLine("  FAIL: " + f);
if (failures.Count > 60) Console.WriteLine($"  ... and {failures.Count - 60} more");
return failures.Count;

// ============================================================================ round helpers

Dictionary<string, string> BuildPackVerify(string label, bool cold = false)
{
    var build = Timed($"{label} build{(cold ? " (cold)" : "")}", () => Dotnet(mono, "build", "Stress.slnx", "--no-restore", "-c", "Release", "-clp:PerformanceSummary"));
    Require(build.ExitCode == 0, $"[{label}] build", build.Output);
    TaskTime($"{label} build", build.Output);

    var outDir = Path.Combine(work, "out-" + label);
    var packResult = Timed($"{label} pack --no-build", () => Dotnet(mono, "pack", "Stress.slnx", "--no-build", "-c", "Release", "-o", outDir, "-clp:PerformanceSummary"));
    Require(packResult.ExitCode == 0, $"[{label}] pack", packResult.Output);
    TaskTime($"{label} pack", packResult.Output);

    var asm = ReadAssemblyVersions();
    var pkgs = new Dictionary<string, (string Version, List<(string Group, string Id, string Version)> Deps)>();
    foreach (var p in projects.Where(p => !p.NonPackable))
        pkgs[p.Name] = NuspecFull(outDir, p.Name);

    var errors = 0;
    void Fail(string message) { if (errors++ < 15) Check(false, $"[{label}] {message}"); else { checks++; failures.Add($"[{label}] {message}"); } }

    foreach (var p in projects.Where(p => !p.NonPackable))
    {
        checks++;
        if (pkgs[p.Name].Version == "<missing>") { Fail($"{p.Name}: no package"); continue; }
        foreach (var tfm in p.Tfms)
        {
            checks++;
            var v = VerAt(p, tfm);
            if (v != pkgs[p.Name].Version) Fail($"{p.Name} ({tfm}) assembly {v} != package {pkgs[p.Name].Version}");
        }
        foreach (var (group, id, depVersion) in pkgs[p.Name].Deps)
        {
            if (!pkgs.TryGetValue(id, out var dep)) continue;
            checks++;
            if (depVersion != dep.Version) Fail($"{p.Name}.nupkg [{group}] depends on {id} {depVersion}, but {id} is {dep.Version}");
        }
    }
    foreach (var p in projects)
    {
        foreach (var tfm in p.Tfms)
        {
            foreach (var (refName, refVersion) in AssemblyRefs(p, tfm))
            {
                var dep = all.FirstOrDefault(d => d.Name == refName);
                if (dep == null) continue;
                checks++;
                var depTfm = dep.Tfms.Contains(tfm) ? tfm : dep.Tfms[0];
                var expected = AsmVerAt(dep, depTfm);
                if (refVersion != expected) Fail($"{p.Name} ({tfm}) compiled against {refName} {refVersion}, but {refName} is {expected}");
            }
        }
    }
    foreach (var p in projects.Where(p => p.NonPackable))
    {
        checks++;
        if (asm.GetValueOrDefault(p.Name) != p.ExplicitVersion) Fail($"non-packable {p.Name} keeps <Version> {p.ExplicitVersion} (got {asm.GetValueOrDefault(p.Name)})");
    }

    // CLI parity (one report call for the whole repository)
    var report = RunWithRollForward(mono, "dotnet", cli, "report", "--repo", mono, "--output", "json", "--include-test-projects", "--include-non-packable");
    if (report.ExitCode != 0) { Check(false, $"[{label}] CLI report succeeds"); }
    else
    {
        try
        {
            var json = report.Output[report.Output.IndexOf('{')..];
            using var doc = JsonDocument.Parse(json);
            var cliVersions = doc.RootElement.GetProperty("projects").EnumerateArray()
                .ToDictionary(e => e.GetProperty("name").GetString()!, e => e.GetProperty("version").GetProperty("version").GetString()!);
            var mismatches = projects.Where(p => !p.NonPackable && cliVersions.GetValueOrDefault(p.Name) != asm.GetValueOrDefault(p.Name)).ToList();
            checks++;
            if (mismatches.Count > 0)
                Fail($"CLI report disagrees with MSBuild for {mismatches.Count} project(s): " +
                     string.Join(", ", mismatches.Take(6).Select(p => $"{p.Name} cli={cliVersions.GetValueOrDefault(p.Name)} msbuild={asm.GetValueOrDefault(p.Name)}")));
        }
        catch (Exception ex) { Check(false, $"[{label}] CLI report JSON parses ({ex.Message})"); }
    }

    Console.WriteLine($"  [{label}] verified {projects.Count} projects; distinct versions: " +
                      string.Join(", ", asm.Where(kv => versioned.Any(v => v.Name == kv.Key)).GroupBy(kv => kv.Value).OrderBy(g => g.Key).Select(g => $"{g.Key} x{g.Count()}")));
    return asm;
}

void ExpectVersions(string label, Dictionary<string, string> actual, Func<Proj, string> expected)
{
    var wrong = versioned.Where(p => actual.GetValueOrDefault(p.Name) != expected(p)).ToList();
    checks += versioned.Count;
    if (wrong.Count == 0)
    {
        Console.WriteLine($"  PASS  [{label}] all {versioned.Count} versioned projects have exactly the expected version");
        return;
    }
    foreach (var p in wrong.Take(15))
        Check(false, $"[{label}] {p.Name} expected {expected(p)} but got {actual.GetValueOrDefault(p.Name)} " +
                     $"(deps: {string.Join(",", p.Deps.Select(d => d.Name))})");
    for (var i = 15; i < wrong.Count; i++) failures.Add($"[{label}] {wrong[i].Name} wrong version");
}

void Change(IEnumerable<Proj> changed, string message)
{
    foreach (var p in changed) File.AppendAllText(Path.Combine(mono, "src", p.Name, "C.cs"), $"\n// change {Guid.NewGuid():N}\n");
    Git("commit", "-q", "-am", message);
}

bool Affected(Proj p, IEnumerable<Proj> changed) => changed.Contains(p) || Closure(p).Overlaps(changed);

Dictionary<string, string> ReadAssemblyVersions() => all.Where(p => !p.IsTest).ToDictionary(p => p.Name, p => VerAt(p, p.Tfms[0]));

T Timed<T>(string name, Func<T> action) { var sw = Stopwatch.StartNew(); var r = action(); timings.Add((name, sw.Elapsed, "")); return r; }

void TaskTime(string label, string output)
{
    var m = Regex.Match(output, @"(\d+) ms\s+MonoRepoVersionTask\s+(\d+) calls");
    var csc = Regex.Match(output, @"(\d+) ms\s+Csc\s+(\d+) calls");
    var note = (m.Success ? $"versioning task {m.Groups[1].Value} ms / {m.Groups[2].Value} calls" : "versioning task: n/a") +
               (csc.Success ? $", Csc {csc.Groups[1].Value} ms / {csc.Groups[2].Value} calls" : ", Csc: none");
    var i = timings.FindLastIndex(t => t.Item1.StartsWith(label));
    if (i >= 0) timings[i] = (timings[i].Item1, timings[i].Item2, note);
}

void Step(string name) => Console.WriteLine($"\n== {name}");

void Check(bool ok, string what)
{
    checks++;
    Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {what}");
    if (!ok) failures.Add(what);
}

void Require(bool ok, string what, string output)
{
    if (ok) return;
    Console.WriteLine(string.Join('\n', output.Split('\n').TakeLast(60)));
    throw new FatalException(what);
}

void Git(params string[] arguments)
{
    var r = Run(mono, "git", arguments);
    if (r.ExitCode != 0) throw new FatalException($"git {string.Join(' ', arguments)} failed: {r.Output}");
}

string Dll(Proj p, string tfm) => Path.Combine(mono, p.IsTest ? "tests" : "src", p.Name, "bin", "Release", tfm, p.Name + ".dll");

string VerAt(Proj p, string tfm)
{
    var dll = Dll(p, tfm);
    if (!File.Exists(dll)) return "<missing>";
    var v = FileVersionInfo.GetVersionInfo(dll).ProductVersion ?? "<none>";
    var plus = v.IndexOf('+');
    return plus >= 0 ? v[..plus] : v;
}

string AsmVerAt(Proj p, string tfm)
{
    var dll = Dll(p, tfm);
    return File.Exists(dll) ? System.Reflection.AssemblyName.GetAssemblyName(dll).Version?.ToString() ?? "<none>" : "<missing>";
}

List<(string Name, string Version)> AssemblyRefs(Proj p, string tfm)
{
    var result = new List<(string, string)>();
    var dll = Dll(p, tfm);
    if (!File.Exists(dll)) return result;
    using var stream = File.OpenRead(dll);
    using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
    var md = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
    foreach (var h in md.AssemblyReferences) { var r = md.GetAssemblyReference(h); result.Add((md.GetString(r.Name), r.Version.ToString())); }
    return result;
}

void WriteRepo()
{
    void W(string rel, string content)
    {
        var path = Path.Combine(mono, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
    W("nuget.config", $"""
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <packageSources>
            <clear />
            <add key="stress" value="{feed}" />
            <add key="nuget" value="https://api.nuget.org/v3/index.json" />
          </packageSources>
        </configuration>
        """);
    W("Directory.Build.props", $"""
        <Project>
          <PropertyGroup>
            <ImplicitUsings>disable</ImplicitUsings>
            <Nullable>disable</Nullable>
            <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Mister.Version" Version="{pkgVersion}" PrivateAssets="all" />
          </ItemGroup>
        </Project>
        """);
    W(".gitignore", "bin/\nobj/\n");
    foreach (var p in all)
    {
        var dir = (p.IsTest ? "tests/" : "src/") + p.Name;
        var tfm = p.Multi ? "<TargetFrameworks>net8.0;net10.0</TargetFrameworks>" : "<TargetFramework>net10.0</TargetFramework>";
        var extra = (p.IsApp ? "<OutputType>Exe</OutputType>" : "") +
                    (p.NonPackable ? $"<IsPackable>false</IsPackable><Version>{p.ExplicitVersion}</Version>" : "") +
                    (p.IsTest ? "<IsPackable>false</IsPackable><IsTestProject>true</IsTestProject>" : "") +
                    (!p.IsTest && !p.NonPackable ? "<IsPackable>true</IsPackable>" : "");
        var refs = string.Join("", p.Deps.Select(d => $"<ProjectReference Include=\"../../src/{d.Name}/{d.Name}.csproj\" />"));
        W($"{dir}/{p.Name}.csproj", $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>{tfm}{extra}</PropertyGroup><ItemGroup>{refs}</ItemGroup></Project>\n");
        var ns = p.Name.Replace(".", "_");
        var sum = p.Deps.Count == 0 ? "1" : "1 + " + string.Join(" + ", p.Deps.Select(d => $"{d.Name.Replace(".", "_")}.C.V"));
        W($"{dir}/{(p.IsTest ? "T" : "C")}.cs", p.IsApp
            ? $"namespace {ns} {{ public static class C {{ public static int V => {sum}; public static void Main() => System.Console.WriteLine(V); }} }}\n"
            : $"namespace {ns} {{ public static class C {{ public static int V => {sum}; }} }}\n");
    }
    W("Stress.slnx", "<Solution>\n" + string.Join("\n", all.Select(p => $"  <Project Path=\"{(p.IsTest ? "tests" : "src")}/{p.Name}/{p.Name}.csproj\" />")) + "\n</Solution>\n");
}

static HashSet<Proj> Closure(Proj p)
{
    var seen = new HashSet<Proj>();
    var stack = new Stack<Proj>(p.Deps);
    while (stack.Count > 0) { var d = stack.Pop(); if (seen.Add(d)) foreach (var x in d.Deps) stack.Push(x); }
    return seen;
}

static int Depth(Proj p) => p.Deps.Count == 0 ? 0 : 1 + p.Deps.Max(Depth);

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
    using var p = Process.Start(psi)!;
    var stdout = p.StandardOutput.ReadToEndAsync();
    var stderr = p.StandardError.ReadToEndAsync();
    p.WaitForExit();
    return (p.ExitCode, stdout.Result + stderr.Result);
}

static (int ExitCode, string Output) Dotnet(string cwd, params string[] arguments) => Run(cwd, "dotnet", arguments);

static (int ExitCode, string Output) RunWithRollForward(string cwd, string file, params string[] arguments)
{
    Environment.SetEnvironmentVariable("DOTNET_ROLL_FORWARD", "Major");
    try { return Run(cwd, file, arguments); }
    finally { Environment.SetEnvironmentVariable("DOTNET_ROLL_FORWARD", null); }
}

static (string Version, List<(string Group, string Id, string Version)> Deps) NuspecFull(string outDir, string id)
{
    if (!Directory.Exists(outDir)) return ("<missing>", new());
    var nupkg = Directory.GetFiles(outDir, id + ".*.nupkg").FirstOrDefault(f => Regex.IsMatch(Path.GetFileName(f), $@"^{Regex.Escape(id)}\.\d"));
    if (nupkg == null) return ("<missing>", new());
    using var zip = ZipFile.OpenRead(nupkg);
    using var s = zip.Entries.First(e => e.FullName.EndsWith(".nuspec")).Open();
    var doc = XDocument.Load(s);
    var version = doc.Descendants().First(e => e.Name.LocalName == "version").Value;
    var deps = doc.Descendants().Where(e => e.Name.LocalName == "dependency")
        .Select(e => ((string?)e.Parent?.Attribute("targetFramework") ?? "(any)", (string)e.Attribute("id")!,
            ((string)e.Attribute("version")!).Trim('[', ']', '(', ')', ' ').Split(',')[0]))
        .ToList();
    return (version, deps);
}

static void TryDelete(string dir) { try { Directory.Delete(dir, true); } catch { } }

sealed class Proj(string name, int layer)
{
    public string Name { get; } = name;
    public int Layer { get; } = layer;
    public bool Multi { get; set; }
    public bool IsApp { get; set; }
    public bool IsTest { get; set; }
    public bool NonPackable { get; set; }
    public string? ExplicitVersion { get; set; }
    public List<Proj> Deps { get; } = new();
    public string[] Tfms => Multi ? new[] { "net8.0", "net10.0" } : new[] { "net10.0" };
    public override string ToString() => Name;
}

sealed class FatalException(string message) : Exception(message);
