# Taking Cake 7 to Eleven

Sample repository for the [.NET Skåne](https://www.meetup.com/net-skane/) meetup talk **Taking Cake 7 to Eleven: Pipelines as Code with C# and .NET 11**, presented at the [Øredev 20th anniversary meetup](https://www.meetup.com/net-skane/events/316270872/).

It shows a complete build pipeline (clean, restore, build, test, pack and publish artifacts) written as a single
file-based C# app using [Cake.Sdk](https://cakebuild.net/docs/running-builds/runners/cake-sdk). The same `cake.cs`
runs on your machine and on GitHub Actions, Azure Pipelines, AppVeyor, Bitbucket Pipelines and RWX.

- Meetup: [Øredev 20th anniversary meetup](https://www.meetup.com/net-skane/events/316270872/)
- Cake: [cakebuild.net](https://cakebuild.net)

> [!NOTE]
> .NET 11 and Cake 7 aren't released yet. This repository uses the .NET 11 RC3 SDK and a Cake.Sdk 7 pre-release from
> the [Cake pre-release feed](https://cakebuild.net/docs/getting-started/testing-pre-releases), configured in
> `nuget.config`. Versions are pinned in `global.json`.

## Prerequisites

- [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0) matching the version in `global.json`

That's it. Cake.Sdk is restored automatically the first time you run the build.

## Run the build

```bash
dotnet cake.cs
```

On Linux and macOS you can also run it directly thanks to the shebang:

```bash
chmod +x cake.cs
./cake.cs
```

Without a `--target`, the build detects the CI provider it runs on and picks the matching target
(`Default` when running locally), so the CI YAML files stay minimal. Pick a target or configuration with arguments:

```bash
dotnet cake.cs --target=Pack
dotnet cake.cs --target=Pack --configuration=Debug
dotnet cake.cs --target=Pack --package-version=2.3.4
```

`--package-version` defaults to a UTC date-based version (`yyyy.M.d.n`, for example `2026.11.2.1`).
On GitHub Actions `n` is the workflow run number; locally it is `1`. Pass `--package-version` to override.

| Target                                | Does                                                                                                      |
| ------------------------------------- | --------------------------------------------------------------------------------------------------------- |
| `Clean`                               | Cleans `./artifacts` and `src/**/{obj,bin}/{Configuration}`                                               |
| `Restore`                             | Restores `src/HelloWorldApp.slnx`                                                                         |
| `Build`                               | Builds the solution                                                                                       |
| `Test`                                | Runs tests on Microsoft.Testing.Platform with code coverage and TRX reports to `./artifacts/test-results` |
| `Pack`                                | Packs NuGet packages, including the `helloworld` .NET tool, to `./artifacts/nuget`                        |
| `Upload-Artifact`                     | Uploads artifacts using the current CI provider (skipped locally)                                         |
| `PublishCakeAsContainerArchive`       | Publishes `cake.cs` as a container image archive to `./artifacts/cake-container.tar.gz`                   |
| `PublishCakeAsContainerLocalRegistry` | Publishes `cake.cs` as `cake-sdk:latest` to the local wslc, Podman or Docker registry, then runs the build in that image |
| `PublishCakeAsContainerRegistry`      | Publishes `cake.cs` to a container registry (`docker.io/devlead/dotnetskane:latest` by default)           |
| `Default`                             | Runs `Test`, used when building locally                                                                   |
| `GitHubActions`                       | Runs `Upload-Artifact` on GitHub Actions                                                                  |
| `AzurePipelines`                      | Runs `Upload-Artifact` on Azure Pipelines                                                                 |
| `AppVeyor`                            | Runs `Upload-Artifact` on AppVeyor                                                                        |
| `Rwx`                                 | Runs `Upload-Artifact` on RWX                                                                             |
| `BitbucketPipelines`                  | Runs `Upload-Artifact` on Bitbucket Pipelines (artifacts are declared in the YAML there)                  |

## Run the build from a container

The build itself can be published as a container image, using the .NET SDK's built-in container support with
`mcr.microsoft.com/dotnet/sdk:11.0-resolute-aot` as the base image. Then anyone with a container runtime can build
the repository without installing the .NET SDK or Cake.

Publish `cake.cs` to your local registry as `cake-sdk:latest`. The first of `wslc`, `podman` or `docker` found
on the `PATH` is used. The target also mounts the repository and runs the build inside that image:

```bash
dotnet cake.cs --target=PublishCakeAsContainerLocalRegistry
```

Publish the meetup image used by CI (defaults: `docker.io/devlead/dotnetskane:latest`):

```bash
dotnet cake.cs --target=PublishCakeAsContainerRegistry
```

Override registry settings with `--registry`, `--repository`, `--imageTag`, and `--baseImage` as needed.
[`.github/workflows/cake-container.yml`](.github/workflows/cake-container.yml) runs the same `cake.cs` via
`docker.io/devlead/dotnetskane:latest`.

To share the image without a registry, publish it as an archive instead and load it with `docker load` or
`podman load`:

```bash
dotnet cake.cs --target=PublishCakeAsContainerArchive
docker load -i artifacts/cake-container.tar.gz
```

## What's in the box

```text
.
├── cake.cs                     # The build, a file-based C# app using Cake.Sdk
├── build/
│   └── BuildData.cs            # Typed build state, pulled in with #:include
├── global.json                 # Pins the .NET SDK and Cake.Sdk, enables Microsoft.Testing.Platform
├── nuget.config                # nuget.org + Cake pre-release feed
├── src/
│   ├── HelloWorldApp.slnx
│   ├── HelloWorldApp.Common/   # Class library
│   ├── HelloWorldApp.Tests/    # xunit v3 tests on Microsoft.Testing.Platform
│   └── HelloWorldApp.Tool/     # Console app packed as a .NET tool
├── .github/workflows/cake.yml
├── .github/workflows/cake-container.yml
├── azure-pipelines.yml
├── appveyor.yml
├── bitbucket-pipelines.yml
└── .rwx/cake.yml
```

### Highlights

- **File-based build**: `cake.cs` uses the `#:sdk` and `#:include` directives, no project file needed.
- **Typed build state**: settings are created once in `Setup` and passed to each task as a `BuildData` record,
  see [Sharing build state](https://cakebuild.net/docs/writing-builds/sharing-build-state).
- **Microsoft.Testing.Platform**: tests use `xunit.v3.mtp-v2` with the `CodeCoverage` and `TrxReport` extensions,
  no `Microsoft.NET.Test.Sdk` or VSTest adapter needed.
- **Build as a container**: `cake.cs` can be published as a container image with `dotnet publish`, no
  `Dockerfile` needed.
- **.NET 11 and C# 15**: all projects target `net11.0`.

## CI

Every provider installs the .NET SDK from `global.json` (Bitbucket uses the `mcr.microsoft.com/dotnet/sdk:11.0`
container image) and runs the same `cake.cs`.

| Provider                       | File                                                                     |
| ------------------------------ | ------------------------------------------------------------------------ |
| GitHub Actions                 | [.github/workflows/cake.yml](.github/workflows/cake.yml)                 |
| GitHub Actions (Cake container)| [.github/workflows/cake-container.yml](.github/workflows/cake-container.yml) |
| Azure Pipelines                | [azure-pipelines.yml](azure-pipelines.yml)                               |
| AppVeyor                       | [appveyor.yml](appveyor.yml)                                             |
| Bitbucket Pipelines            | [bitbucket-pipelines.yml](bitbucket-pipelines.yml)                       |
| RWX                            | [.rwx/cake.yml](.rwx/cake.yml)                                           |

Generate one for your own repository with the Cake template:

```bash
dotnet new install Cake.Template
dotnet new cakeci --Provider github
```

## Formatting

The solution and the file-based build are both formatted with `dotnet format`:

```bash
dotnet format src/HelloWorldApp.slnx
dotnet format cake.cs
```

## License

[MIT](LICENSE)
