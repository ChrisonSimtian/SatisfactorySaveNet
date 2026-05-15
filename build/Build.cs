using System;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

/// <summary>
/// NUKE build for the SatisfactorySaveNet fork. Targets:
///   <c>./build.sh Compile</c>  — restore + compile
///   <c>./build.sh Test</c>     — restore + compile + run NUnit tests
///   <c>./build.sh Pack</c>     — produce nupkg(s) in <c>artifacts/packages</c>
///   <c>./build.sh Push --nuget-source &lt;url&gt; --nuget-api-key &lt;pat&gt;</c>
///                              — publish nupkgs (CI uses GitHub Packages on tag)
///
/// Versioning: tag-driven. On CI, <c>GITHUB_REF=refs/tags/vX.Y.Z</c> sets the
/// package version to <c>X.Y.Z</c>. Untagged builds (local dev, branch CI) get
/// a <c>0.0.0-ci.&lt;timestamp&gt;</c> prerelease so nupkgs are unique but obviously
/// not-for-release.
/// </summary>
class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.Pack);

    [Parameter("Configuration to build — Debug for local, Release for CI.")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Solution] readonly Solution Solution = null!;

    [Parameter("NuGet feed URL to publish to (e.g. https://nuget.pkg.github.com/<owner>/index.json).")]
    readonly string? NuGetSource;

    [Parameter("API key / PAT for the NuGet feed.")]
    [Secret]
    readonly string? NuGetApiKey;

    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    AbsolutePath PackagesDirectory => ArtifactsDirectory / "packages";

    // Resolved once per build invocation so every produced package within a
    // single run shares the same version string. A property would recompute
    // on every access — fine for tag builds but it drifts the timestamp
    // suffix between sequential pack calls on non-tag builds.
    static readonly Lazy<string> ResolvedVersion = new(ResolveVersion);
    string Version => ResolvedVersion.Value;

    static string ResolveVersion()
    {
        // CI publishes on tag push — GitHub Actions sets GITHUB_REF=refs/tags/v1.2.3.
        var gitRef = Environment.GetEnvironmentVariable("GITHUB_REF");
        const string tagPrefix = "refs/tags/v";
        if (!string.IsNullOrEmpty(gitRef) && gitRef.StartsWith(tagPrefix, StringComparison.Ordinal))
            return gitRef[tagPrefix.Length..];

        // Untagged builds: CI prerelease keyed off the run number when present,
        // "local" otherwise. Same value for all packs in this run.
        var runNumber = Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER");
        return string.IsNullOrEmpty(runNumber)
            ? "0.0.0-ci.local"
            : $"0.0.0-ci.{runNumber}";
    }

    Target Clean => _ => _
        .Executes(() =>
        {
            ArtifactsDirectory.CreateOrCleanDirectory();
        });

    Target Restore => _ => _
        .DependsOn(Clean)
        .Executes(() =>
        {
            DotNetTasks.DotNetRestore(s => s.SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetTasks.DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetVersion(Version)
                .EnableNoRestore());
        });

    Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            DotNetTasks.DotNetTest(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .EnableNoBuild()
                .EnableNoRestore());
        });

    Target Pack => _ => _
        .DependsOn(Compile)
        .Produces(PackagesDirectory / "*.nupkg")
        .Executes(() =>
        {
            // Only the two library projects are packable; the test + benchmark
            // projects opt out via their own csproj IsPackable=false.
            string[] packable =
            {
                "SatisfactorySaveNet",
                "SatisfactorySaveNet.Abstracts",
            };
            foreach (var name in packable)
            {
                var project = Solution.GetProject(name)
                    ?? throw new InvalidOperationException($"Project '{name}' not found in solution.");
                DotNetTasks.DotNetPack(s => s
                    .SetProject(project)
                    .SetConfiguration(Configuration)
                    .SetVersion(Version)
                    .SetOutputDirectory(PackagesDirectory)
                    .EnableNoBuild()
                    .EnableNoRestore());
            }
        });

    Target Push => _ => _
        .DependsOn(Pack)
        .Requires(() => NuGetSource)
        .Requires(() => NuGetApiKey)
        .Executes(() =>
        {
            PackagesDirectory.GlobFiles("*.nupkg")
                .ForEach(package => DotNetTasks.DotNetNuGetPush(s => s
                    .SetTargetPath(package)
                    .SetSource(NuGetSource!)
                    .SetApiKey(NuGetApiKey!)
                    .EnableSkipDuplicate()));
        });
}
