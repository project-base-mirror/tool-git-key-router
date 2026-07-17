using System.Text;
using System.Text.RegularExpressions;
using GitKeyRouter.Core.Abstractions;
using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Validation;

namespace GitKeyRouter.Core.Services;

public sealed partial class SshConfigService
{
    public const string BeginPrefix = "# BEGIN GitKeyRouter managed block: ";
    public const string EndPrefix = "# END GitKeyRouter managed block: ";

    private readonly IFileSystem _fileSystem;
    private readonly IAppPaths _paths;
    private readonly IBackupService _backupService;

    public SshConfigService(IFileSystem fileSystem, IAppPaths paths, IBackupService backupService)
    {
        _fileSystem = fileSystem;
        _paths = paths;
        _backupService = backupService;
    }

    public async Task<string> ReadRawAsync(CancellationToken cancellationToken = default)
    {
        if (!_fileSystem.FileExists(_paths.SshConfigPath))
        {
            return string.Empty;
        }

        return await _fileSystem.ReadAllTextAsync(_paths.SshConfigPath, cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<SshManagedBlock> ParseManagedBlocks(string text)
    {
        var blocks = new List<SshManagedBlock>();
        foreach (Match match in ManagedBlockPattern().Matches(text))
        {
            blocks.Add(new SshManagedBlock
            {
                HostAlias = match.Groups["alias"].Value.Trim(),
                RawText = match.Value,
                StartIndex = match.Index,
                Length = match.Length
            });
        }

        return blocks;
    }

    public ChangePreview PreviewUpsert(string original, GitHubIdentity identity)
    {
        var validation = HostAliasValidator.Validate(identity.HostAlias);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, validation.Errors));
        }

        var blocks = ParseManagedBlocks(original)
            .Where(block => string.Equals(block.HostAlias, identity.HostAlias, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (blocks.Count > 1)
        {
            throw new InvalidOperationException($"Multiple managed blocks exist for HostAlias '{identity.HostAlias}'. Resolve duplicates before saving.");
        }

        var newline = DetectNewline(original);
        var blockText = BuildManagedBlock(identity, newline);
        string updated;
        if (blocks.Count == 1)
        {
            var block = blocks[0];
            updated = original.Remove(block.StartIndex, block.Length).Insert(block.StartIndex, blockText);
        }
        else
        {
            var separator = string.IsNullOrEmpty(original)
                ? string.Empty
                : original.EndsWith("\r\n", StringComparison.Ordinal) || original.EndsWith('\n') || original.EndsWith('\r')
                    ? newline
                    : newline + newline;
            updated = original + separator + blockText;
        }

        return new ChangePreview
        {
            Description = $"Update SSH managed block: {identity.HostAlias}",
            OriginalText = original,
            UpdatedText = updated,
            DiffText = TextDiffService.CreateSimpleDiff(original, updated, "ssh_config.before", "ssh_config.after")
        };
    }

    public ChangePreview PreviewDelete(string original, string hostAlias)
    {
        var blocks = ParseManagedBlocks(original)
            .Where(block => string.Equals(block.HostAlias, hostAlias, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(block => block.StartIndex)
            .ToList();

        var updated = original;
        foreach (var block in blocks)
        {
            updated = updated.Remove(block.StartIndex, block.Length);
        }

        updated = CollapseExcessBlankLines(updated, DetectNewline(original));
        return new ChangePreview
        {
            Description = $"Delete SSH managed block: {hostAlias}",
            OriginalText = original,
            UpdatedText = updated,
            DiffText = TextDiffService.CreateSimpleDiff(original, updated, "ssh_config.before", "ssh_config.after")
        };
    }

    public async Task<OperationResult> ApplyAsync(ChangePreview preview, string reason, CancellationToken cancellationToken = default)
    {
        if (!preview.HasChanges)
        {
            return OperationResult.Ok("SSH config already matches the requested state.");
        }

        await _backupService.CreateSnapshotAsync(reason, cancellationToken).ConfigureAwait(false);
        _fileSystem.CreateDirectory(_paths.SshDirectory);

        if (_fileSystem.FileExists(_paths.SshConfigPath))
        {
            _fileSystem.CopyFile(_paths.SshConfigPath, _paths.LegacySshConfigBackupPath, true);
        }

        await _fileSystem.WriteAllTextAtomicAsync(_paths.SshConfigPath, preview.UpdatedText, cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok("SSH config was updated and backed up.");
    }

    public static string BuildManagedBlock(GitHubIdentity identity, string newline)
    {
        var keyPath = ConvertWindowsPathToOpenSsh(identity.PrivateKeyPath);
        if (keyPath.Contains(' '))
        {
            keyPath = $"\"{keyPath}\"";
        }

        var builder = new StringBuilder();
        builder.Append(BeginPrefix).Append(identity.HostAlias).Append(newline);
        builder.Append("Host ").Append(identity.HostAlias).Append(newline);
        builder.Append("    HostName github.com").Append(newline);
        builder.Append("    User git").Append(newline);
        builder.Append("    IdentityFile ").Append(keyPath).Append(newline);
        builder.Append("    IdentitiesOnly yes").Append(newline);
        builder.Append(EndPrefix).Append(identity.HostAlias).Append(newline);
        return builder.ToString();
    }

    public static string ConvertWindowsPathToOpenSsh(string path)
        => Path.GetFullPath(path).Replace('\\', '/');

    public static string DetectNewline(string text)
    {
        if (text.Contains("\r\n", StringComparison.Ordinal))
        {
            return "\r\n";
        }

        return text.Contains('\n') ? "\n" : Environment.NewLine;
    }

    private static string CollapseExcessBlankLines(string text, string newline)
    {
        var triple = newline + newline + newline;
        while (text.Contains(triple, StringComparison.Ordinal))
        {
            text = text.Replace(triple, newline + newline, StringComparison.Ordinal);
        }

        return text;
    }

    [GeneratedRegex("(?ms)^# BEGIN GitKeyRouter managed block: (?<alias>[^\\r\\n]+)\\r?\\n.*?^# END GitKeyRouter managed block: \\k<alias>(?:\\r?\\n|$)", RegexOptions.CultureInvariant)]
    private static partial Regex ManagedBlockPattern();
}
