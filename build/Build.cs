using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Utilities.Collections;

/// <summary>
/// NUKE build for the SatisfactorySaveNet fork. Targets:
///   <c>./build.sh Compile</c>  — restore + compile
///   <c>./build.sh Test</c>     — restore + compile + run NUnit tests
///   <c>./build.sh Pack</c>     — produce nupkg(s) in <c>artifacts/packages</c>
///   <c>./build.sh Push --nuget-source &lt;url&gt; --nuget-api-key &lt;pat&gt;</c>
///                              — publish nupkgs (CI uses GitHub Packages on tag)
///
/// Versioning is owned by Nerdbank.GitVersioning via <c>version.json</c> at the
/// repo root — the build doesn't override <c>$(Version)</c>; MSBuild reads it
/// from NB.GV which derives from git height + <c>publicReleaseRefSpec</c>.
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
            // $(Version) is set by Nerdbank.GitVersioning — no manual override.
            DotNetTasks.DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
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
