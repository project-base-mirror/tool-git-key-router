using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace GitKeyRouter.App.Tests;

public sealed class PublishSmokeTests
{
    [Fact]
    public void PublishProfiles_UseDistinctOutputDirectoriesAndRuntimeModes()
    {
        var repositoryRoot = FindRepositoryRoot();
        var profileDirectory = Path.Combine(
            repositoryRoot,
            "src",
            "GitKeyRouter.App",
            "Properties",
            "PublishProfiles");
        var selfContained = LoadProfile(
            Path.Combine(profileDirectory, "win-x64-single-file.pubxml"));
        var frameworkDependent = LoadProfile(
            Path.Combine(profileDirectory, "win-x64-framework-dependent.pubxml"));

        Assert.Equal("self-contained", selfContained["GitKeyRouterPublishFlavor"]);
        Assert.Equal("true", selfContained["SelfContained"]);
        Assert.Equal("framework-dependent", frameworkDependent["GitKeyRouterPublishFlavor"]);
        Assert.Equal("false", frameworkDependent["SelfContained"]);
        Assert.Equal("true", selfContained["PublishSingleFile"]);
        Assert.Equal("true", frameworkDependent["PublishSingleFile"]);
        Assert.NotEqual(selfContained["PublishDir"], frameworkDependent["PublishDir"]);
    }

    [Fact]
    public async Task FrameworkDependentPublish_ProducesLaunchableCompactExecutable()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "src",
            "GitKeyRouter.App",
            "GitKeyRouter.App.csproj");
        var validationScriptPath = Path.Combine(
            repositoryRoot,
            "scripts",
            "Test-WinX64Publish.ps1");
        var publishRoot = Path.Combine(repositoryRoot, "artifacts", "publish");
        var publishDirectory = Path.Combine(publishRoot, "win-x64-framework-dependent");
        const string checksumFileName = "GitKeyRouter-win-x64-framework-dependent.sha256";
        var checksumPath = Path.Combine(publishRoot, checksumFileName);

        if (Directory.Exists(publishDirectory))
        {
            Directory.Delete(publishDirectory, recursive: true);
        }

        if (File.Exists(checksumPath))
        {
            File.Delete(checksumPath);
        }

        Directory.CreateDirectory(publishDirectory);

        var publishResult = await RunProcessAsync(
            "dotnet",
            [
                "publish",
                projectPath,
                "-c",
                "Release",
                "-r",
                "win-x64",
                "-p:PublishProfile=win-x64-framework-dependent",
                "-o",
                publishDirectory
            ],
            repositoryRoot,
            TimeSpan.FromMinutes(3),
            captureOutput: true);

        Assert.True(
            publishResult.ExitCode == 0,
            $"dotnet publish failed.{Environment.NewLine}{publishResult.StandardOutput}{Environment.NewLine}{publishResult.StandardError}");

        var entries = Directory.GetFileSystemEntries(publishDirectory);
        Assert.True(
            entries.Length == 1,
            $"Expected only GitKeyRouter.exe, found: {string.Join(", ", entries.Select(Path.GetFileName))}");

        var executablePath = entries[0];
        Assert.Equal("GitKeyRouter.exe", Path.GetFileName(executablePath));
        Assert.True(File.Exists(executablePath));

        var executableInfo = new FileInfo(executablePath);
        Assert.InRange(executableInfo.Length, 1, 25L * 1024 * 1024);

        await using (var stream = File.OpenRead(executablePath))
        {
            Assert.Equal(0x4D, stream.ReadByte());
            Assert.Equal(0x5A, stream.ReadByte());
        }

        var validationResult = await RunProcessAsync(
            "powershell.exe",
            [
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                validationScriptPath,
                "-PublishDir",
                publishDirectory,
                "-ChecksumFileName",
                checksumFileName
            ],
            repositoryRoot,
            TimeSpan.FromSeconds(30),
            captureOutput: true);

        Assert.True(
            validationResult.ExitCode == 0,
            $"Publish validation script failed.{Environment.NewLine}{validationResult.StandardOutput}{Environment.NewLine}{validationResult.StandardError}");
        Assert.True(
            File.Exists(checksumPath),
            "Publish validation must create a SHA-256 checksum file.");

        await using var hashStream = File.OpenRead(executablePath);
        var expectedHash = Convert.ToHexString(await SHA256.HashDataAsync(hashStream));
        var checksumContent = await File.ReadAllTextAsync(checksumPath);
        Assert.Equal($"{expectedHash}  GitKeyRouter.exe\r\n", checksumContent);
    }

    [Fact]
    public async Task PrepareReleaseAssets_CreatesVersionedArchivesAndChecksums()
    {
        var repositoryRoot = FindRepositoryRoot();
        var scriptPath = Path.Combine(repositoryRoot, "scripts", "Prepare-ReleaseAssets.ps1");
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "GitKeyRouter.ReleaseTests",
            Guid.NewGuid().ToString("N"));
        var publishRoot = Path.Combine(temporaryRoot, "publish");
        var outputDirectory = Path.Combine(temporaryRoot, "release");

        try
        {
            var portableDirectory = Path.Combine(publishRoot, "win-x64");
            var frameworkDirectory = Path.Combine(publishRoot, "win-x64-framework-dependent");
            Directory.CreateDirectory(portableDirectory);
            Directory.CreateDirectory(frameworkDirectory);
            await File.WriteAllBytesAsync(
                Path.Combine(portableDirectory, "GitKeyRouter.exe"),
                [0x4D, 0x5A, 0x01, 0x02]);
            await File.WriteAllBytesAsync(
                Path.Combine(frameworkDirectory, "GitKeyRouter.exe"),
                [0x4D, 0x5A, 0x03, 0x04]);

            var result = await RunProcessAsync(
                "powershell.exe",
                [
                    "-NoProfile",
                    "-NonInteractive",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
                    scriptPath,
                    "-Version",
                    "0.3.0",
                    "-PublishRoot",
                    publishRoot,
                    "-OutputDirectory",
                    outputDirectory
                ],
                repositoryRoot,
                TimeSpan.FromSeconds(30),
                captureOutput: true);

            Assert.True(
                result.ExitCode == 0,
                $"Release asset preparation failed.{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}");

            var portableZip = Path.Combine(
                outputDirectory,
                "GitKeyRouter-v0.3.0-win-x64-portable.zip");
            var frameworkZip = Path.Combine(
                outputDirectory,
                "GitKeyRouter-v0.3.0-win-x64-framework-dependent.zip");
            var checksumPath = Path.Combine(outputDirectory, "SHA256SUMS.txt");

            Assert.True(File.Exists(portableZip));
            Assert.True(File.Exists(frameworkZip));
            Assert.True(File.Exists(checksumPath));

            foreach (var archivePath in new[] { portableZip, frameworkZip })
            {
                using var archive = ZipFile.OpenRead(archivePath);
                var entryNames = archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal);
                Assert.Contains("GitKeyRouter.exe", entryNames);
                Assert.Contains("LICENSE.txt", entryNames);
                Assert.Contains("README.txt", entryNames);
            }

            var checksumLines = await File.ReadAllLinesAsync(checksumPath);
            Assert.Equal(2, checksumLines.Length);
            foreach (var archivePath in new[] { portableZip, frameworkZip })
            {
                await using var stream = File.OpenRead(archivePath);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream));
                Assert.Contains($"{hash}  {Path.GetFileName(archivePath)}", checksumLines);
            }
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static IReadOnlyDictionary<string, string> LoadProfile(string path)
    {
        var document = XDocument.Load(path);
        var propertyGroup = document.Root?.Element("PropertyGroup")
            ?? throw new InvalidDataException($"Publish profile has no PropertyGroup: {path}");

        return propertyGroup
            .Elements()
            .ToDictionary(
                element => element.Name.LocalName,
                element => element.Value,
                StringComparer.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GitKeyRouter.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the GitKeyRouter repository root.");
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        bool captureOutput)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = captureOutput,
            RedirectStandardError = captureOutput
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process: {fileName}");
        }

        var standardOutputTask = captureOutput
            ? process.StandardOutput.ReadToEndAsync()
            : Task.FromResult(string.Empty);
        var standardErrorTask = captureOutput
            ? process.StandardError.ReadToEndAsync()
            : Task.FromResult(string.Empty);

        using var timeoutSource = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw new TimeoutException($"Process exceeded timeout {timeout}: {fileName}");
        }

        return new ProcessResult(
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask);
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
