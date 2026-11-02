public record BuildData(
    string Configuration,
    string Version,
    bool IsLocalBuild,
    FilePath Solution,
    DirectoryPath Artifacts)
{
    public DirectoryPath TestResults => Artifacts.Combine("test-results");

    public DirectoryPath NuGetOutput => Artifacts.Combine("nuget");

    public DotNetMSBuildSettings MSBuildSettings { get; } =
        new DotNetMSBuildSettings()
            .SetVersion(Version)
            .SetContinuousIntegrationBuild(!IsLocalBuild);
}
