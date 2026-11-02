#!/usr/bin/env dotnet
#:sdk Cake.Sdk
#:include build/**/*.cs

Setup(context =>
{
    var packageVersion = Argument(
            "package-version",
            FormattableString.Invariant(
                // Example: 2026.11.2.1
                $"{DateTime.UtcNow:yyyy.M.d}.{(
                    BuildSystem.IsRunningOnGitHubActions
                        ? BuildSystem.GitHubActions.Environment.Workflow.RunNumber
                        : 1)}"
            )
        );

    return new BuildData(
        Configuration: Argument("configuration", "Release"),
        Version: packageVersion,
        IsLocalBuild: BuildSystem.IsLocalBuild,
        Solution: File("./src/HelloWorldApp.slnx"),
        Artifacts: MakeAbsolute(Directory("./artifacts")));

});

Task("Clean")
    .Does<BuildData>(
        static (context, data) =>
        {
            CleanDirectory(data.Artifacts);
            CleanDirectories(
               $$"""
                src/**/{obj,bin}/{{data.Configuration}}
                """);
        }
            );

Task("Restore")
    .IsDependentOn("Clean")
    .Does<BuildData>(
        static (context, data) => DotNetRestore(
            data.Solution.FullPath,
            new DotNetRestoreSettings
            {
                MSBuildSettings = data.MSBuildSettings,
            }));

Task("Build")
    .IsDependentOn("Restore")
    .Does<BuildData>(
        static (context, data) => DotNetBuild(
            data.Solution.FullPath,
            new DotNetBuildSettings
            {
                Configuration = data.Configuration,
                MSBuildSettings = data.MSBuildSettings,
                NoRestore = true,
            }));

Task("Test")
    .IsDependentOn("Build")
    .Does<BuildData>(
        static (context, data) => DotNetTest(
            data.Solution.FullPath,
            new DotNetTestSettings
            {
                Configuration = data.Configuration,
                MSBuildSettings = data.MSBuildSettings,
                NoRestore = true,
                NoBuild = true,
                PathType = DotNetTestPathType.Solution,
                ArgumentCustomization = args => args
                    .Append("--coverage")
                    .Append("--coverage-output-format cobertura")
                    .Append("--report-trx")
                    .AppendSwitchQuoted(
                        "--results-directory",
                        data.TestResults.FullPath),
            }));

Task("Pack")
    .IsDependentOn("Test")
    .Does<BuildData>(
        static (context, data) => DotNetPack(
            data.Solution.FullPath,
            new DotNetPackSettings
            {
                Configuration = data.Configuration,
                MSBuildSettings = data.MSBuildSettings,
                NoBuild = true,
                NoRestore = true,
                OutputDirectory = data.NuGetOutput,
            }));

Task("Upload-Artifact")
    .IsDependentOn("Pack")
    .WithCriteria(!BuildSystem.IsLocalBuild)
    .Does<BuildData>(
        static async (context, data) =>
        {
            var packages = context.GetFiles(
                $"{data.NuGetOutput.FullPath}/*.nupkg");

            switch (BuildSystem.Provider)
            {
                case BuildProvider.GitHubActions:
                    await GitHubActions.Commands.UploadArtifact(
                        data.Artifacts, "artifacts");
                    break;

                case BuildProvider.AzurePipelines:
                    AzurePipelines.Commands.UploadArtifactDirectory(
                        data.Artifacts, "artifacts");
                    break;

                case BuildProvider.AppVeyor:
                    foreach (var package in packages)
                        AppVeyor.UploadArtifact(package);
                    break;

                case BuildProvider.Rwx:
                    foreach (var package in packages)
                        Rwx.Commands.UploadArtifact(package);
                    break;

                default:
                    context.Information(
                        $"No artifact upload for {BuildSystem.Provider}");
                    break;
            }
        });

Task("PublishCakeAsContainerArchive")
    .IsDependentOn("Clean")
    .Does<BuildData>(
        static (context, data) =>
        {
            DotNetPublish(
                "cake.cs",
                new DotNetPublishSettings
                {
                    Configuration = data.Configuration,
                    MSBuildSettings = data.MSBuildSettings,
                    OutputDirectory = data.Artifacts
                }.WithContainerArchive(
                    archiveOutputPath: data
                                        .Artifacts
                                        .CombineWithFilePath("cake-container.tar.gz"),
                    baseImage: "mcr.microsoft.com/dotnet/sdk:11.0-resolute-aot"
                )
            );
        });

Task("PublishCakeAsContainerLocalRegistry")
    .IsDependentOn("Clean")
    .Does<BuildData>(
        static (context, data) =>
        {

            var containerExecutable = context
                .Tools
                .Resolve(
                    ["wslc.exe", "podman", "podman.exe", "docker", "docker.exe"]
                ) ?? throw new InvalidOperationException("No suitable container executable found");

            var containerToolName = containerExecutable.GetFilenameWithoutExtension().FullPath;

            var localRegistry = containerToolName switch
            {
                "wslc" => DotNetContainerLocalRegistry.Wslc,
                "podman" => DotNetContainerLocalRegistry.Podman,
                "docker" => DotNetContainerLocalRegistry.Docker,
                _ => throw new InvalidOperationException("No suitable local registry found")
            };

            string
                repository = "cake-sdk",
                imageTag = "latest";

            Information($"Publishing to local registry: {localRegistry} as {repository}:{imageTag}");

            DotNetPublish(
                "cake.cs",
                new DotNetPublishSettings
                {
                    Configuration = data.Configuration,
                    MSBuildSettings = data.MSBuildSettings,
                    OutputDirectory = data.Artifacts
                }.WithContainerLocalRegistry(
                    localRegistry: localRegistry,
                    repository: repository,
                    imageTag: imageTag,
                    baseImage: "mcr.microsoft.com/dotnet/sdk:11.0-resolute-aot"
                )
            );

            var containerCommandSettings = new CommandSettings
            {
                ToolName = containerToolName,
                ToolExecutableNames = [containerExecutable.FullPath],
                ToolPath = containerExecutable
            };

            Information($"Checking if image {repository}:{imageTag} exists");

            Command(
                containerCommandSettings,
                arguments:
                $"""
                image list --filter "reference={repository}:{imageTag}"
                """
            );

            Information($"Building src directory using container image {repository}:{imageTag}");

            Command(
                containerCommandSettings,
                arguments:
                $"""
                run --rm -it -v "{Context.Environment.WorkingDirectory}:/repo" -w /repo {repository}:{imageTag}"
                """
            );
        });

Task("PublishCakeAsContainerRegistry")
    .IsDependentOn("Clean")
    .Does<BuildData>(
        static (context, data) =>
        {
            string
                registry = Argument("registry", "docker.io"),
                repository = Argument("repository", "devlead/dotnetskane"),
                imageTag = Argument("imageTag", "latest"),
                baseImage = Argument("baseImage", "mcr.microsoft.com/dotnet/sdk:11.0-resolute-aot");

            Information($"Publishing image to {registry}/{repository}:{imageTag} using baseImage {baseImage}");

            DotNetPublish(
                "cake.cs",
                new DotNetPublishSettings
                {
                    Configuration = data.Configuration,
                    MSBuildSettings = data.MSBuildSettings,
                    OutputDirectory = data.Artifacts,
                    Container = new()
                    {
                        User = "root"
                    }
                }.WithContainerRegistry(
                    registry: "docker.io",
                    repository: repository,
                    imageTag: imageTag,
                    baseImage: baseImage
                )
            );
        });

Task("GitHubActions")
    .IsDependentOn("Upload-Artifact");

Task("AzurePipelines")
    .IsDependentOn("Upload-Artifact");

Task("AppVeyor")
    .IsDependentOn("Upload-Artifact");

Task("Rwx")
    .IsDependentOn("Upload-Artifact");

Task("BitbucketPipelines")
    .IsDependentOn("Upload-Artifact");

Task("Default")
    .IsDependentOn("Test");

RunTarget(Argument("target", BuildSystem.Provider switch
{
    BuildProvider.GitHubActions
        => "GitHubActions",
    BuildProvider.AzurePipelines
        => "AzurePipelines",
    BuildProvider.AppVeyor
        => "AppVeyor",
    BuildProvider.Rwx
        => "Rwx",
    BuildProvider.BitbucketPipelines
        => "BitbucketPipelines",
    _ => "Default",
}));
