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
    public async Task ManualPublishBatchFiles_RouteToExpectedVariants()
    {
        var repositoryRoot = FindRepositoryRoot();
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Publish-WinX64.bat"] = "-Variant All",
            ["Publish-WinX64-SelfContained.bat"] = "-Variant SelfContained",
            ["Publish-WinX64-FrameworkDependent.bat"] = "-Variant FrameworkDependent"
        };

        foreach (var (fileName, variantArgument) in expected)
        {
            var path = Path.Combine(repositoryRoot, fileName);
            Assert.True(File.Exists(path), $"Manual publisher was not found: {path}");
            var content = await File.ReadAllTextAsync(path);
            Assert.Contains("scripts\\Publish-WinX64.ps1", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(variantArgument, content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("-OpenOutput", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("artifacts\\publish", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("GITKEYROUTER_NO_PAUSE", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("pause", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("exit /b %RC%", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ManualPublishScript_ReportsAndCanOpenTheGeneratedOutputDirectory()
    {
        var repositoryRoot = FindRepositoryRoot();
        var scriptPath = Path.Combine(repositoryRoot, "scripts", "Publish-WinX64.ps1");
        var content = await File.ReadAllTextAsync(scriptPath);

        Assert.Contains("[switch]$OpenOutput", content, StringComparison.Ordinal);
        Assert.Contains("Repository root:", content, StringComparison.Ordinal);
        Assert.Contains("Self-contained binary:", content, StringComparison.Ordinal);
        Assert.Contains("Framework-dependent binary:", content, StringComparison.Ordinal);
        Assert.Contains("Versioned archives and checksums:", content, StringComparison.Ordinal);
        Assert.Contains("Opening output folder:", content, StringComparison.Ordinal);
        Assert.Contains("explorer.exe", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ManualPublishScript_HasValidPowerShellSyntax()
    {
        var repositoryRoot = FindRepositoryRoot();
        var scriptPath = Path.Combine(repositoryRoot, "scripts", "Publish-WinX64.ps1");
        var quotedScriptPath = scriptPath.Replace("'", "''", StringComparison.Ordinal);
        var parseCommand = $$"""
            $tokens = $null
            $errors = $null
            [System.Management.Automation.Language.Parser]::ParseFile('{{quotedScriptPath}}', [ref]$tokens, [ref]$errors) | Out-Null
            if ($errors.Count -gt 0) {
                $errors | ForEach-Object { [Console]::Error.WriteLine($_.Message) }
                exit 1
            }
            """;

        var result = await RunProcessAsync(
            "powershell.exe",
            ["-NoProfile", "-NonInteractive", "-Command", parseCommand],
            repositoryRoot,
            TimeSpan.FromSeconds(30),
            captureOutput: true);

        Assert.True(
            result.ExitCode == 0,
            $"Publish-WinX64.ps1 has invalid syntax.{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}");
    }

    [Fact]
    public async Task ReleasePublish_ProducesBothExecutablesAndVersionedAssets()
    {
        var repositoryRoot = FindRepositoryRoot();
        var solutionPath = Path.Combine(repositoryRoot, "GitKeyRouter.sln");
        var projectPath = Path.Combine(repositoryRoot, "src", "GitKeyRouter.App", "GitKeyRouter.App.csproj");
        var publishRoot = Path.Combine(repositoryRoot, "artifacts", "publish");
        var releaseDirectory = Path.Combine(repositoryRoot, "artifacts", "release");
        var prepareReleaseScript = Path.Combine(repositoryRoot, "scripts", "Prepare-ReleaseAssets.ps1");
        var versionDocument = XDocument.Load(Path.Combine(repositoryRoot, "Directory.Build.props"));
        var version = versionDocument.Descendants("Version").Single().Value;

        var formatResult = await RunProcessAsync(
            "dotnet",
            ["format", solutionPath],
            repositoryRoot,
            TimeSpan.FromMinutes(3),
            captureOutput: true);
        Assert.True(
            formatResult.ExitCode == 0,
            $"dotnet format failed.{Environment.NewLine}{formatResult.StandardOutput}{Environment.NewLine}{formatResult.StandardError}");

        var variants = new[]
        {
            new { Profile = "win-x64-single-file", Directory = "win-x64", MaximumMiB = 200L },
            new { Profile = "win-x64-framework-dependent", Directory = "win-x64-framework-dependent", MaximumMiB = 25L }
        };

        foreach (var variant in variants)
        {
            var publishDirectory = Path.Combine(publishRoot, variant.Directory);
            if (Directory.Exists(publishDirectory))
            {
                Directory.Delete(publishDirectory, recursive: true);
            }

            var publishResult = await RunProcessAsync(
                "dotnet",
                [
                    "publish", projectPath, "-c", "Release", "-r", "win-x64",
                    $"-p:PublishProfile={variant.Profile}", "-o", publishDirectory
                ],
                repositoryRoot,
                TimeSpan.FromMinutes(5),
                captureOutput: true);
            Assert.True(
                publishResult.ExitCode == 0,
                $"dotnet publish failed for {variant.Profile}.{Environment.NewLine}{publishResult.StandardOutput}{Environment.NewLine}{publishResult.StandardError}");

            var entries = Directory.GetFileSystemEntries(publishDirectory);
            var executablePath = Path.Combine(publishDirectory, "GitKeyRouter.exe");
            Assert.Single(entries);
            Assert.True(File.Exists(executablePath), $"Published EXE was not found: {executablePath}");
            Assert.DoesNotContain(entries, path => path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase));
            Assert.InRange(new FileInfo(executablePath).Length, 1, variant.MaximumMiB * 1024 * 1024);

            await using (var stream = File.OpenRead(executablePath))
            {
                Assert.Equal(0x4D, stream.ReadByte());
                Assert.Equal(0x5A, stream.ReadByte());
            }

            var versionResult = await RunProcessAsync(
                executablePath,
                ["--version"],
                repositoryRoot,
                TimeSpan.FromSeconds(30),
                captureOutput: true);
            Assert.Equal(0, versionResult.ExitCode);
            Assert.Contains(version, versionResult.StandardOutput, StringComparison.OrdinalIgnoreCase);
        }

        var releaseResult = await RunProcessAsync(
            "powershell.exe",
            [
                "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                "-File", prepareReleaseScript, "-Version", version,
                "-PublishRoot", publishRoot, "-OutputDirectory", releaseDirectory
            ],
            repositoryRoot,
            TimeSpan.FromMinutes(2),
            captureOutput: true);
        Assert.True(
            releaseResult.ExitCode == 0,
            $"Release packaging failed.{Environment.NewLine}{releaseResult.StandardOutput}{Environment.NewLine}{releaseResult.StandardError}");
        Assert.True(File.Exists(Path.Combine(releaseDirectory, $"GitKeyRouter-v{version}-win-x64-portable.zip")));
        Assert.True(File.Exists(Path.Combine(releaseDirectory, $"GitKeyRouter-v{version}-win-x64-framework-dependent.zip")));
        Assert.True(File.Exists(Path.Combine(releaseDirectory, "SHA256SUMS.txt")));
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
