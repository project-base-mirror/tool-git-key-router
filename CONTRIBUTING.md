# Contributing to GitKeyRouter / 参与贡献

Thank you for improving GitKeyRouter. Changes should preserve the project's safety model: visible plans, exact file or Git edits, backups before risky operations, and no exposure of private-key contents.

感谢参与改进 GitKeyRouter。所有变更都应保持项目的安全模型：计划可见、精确修改文件或 Git 配置、高风险操作前备份，并且绝不暴露私钥正文。

## Development requirements / 开发环境

- Windows 10 or Windows 11 x64
- .NET SDK version from `global.json`
- Git for Windows
- OpenSSH Client or Git for Windows OpenSSH

## Setup and validation / 初始化与验证

```powershell
dotnet restore .\GitKeyRouter.sln --locked-mode
dotnet build .\GitKeyRouter.sln -c Release --no-restore
dotnet test .\GitKeyRouter.sln -c Release --no-build --no-restore
dotnet format .\GitKeyRouter.sln --verify-no-changes --no-restore
```

Run the full publish pipeline for release, build-script, runtime, or UI changes:

```powershell
.\scripts\Publish-WinX64.ps1 -SkipFormat
```

发布、构建脚本、运行时或 UI 变更还应运行完整发布流程。

## Dependency updates / 依赖更新

NuGet restore is locked. When intentionally changing dependencies, regenerate and review all affected `packages.lock.json` files:

```powershell
dotnet restore .\GitKeyRouter.sln --use-lock-file --force-evaluate
dotnet restore .\src\GitKeyRouter.App\GitKeyRouter.App.csproj `
  -r win-x64 `
  --use-lock-file `
  --force-evaluate `
  -p:NuGetLockFilePath=packages.publish-win-x64.lock.json `
  -p:PublishSingleFile=true
```

Standard builds use `packages.lock.json`; WinForms tests use `packages.win-x64.lock.json`; single-file publishing uses `packages.publish-win-x64.lock.json`. Keeping all three graphs separate prevents one restore mode from overwriting another. Commit project files and every affected lock-file set together. Do not edit lock files manually.

依赖声明与锁文件必须在同一个提交中更新，不要手工编辑锁文件。

## Change guidelines / 变更要求

- Keep one coherent requirement per commit. Include its tests, documentation, formatting, and focused repairs in that commit.
- Add tests for failure paths, not only the successful path.
- Preserve compatibility with existing `config.json` and backup schemas unless a migration is explicitly designed and tested.
- Do not invoke Git or SSH through a shell. Use `ProcessStartInfo.ArgumentList`.
- Never log, display, copy, or commit private-key contents or real credentials.
- UI changes must be checked at the minimum supported window size and common DPI scales.
- Keep Chinese and English README behavior descriptions aligned.

## Pull-request checklist / 合并检查

- [ ] The change has a clear requirement and rollback boundary.
- [ ] Release build and all tests pass.
- [ ] Formatting verification passes.
- [ ] Locked restore succeeds.
- [ ] New or changed risky behavior has focused failure-path tests.
- [ ] Documentation and both README languages are updated when behavior changes.
- [ ] No secrets, generated release artifacts, or unrelated local work are included.

Potential security issues must follow [SECURITY.md](SECURITY.md), not the public issue tracker.
