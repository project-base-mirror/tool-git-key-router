# Security Policy / 安全策略

## Supported versions / 支持版本

Security fixes are applied to the latest released version and the current `main` branch. Older releases may require upgrading before a fix is available.

安全修复面向最新正式版本和当前 `main` 分支。旧版本可能需要先升级才能获得修复。

## Reporting a vulnerability / 报告漏洞

Use the repository's private **Report a vulnerability** security-advisory flow. Do not open a public issue for an unpatched vulnerability.

请使用仓库的 **Report a vulnerability** 私有安全公告入口。尚未修复的漏洞不要通过公开 Issue 报告。

Include, when available:

- The affected GitKeyRouter version and Windows version
- Reproduction steps and expected versus actual behavior
- Whether private keys, Git configuration, SSH Config, backups, or command execution are involved
- A minimal proof of concept with secrets removed
- Any proposed mitigation

请尽量提供受影响版本、Windows 版本、复现步骤、预期与实际行为、涉及的数据类型，以及已去除秘密信息的最小复现。

Never include real private keys, access tokens, passwords, production repository URLs, or unredacted personal paths.

不要提交真实私钥、访问令牌、密码、生产仓库地址或未经脱敏的个人路径。

## Security boundaries / 安全边界

High-priority reports include:

- Private-key disclosure or insufficient redaction
- Command or argument injection into `git.exe`, `ssh.exe`, or `ssh-keygen.exe`
- Unintended replacement of unmanaged SSH Config or `.gitconfig` content
- Backup integrity bypass, unsafe restore, or path traversal
- Concurrent writes that can corrupt configuration
- Release artifact tampering or dependency-supply-chain issues

The maintainers will validate the report, coordinate a fix, and publish details after users have a reasonable opportunity to update. No response-time guarantee is implied.

维护者会验证报告、协调修复，并在用户获得合理升级时间后公开细节；本文不承诺固定响应时限。
