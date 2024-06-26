// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MSStore.API;
using MSStore.API.Packaged;
using MSStore.API.Packaged.Models;
using MSStore.CLI.Helpers;
using MSStore.CLI.Services;
using MSStore.CLI.Services.ElectronManager;
using Spectre.Console;

namespace MSStore.CLI.ProjectConfigurators
{
    internal class ElectronProjectConfigurator : NodeBaseProjectConfigurator
    {
        private readonly IElectronManifestManager _electronManifestManager;
        private ElectronManifest? _electronManifest;

        public ElectronProjectConfigurator(IExternalCommandExecutor externalCommandExecutor, IElectronManifestManager electronManifestManager, IBrowserLauncher browserLauncher, IConsoleReader consoleReader, IZipFileManager zipFileManager, IFileDownloader fileDownloader, IAzureBlobManager azureBlobManager, IEnvironmentInformationService environmentInformationService, ILogger<ElectronProjectConfigurator> logger)
            : base(externalCommandExecutor, browserLauncher, consoleReader, zipFileManager, fileDownloader, azureBlobManager, environmentInformationService, logger)
        {
            _electronManifestManager = electronManifestManager ?? throw new ArgumentNullException(nameof(electronManifestManager));
        }

        public override string ToString() => "Electron";

        public override string[] PackageFilesExtensionInclude => new[]
        {
            ".appx"
        };
        public override string[]? PackageFilesExtensionExclude { get; }
        public override SearchOption PackageFilesSearchOption { get; } = SearchOption.TopDirectoryOnly;
        public override PublishFileSearchFilterStrategy PublishFileSearchFilterStrategy { get; } = PublishFileSearchFilterStrategy.All;
        public override string OutputSubdirectory { get; } = "dist";
        public override string DefaultInputSubdirectory
        {
            get
            {
                return _electronManifest?.Build?.Directories?.Output ?? "dist";
            }
        }

        public string DefaultBuildResources
        {
            get
            {
                return _electronManifest?.Build?.Directories?.BuildResources ?? "build";
            }
        }

        public override IEnumerable<BuildArch>? DefaultBuildArchs => new[]
        {
            BuildArch.X64,
            BuildArch.Arm64
        };

        public override bool PackageOnlyOnWindows => true;

        public override AllowTargetFutureDeviceFamily[] AllowTargetFutureDeviceFamilies { get; } = new[]
        {
            AllowTargetFutureDeviceFamily.Desktop
        };

        public override async Task<bool> CanConfigureAsync(string pathOrUrl, CancellationToken ct)
        {
            if (!await base.CanConfigureAsync(pathOrUrl, ct))
            {
                return false;
            }

            var (projectRootPath, _) = GetInfo(pathOrUrl);

            if (IsYarn(projectRootPath))
            {
                if (!await RunYarnInstallAsync(projectRootPath, ct))
                {
                    return false;
                }

                return !await YarnPackageExistsAsync(projectRootPath, "react-native", true, ct);
            }
            else
            {
                if (!await RunNpmInstallAsync(projectRootPath, ct))
                {
                    return false;
                }

                return !await NpmPackageExistsAsync(projectRootPath, "react-native", true, ct);
            }
        }

        public override async Task<(int returnCode, DirectoryInfo? outputDirectory)> ConfigureAsync(string pathOrUrl, DirectoryInfo? output, string publisherDisplayName, DevCenterApplication app, Version? version, IStorePackagedAPI storePackagedAPI, CancellationToken ct)
        {
            var (projectRootPath, electronProjectFile) = GetInfo(pathOrUrl);

            if (!await InstallDependencyAsync(projectRootPath, "electron-builder", ct))
            {
                return (-1, null);
            }

            _electronManifest = await UpdateManifestAsync(electronProjectFile, app, publisherDisplayName, version, _electronManifestManager, ct);

            CopyDefaultImages(projectRootPath);

            AnsiConsole.WriteLine($"Electron project '{electronProjectFile.FullName}' is now configured to build to the Microsoft Store!");
            AnsiConsole.MarkupLine("For more information on building your Electron project to the Microsoft Store, see [link]https://www.electron.build/configuration/appx#how-to-publish-your-electron-app-to-the-windows-app-store[/]");

            return (0, output);
        }

        private void CopyDefaultImages(DirectoryInfo projectRootPath)
        {
            var defaultAssets = new Dictionary<string, string>()
            {
                {
                    "SampleAppx.50x50.png", "StoreLogo.png"
                },
                {
                    "SampleAppx.150x150.png", "Square150x150Logo.png"
                },
                {
                    "SampleAppx.44x44.png", "Square44x44Logo.png"
                },
                {
                    "SampleAppx.310x150.png", "Wide310x150Logo.png"
                }
            };

            var appxAssetsFolder = GetDefaultAssetsAppxFolder();
            if (appxAssetsFolder == null)
            {
                return;
            }

            var appxFolder = Path.Combine(projectRootPath.FullName, DefaultBuildResources, "appx");
            if (!Directory.Exists(appxFolder))
            {
                Directory.CreateDirectory(appxFolder);
            }

            foreach (var image in defaultAssets)
            {
                var originImagePath = Path.Combine(appxAssetsFolder, image.Key);
                if (File.Exists(originImagePath))
                {
                    var destinationImagePath = Path.Combine(appxFolder, image.Value);
                    File.Copy(originImagePath, destinationImagePath, true);
                }
            }
        }

        public override Task<List<string>?> GetAppImagesAsync(string pathOrUrl, CancellationToken ct)
        {
            var appxFolder = Path.Combine(pathOrUrl, DefaultBuildResources, "appx");

            // https://www.electron.build/configuration/appx#appx-assets
            var fileNames = new List<string>
            {
                "StoreLogo",
                "Square150x150Logo",
                "Square44x44Logo",
                "Wide310x150Logo",
                "BadgeLogo",
                "LargeTile",
                "SmallTile",
                "SplashScreen"
            };

            if (!Directory.Exists(appxFolder))
            {
                return Task.FromResult<List<string>?>(null);
            }

            return Task.FromResult<List<string>?>(
                Directory.GetFiles(appxFolder)
                    .Where(f => fileNames
                        .Any(n => Path.GetFileNameWithoutExtension(f)
                            .Equals(n, StringComparison.OrdinalIgnoreCase)))
                    .ToList());
        }

        public override Task<List<string>?> GetDefaultImagesAsync(string pathOrUrl, CancellationToken ct)
        {
            var appxAssetsFolder = GetDefaultAssetsAppxFolder();
            if (Directory.Exists(appxAssetsFolder))
            {
                var appxAssetsDir = new DirectoryInfo(appxAssetsFolder);
                return Task.FromResult<List<string>?>(appxAssetsDir.GetFiles().Select(f => f.FullName).ToList());
            }

            return Task.FromResult<List<string>?>(null);
        }

        private static string? GetDefaultAssetsAppxFolder()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var winCodeSign = Path.Combine(appData, "electron-builder", "Cache", "winCodeSign");
            var winCodeSignDir = new DirectoryInfo(winCodeSign);
            if (winCodeSignDir.Exists)
            {
                var winCodeSignDirs = winCodeSignDir.GetDirectories("winCodeSign-*", SearchOption.TopDirectoryOnly);
                if (winCodeSignDirs.Length > 0)
                {
                    var winCodeSignDirInfo = winCodeSignDirs.OrderByDescending(d => d.Name).First();
                    return Path.Combine(winCodeSignDirInfo.FullName, "appxAssets");
                }
            }

            return null;
        }

        internal static async Task<ElectronManifest> UpdateManifestAsync(FileInfo electronProjectFile, DevCenterApplication app, string publisherDisplayName, Version? version, IElectronManifestManager electronManifestManager, CancellationToken ct)
        {
            var electronManifest = await electronManifestManager.LoadAsync(electronProjectFile, ct);

            electronManifest.Build ??= new ElectronManifestBuild();
            electronManifest.Build.Windows ??= new ElectronManifestBuildWindows();
            if (electronManifest.Build.Windows.Targets is JsonArray targets)
            {
                if (targets.All(t => t?.ToString() != "appx"))
                {
                    targets.Add(JsonNode.Parse("\"appx\""));
                }
            }
            else if (electronManifest.Build.Windows.Targets is JsonValue jsonValue)
            {
                if (jsonValue.TryGetValue(out string? existingTarget))
                {
                    electronManifest.Build.Windows.Targets = new JsonArray
                    {
                        JsonNode.Parse("\"appx\""), JsonNode.Parse($"\"{existingTarget}\"")
                    };
                }
            }
            else
            {
                electronManifest.Build.Windows.Targets = new JsonArray
                {
                    JsonNode.Parse("\"appx\"")
                };
            }

            electronManifest.Build.Appx ??= new ElectronManifestBuildAppX();
            electronManifest.Build.Appx.PublisherDisplayName = publisherDisplayName;
            electronManifest.Build.Appx.DisplayName = app.PrimaryName;
            electronManifest.Build.Appx.Publisher = app.PublisherName;
            electronManifest.Build.Appx.IdentityName = app.PackageIdentityName;
            electronManifest.Build.Appx.ApplicationId = "App";
            electronManifest.MSStoreCLIAppID = app.Id;

            if (version != null)
            {
                electronManifest.Version = version.ToVersionString(true);
            }

            await electronManifestManager.SaveAsync(electronManifest, electronProjectFile, ct);

            return electronManifest;
        }

        public override async Task<(int returnCode, DirectoryInfo? outputDirectory)> PackageAsync(string pathOrUrl, DevCenterApplication? app, IEnumerable<BuildArch>? buildArchs, Version? version, DirectoryInfo? output, IStorePackagedAPI storePackagedAPI, CancellationToken ct)
        {
            var (projectRootPath, electronProjectFile) = GetInfo(pathOrUrl);

            bool isYarn = IsYarn(projectRootPath);

            if (app == null)
            {
                if (isYarn)
                {
                    var yarnInstall = await RunYarnInstallAsync(projectRootPath, ct);

                    if (!yarnInstall)
                    {
                        throw new MSStoreException("Failed to run 'yarn install'.");
                    }
                }
                else
                {
                    var npmInstall = await RunNpmInstallAsync(projectRootPath, ct);

                    if (!npmInstall)
                    {
                        throw new MSStoreException("Failed to run 'npm install'.");
                    }
                }
            }

            if (version != null)
            {
                var electronManifest = await _electronManifestManager.LoadAsync(electronProjectFile, ct);
                var versionStr = version.ToVersionString(true);
                if (electronManifest.Version != versionStr)
                {
                    electronManifest.Version = versionStr;
                    await _electronManifestManager.SaveAsync(electronManifest, electronProjectFile, ct);
                }
            }

            return await AnsiConsole.Status().StartAsync("Packaging 'msix'...", async ctx =>
            {
                try
                {
                    var args = "-w=appx";
                    if (output != null)
                    {
                        AnsiConsole.MarkupLine("[yellow]The output option is not supported for Electron apps. The provided output directory will be ignored.[/]");
                        Logger.LogWarning("If you want to customize the output folder, change the .build.directories.output options in your package.json file. (https://github.com/electron-userland/electron-builder/blob/973a0048b46b8367864241a903453f927c158304/packages/app-builder-lib/scheme.json#L3522-L3550)");
                    }

                    if (buildArchs?.Any() == true)
                    {
                        if (buildArchs.Contains(BuildArch.X64))
                        {
                            args += " --x64";
                        }

                        if (buildArchs.Contains(BuildArch.X86))
                        {
                            args += " --ia32";
                        }

                        if (buildArchs.Contains(BuildArch.Arm64))
                        {
                            args += " --arm64";
                        }
                    }

                    string command, arguments;
                    if (isYarn)
                    {
                        command = "yarn";
                        arguments = $"run electron-builder build {args}";
                    }
                    else
                    {
                        command = "npx";
                        arguments = $"electron-builder build {args}";
                    }

                    var result = await ExternalCommandExecutor.RunAsync(command, arguments, projectRootPath.FullName, ct);

                    if (result.ExitCode != 0)
                    {
                        throw new MSStoreException(result.StdErr);
                    }

                    ctx.SuccessStatus("Store package built successfully!");

                    var cleanedStdOut = System.Text.RegularExpressions.Regex.Replace(result.StdOut, @"\e([^\[\]]|\[.*?[a-zA-Z]|\].*?\a)", string.Empty);

                    var msixLine = cleanedStdOut.Split(
                        new string[]
                        {
                            "\n",
                            Environment.NewLine
                        }, StringSplitOptions.None).LastOrDefault(line => line.Contains("target=AppX"));
                    int index;
                    var search = "file=";
                    if (msixLine == null || (index = msixLine.IndexOf(search, StringComparison.OrdinalIgnoreCase)) == -1)
                    {
                        throw new MSStoreException("Failed to find the path to the packaged msix file.");
                    }

                    var msixPath = msixLine.Substring(index + search.Length).Trim();

                    FileInfo? msixFile = null;
                    if (msixPath != null)
                    {
                        if (Path.IsPathFullyQualified(msixPath))
                        {
                            msixFile = new FileInfo(msixPath);
                        }
                        else
                        {
                            msixFile = new FileInfo(Path.Combine(projectRootPath.FullName, msixPath));
                        }
                    }

                    return (0, msixFile?.Directory);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed to package 'msix'.");
                    throw new MSStoreException("Failed to generate msix package.", ex);
                }
            });
        }

        public override async Task<string?> GetAppIdAsync(FileInfo? fileInfo, CancellationToken ct)
        {
            await EnsureElectronManifestAsync(fileInfo, ct);

            return _electronManifest?.MSStoreCLIAppID;
        }

        private async Task EnsureElectronManifestAsync(FileInfo? fileInfo, CancellationToken ct)
        {
            if (fileInfo == null)
            {
                return;
            }

            _electronManifest ??= await _electronManifestManager.LoadAsync(fileInfo, ct);
        }

        public override async Task<int> PublishAsync(string pathOrUrl, DevCenterApplication? app, string? flightId, DirectoryInfo? inputDirectory, bool noCommit, IStorePackagedAPI storePackagedAPI, CancellationToken ct)
        {
            if (_electronManifest == null)
            {
                var (_, manifestFile) = GetInfo(pathOrUrl);
                await EnsureElectronManifestAsync(manifestFile, ct);
            }

            return await base.PublishAsync(pathOrUrl, app, flightId, inputDirectory, noCommit, storePackagedAPI, ct);
        }
    }
}