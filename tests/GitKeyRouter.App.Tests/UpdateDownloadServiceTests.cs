using System.Net;
using System.Security.Cryptography;
using System.Text;
using GitKeyRouter.App.Updates;
using GitKeyRouter.Tests.TestSupport;

namespace GitKeyRouter.App.Tests;

public sealed class UpdateDownloadServiceTests
{
    private static readonly byte[] PackageBytes = Encoding.UTF8.GetBytes("isolated mock installer; never executed");
    private static readonly string PackageHash = Convert.ToHexString(SHA256.HashData(PackageBytes));

    [Theory]
    [InlineData(UpdatePackageKind.InstallerSelfContained)]
    [InlineData(UpdatePackageKind.InstallerFrameworkDependent)]
    public async Task Download_ClosesFileBeforePublishingAndReusesVerifiedCache(UpdatePackageKind kind)
    {
        using var directory = new TemporaryDirectory();
        var release = CreateRelease();
        var handler = new DownloadHandler(release, kind);
        using var client = new HttpClient(handler);
        var service = new UpdateDownloadService(directory.Path, client);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var package = await service.DownloadVerifiedInstallerAsync(release, kind);
            Assert.Equal(PackageHash, package.Sha256);
            Assert.Equal(PackageBytes, await File.ReadAllBytesAsync(package.FilePath));
            // On Windows this also proves no download handle survives publication.
            using var exclusive = new FileStream(package.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }

        Assert.Equal(1, handler.PackageRequests);
        AssertNoPartialFiles(directory.Path);
    }

    [Fact]
    public async Task ConcurrentDownloads_AcrossInstancesWaitAndReuseTheVerifiedFile()
    {
        using var directory = new TemporaryDirectory();
        var release = CreateRelease();
        using var pause = new PausedReadStream(PackageBytes);
        var streamRequests = 0;
        var handler = new DownloadHandler(release)
        {
            PackageStream = () => Interlocked.Increment(ref streamRequests) == 1
                ? pause
                : new MemoryStream(PackageBytes, writable: false)
        };
        using var client = new HttpClient(handler);
        var firstService = new UpdateDownloadService(directory.Path, client);
        var first = firstService.DownloadVerifiedInstallerAsync(release, UpdatePackageKind.InstallerSelfContained);
        await pause.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var others = Enumerable.Range(0, 8).Select(_ =>
            new UpdateDownloadService(directory.Path, client)
                .DownloadVerifiedInstallerAsync(release, UpdatePackageKind.InstallerSelfContained)).ToArray();
        pause.Continue.TrySetResult();
        var results = await Task.WhenAll(others.Prepend(first)).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Single(results.Select(result => result.FilePath).Distinct());
        Assert.All(results, result => Assert.Equal(PackageHash, result.Sha256));
        Assert.Equal(PackageBytes, await File.ReadAllBytesAsync(results[0].FilePath));
        Assert.Equal(1, handler.PackageRequests);
        AssertNoPartialFiles(directory.Path);
    }

    [Fact]
    public async Task CancelledWaiter_DoesNotInterruptActiveDownloadOrBlockLaterRequests()
    {
        using var directory = new TemporaryDirectory();
        var release = CreateRelease();
        using var pause = new PausedReadStream(PackageBytes);
        var streamRequests = 0;
        var handler = new DownloadHandler(release)
        {
            PackageStream = () => Interlocked.Increment(ref streamRequests) == 1
                ? pause
                : new MemoryStream(PackageBytes, writable: false)
        };
        using var client = new HttpClient(handler);
        var service = new UpdateDownloadService(directory.Path, client);
        var first = service.DownloadVerifiedInstallerAsync(release, UpdatePackageKind.InstallerSelfContained);
        await pause.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource();
        var waiting = service.DownloadVerifiedInstallerAsync(release, UpdatePackageKind.InstallerSelfContained, cancellation.Token);
        cancellation.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        }
        finally
        {
            pause.Continue.TrySetResult();
        }

        var package = await first.WaitAsync(TimeSpan.FromSeconds(10));
        var repeated = await service.DownloadVerifiedInstallerAsync(release, UpdatePackageKind.InstallerSelfContained);
        Assert.Equal(package, repeated);
        Assert.Equal(1, handler.PackageRequests);
        AssertNoPartialFiles(directory.Path);
    }

    [Fact]
    public async Task CancelledDownload_RemovesPartialAndAllowsRetry()
    {
        using var directory = new TemporaryDirectory();
        var release = CreateRelease();
        using var pause = new PausedReadStream(PackageBytes);
        var streamRequests = 0;
        var handler = new DownloadHandler(release)
        {
            PackageStream = () => Interlocked.Increment(ref streamRequests) == 1
                ? pause
                : new MemoryStream(PackageBytes, writable: false)
        };
        using var client = new HttpClient(handler);
        var service = new UpdateDownloadService(directory.Path, client);
        using var cancellation = new CancellationTokenSource();
        var download = service.DownloadVerifiedInstallerAsync(release, UpdatePackageKind.InstallerSelfContained, cancellation.Token);
        await pause.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
        Assert.Empty(Directory.GetFiles(directory.Path, "*", SearchOption.AllDirectories));
        handler.PackageStream = () => new MemoryStream(PackageBytes, writable: false);
        var package = await service.DownloadVerifiedInstallerAsync(release, UpdatePackageKind.InstallerSelfContained);
        Assert.Equal(PackageBytes, await File.ReadAllBytesAsync(package.FilePath));
        AssertNoPartialFiles(directory.Path);
    }

    [Fact]
    public async Task HashMismatch_PreservesExistingCacheCleansPartialAndAllowsRetry()
    {
        using var directory = new TemporaryDirectory();
        var release = CreateRelease();
        var versionDirectory = Path.Combine(directory.Path, $"v{release.Version}");
        Directory.CreateDirectory(versionDirectory);
        var destination = Path.Combine(versionDirectory, release.PreferredFileName(UpdatePackageKind.InstallerSelfContained));
        await File.WriteAllTextAsync(destination, "old invalid cache");
        var handler = new DownloadHandler(release)
        {
            PackageStream = () => new MemoryStream(Encoding.UTF8.GetBytes("corrupt download"))
        };
        using var client = new HttpClient(handler);
        var service = new UpdateDownloadService(directory.Path, client);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.DownloadVerifiedInstallerAsync(release, UpdatePackageKind.InstallerSelfContained));
        Assert.Equal("old invalid cache", await File.ReadAllTextAsync(destination));
        AssertNoPartialFiles(directory.Path);

        handler.PackageStream = () => new MemoryStream(PackageBytes, writable: false);
        var package = await service.DownloadVerifiedInstallerAsync(release, UpdatePackageKind.InstallerSelfContained);
        Assert.Equal(PackageBytes, await File.ReadAllBytesAsync(package.FilePath));
        AssertNoPartialFiles(directory.Path);
    }

    [Fact]
    public async Task Download_DoesNotDeleteAnUnrelatedLockedPartialFile()
    {
        using var directory = new TemporaryDirectory();
        var release = CreateRelease();
        var versionDirectory = Path.Combine(directory.Path, $"v{release.Version}");
        Directory.CreateDirectory(versionDirectory);
        var legacyPartial = Path.Combine(versionDirectory, release.PreferredFileName(UpdatePackageKind.InstallerSelfContained) + ".partial");
        await File.WriteAllTextAsync(legacyPartial, "unrelated in-progress download");
        using var heldFile = new FileStream(legacyPartial, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var client = new HttpClient(new DownloadHandler(release));

        var package = await new UpdateDownloadService(directory.Path, client)
            .DownloadVerifiedInstallerAsync(release, UpdatePackageKind.InstallerSelfContained);

        Assert.Equal(PackageBytes, await File.ReadAllBytesAsync(package.FilePath));
        Assert.True(File.Exists(legacyPartial));
        Assert.Single(Directory.GetFiles(versionDirectory, "*.partial"));
    }

    [Fact]
    public async Task Checker_RepeatedAndConcurrentChecksReadOnlyReleaseMetadata()
    {
        using var handler = new MetadataHandler();
        using var client = new HttpClient(handler);
        var checker = new GitHubUpdateChecker(client);
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => checker.GetLatestAsync()));
        Assert.All(concurrent, release => Assert.Equal("v1.2.3", release.TagName));
        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.Equal("v1.2.3", (await checker.GetLatestAsync()).TagName);
        }
        Assert.Equal(19, handler.Requests);
    }

    private sealed class MetadataHandler : HttpMessageHandler
    {
        private int _requests;
        public int Requests => Volatile.Read(ref _requests);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(UpdateProjectLinks.Manifest, request.RequestUri);
            Interlocked.Increment(ref _requests);
            await Task.Yield();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {
                      "schemaVersion": 3,
                      "tagName": "v1.2.3",
                      "releasePage": "https://github.com/project-base-mirror/tool-git-key-router/releases/tag/v1.2.3",
                      "downloads": {},
                      "notes": "mock metadata"
                    }
                    """, Encoding.UTF8, "application/json")
            };
        }
    }

    private static void AssertNoPartialFiles(string directory)
        => Assert.Empty(Directory.GetFiles(directory, "*.partial", SearchOption.AllDirectories));

    private static UpdateReleaseInfo CreateRelease()
    {
        const string root = "https://github.com/project-base-mirror/tool-git-key-router/releases";
        return new UpdateReleaseInfo("v1.2.3", new Version(1, 2, 3, 0), new Uri($"{root}/tag/v1.2.3"),
            null, null,
            new Uri($"{root}/download/v1.2.3/GitKeyRouter-v1.2.3-win-x64-framework-dependent-setup.msi"),
            new Uri($"{root}/download/v1.2.3/GitKeyRouter-v1.2.3-win-x64-setup.msi"),
            new Uri($"{root}/download/v1.2.3/SHA256SUMS.txt"), "mock", UpdateReleaseSource.PagesManifest);
    }

    private sealed class DownloadHandler(
        UpdateReleaseInfo release,
        UpdatePackageKind kind = UpdatePackageKind.InstallerSelfContained) : HttpMessageHandler
    {
        private int _packageRequests;
        public int PackageRequests => Volatile.Read(ref _packageRequests);
        public Func<Stream> PackageStream { get; set; } = () => new MemoryStream(PackageBytes, writable: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpContent content;
            if (request.RequestUri == release.ChecksumsUrl)
            {
                content = new StringContent($"{PackageHash}  {release.PreferredFileName(kind)}\n");
            }
            else
            {
                Assert.Equal(release.PreferredDownload(kind), request.RequestUri);
                Interlocked.Increment(ref _packageRequests);
                content = new StreamContent(PackageStream());
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class PausedReadStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Continue.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }
}
