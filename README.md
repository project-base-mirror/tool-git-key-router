# GitKeyRouter

GitKeyRouter 是一个面向 Windows 10 / Windows 11 的本地桌面工具，用于统一管理：

- GitHub.com、GitLab.com、自建 GitLab、Gitea 和通用 Git 服务实例
- 每个服务下的多个 SSH 身份、账号和密钥路径
- `%USERPROFILE%\.ssh\config` 中按服务生成的 HostAlias
- 按“Git 服务 + Owner / Namespace”路由的 `url.*.insteadOf` 全局 Git 配置
- 按目录或远程 URL 自动选择 `user.name`、`user.email` 和签名密钥的 Git Profiles
- 配置修改前的 diff、命令输出、备份和选择性恢复
- GUI 与 DevRunner 可调用的简单 CLI

项目使用 C#、.NET 8 和 WinForms，不使用数据库、WebView、Node.js、Electron、GitHub API、OAuth 或 PAT。

> 项目目标框架为 .NET 8，并在 Windows 环境通过 Release 构建和自动化测试验证。发布前仍建议在目标机器执行 `dotnet build --configuration Release` 和 `dotnet test --configuration Release`。

## 安全和操作边界

GitKeyRouter 以“管理方便、状态透明、操作可恢复”为目标：

- 不实现 SSH 密钥算法，生成密钥时调用系统 `ssh-keygen.exe`
- Git 操作调用实际 `git.exe`
- SSH 测试调用实际 `ssh.exe`
- 不通过 `cmd.exe` 或 PowerShell 拼接 Git/SSH 命令
- 外部进程使用 `ProcessStartInfo.ArgumentList` 分别传递参数
- 不自动下载或安装 Git、OpenSSH 或其他软件
- 不保存、复制或显示私钥正文
- 删除身份记录时默认不删除密钥文件
- 自动管理 SSH Config 时只修改 GitKeyRouter managed block
- Git rewrite 使用 `git config --global` 精确读写，不覆盖整个 `.gitconfig`
- 危险操作先显示文本 diff 或结构化变更计划
- 修改前创建快照，恢复操作本身也会先创建新快照

## 系统要求

- Windows 10 或 Windows 11 x64
- .NET 8 SDK：仅源码构建需要
- Git for Windows：提供 `git.exe`
- Windows OpenSSH Client 或 Git for Windows OpenSSH：提供 `ssh.exe`、`ssh-keygen.exe`

程序启动和“一键诊断”会显示三个工具的：

- 是否存在
- 实际采用路径
- 其他候选路径
- 版本或文件版本
- 探测命令的 stdout、stderr 和 exit code

缺失时只显示明确提示，不会自动安装。

## 快速开始

### 1. 启动程序

无命令行参数时进入 WinForms：

```powershell
GitKeyRouter.exe
```

### 2. 配置 Git 服务和身份

GitHub.com 是不可删除的内置服务。使用 GitLab、Gitea 或其他自建服务时，先在“Git 服务”页面创建实例并填写域名、SSH 用户、端口和 Web Base URL。

然后打开“Git 身份”，创建例如：

```text
Git 服务: GitHub.com
显示名称: Camus GitHub
账号: camus0109
HostAlias: github-camus
私钥路径: C:\Users\fgc01\.ssh\id_ed25519_github_camus
公钥路径: C:\Users\fgc01\.ssh\id_ed25519_github_camus.pub
注释: camus0109
```

程序配置保存于：

```text
%APPDATA%\GitKeyRouter\config.json
```

密钥仍保存在用户选择的位置，不会复制到程序配置目录。

### 3. 生成或导入密钥

“生成密钥”调用：

```text
ssh-keygen.exe -t ed25519 -C <comment> -f <private-key-path> -N ""
```

第一版默认不设置 passphrase，界面会在执行前明确提示。

目标文件存在时可以：

- 取消
- 返回编辑身份并选择其他文件名
- 明确覆盖；覆盖前旧密钥会备份为 `.gitkeyrouter.<timestamp>.bak`

生成后会显示完整公钥，并提供复制和导出功能。

GitKeyRouter 会识别同一身份目录中的多种公钥格式，并在“Git 身份”列表中为每个格式显示独立行：

- OpenSSH 公钥
- RFC4716 / SSH2 公钥
- PEM / PKCS8 公钥
- 未知或无效的候选公钥文件

格式转换不会覆盖原文件，而是使用明确的文件名并存：

```text
id_ed25519_account.pub             # 用户配置的原始公钥路径
id_ed25519_account.openssh.pub     # OpenSSH
id_ed25519_account.rfc4716.pub     # RFC4716 / SSH2
id_ed25519_account.pem.pub         # PEM / PKCS8
```

如果目标格式文件已存在，默认拒绝覆盖；用户明确允许覆盖时，会先创建 `.gitkeyrouter.<timestamp>.bak` 备份。备份和临时转换文件不会显示在公钥变体列表中。

程序不显示私钥正文。选择已配置的 OpenSSH/PEM 私钥时，只会调用 `ssh-keygen -y` 派生新的 `.openssh.pub` 文件。PuTTY PPK 需要先用 PuTTYgen 转换。

### 4. 添加公钥到 Git 服务

在对应 Git 服务账户中打开 SSH Keys 页面，创建新 Key，并使用“复制公钥”复制标记为 OpenSSH 格式的公钥。RFC4716、PEM、私钥或结构损坏的文本不会被该按钮复制。

GitKeyRouter 0.2 不调用 GitHub、GitLab 或 Gitea API，也不会替用户上传公钥。

### 5. 同步 SSH Config

同步后只增加受控区块：

```sshconfig
# BEGIN GitKeyRouter managed block: github-camus
Host github-camus
    HostName github.com
    User git
    IdentityFile C:/Users/fgc01/.ssh/id_ed25519_github_camus
    IdentitiesOnly yes
# END GitKeyRouter managed block: github-camus
```

程序保留：

- 其他 Host 配置
- 用户注释
- 非受控文本
- 原有 CRLF/LF 换行风格

普通同步不会重写完整 SSH Config。只有用户主动进入“编辑原始文本”并确认完整 diff 时，才会执行完整文本替换。

### 6. 创建仓库路由

示例：

```text
camus0109/*
→ github-camus

project-base-mirror/*
→ github-project-base
```

对于 `camus0109`，期望规则为：

```text
url.git@github-camus:camus0109/.insteadOf = https://github.com/camus0109/
url.git@github-camus:camus0109/.insteadOf = git@github.com:camus0109/
```

程序实际调用等价于：

```powershell
git config --global --add "url.git@github-camus:camus0109/.insteadOf" "https://github.com/camus0109/"
git config --global --add "url.git@github-camus:camus0109/.insteadOf" "git@github.com:camus0109/"
```

显示给用户的命令只用于复制和审阅；程序执行时不会把整条命令交给 shell。

## 仓库路由原理

输入：

```text
https://github.com/camus0109/panel-terraria.git
```

Git 根据 `insteadOf` 的最长匹配前缀，将它改写为：

```text
git@github-camus:camus0109/panel-terraria.git
```

OpenSSH 再根据 `Host github-camus` 选择对应私钥。

因此：

- Git 服务和 Owner / Namespace 决定使用哪个 HostAlias
- HostAlias 决定使用哪个 IdentityFile
- 仓库本身不需要逐个修改 SSH key 配置

## Git rewrite 状态

“Git 重写配置”页区分：

- `Correct`：精确规则存在一次
- `Missing`：没有任何对应前缀规则
- `Duplicate`：相同 Base URL 和 insteadOf 重复
- `Conflict`：同一个 insteadOf 前缀指向其他 Base URL
- `Extra`：当前 Git 中存在，但不属于启用仓库路由

支持：

- 应用缺失配置
- 修复当前全部路由
- 删除指定规则
- 清理重复规则
- 复制对应 Git 命令

“修复”只处理当前启用路由涉及的精确前缀，不会自动删除其他无关 URL rewrite。

删除使用：

```text
git config --global --fixed-value --unset-all <key> <exact-value>
```

`--fixed-value` 用于避免把 URL 当作正则表达式删除。

## URL 测试

### 本地预览

只读取当前 Git rewrite 并按最长前缀规则计算，不访问网络。

### 实际连接测试

用户明确确认后执行：

```text
git ls-remote <original-url> HEAD
```

结果窗口显示：

- 实际 executable
- 独立参数
- stdout
- stderr
- exit code
- 超时和耗时

## SSH 测试

普通模式：

```text
ssh -T git@github-camus
```

详细模式：

```text
ssh -vT git@github-camus
```

GitHub、GitLab 和 Gitea 的 SSH 测试可能在认证成功时仍返回非零 exit code，因此程序会由对应平台适配器检查 stdout 和 stderr 中的服务特定成功提示。

```text
successfully authenticated
```

原始输出始终可查看。

## 备份和恢复

备份目录：

```text
%APPDATA%\GitKeyRouter\backups\<timestamp>\
```

每个快照可能包含：

```text
manifest.json
app_config.json
ssh_config.txt
git_url_rewrites.json
```

其中：

- `app_config.json`：Git 服务、身份和仓库路由配置
- `ssh_config.txt`：修改前 SSH Config
- `git_url_rewrites.json`：修改前所有 `url.*.insteadOf` 规则
- `manifest.json`：时间、原因、配置 Schema、原文件是否存在、Git 快照是否成功等元数据

如果 Git 不可用，身份配置仍允许保存；快照会明确记录 Git rewrite 捕获失败，而不是伪造空快照。此类快照不能用于恢复 Git rewrite。

支持分别恢复：

- SSH Config
- Git URL rewrite
- 程序配置

恢复 Git rewrite 仍通过 `git config` 逐条精确删除和增加，不替换整个 `.gitconfig`。

## Git Profiles 与提交身份

0.3.0 新增“Git Profiles”页面。每个 Profile 可以保存 `user.name`、`user.email`、签名密钥、默认 Git 服务和默认 SSH 身份，并通过目录或远程 URL 规则自动生效。

目录规则生成 Git 官方的 `includeIf "gitdir/i:<目录>/"` 条件；远程 URL 规则生成 `includeIf "hasconfig:remote.*.url:<模式>"` 条件。程序不会逐仓库修改 `.git/config`，而是在 `%APPDATA%\GitKeyRouter\git-profiles` 中生成一个 managed 条件配置入口和独立 Profile 文件，并只在全局 Git 配置中注册一次 `include.path`。

“预览并应用”会先显示入口文件和所有 Profile 文件的 diff。删除 Profile 或规则后，需要再次应用才能同步删除已经生成的条件配置。

## CLI

GUI 与 CLI 使用同一个服务图和同一套业务逻辑。

```powershell
GitKeyRouter.exe diagnose
GitKeyRouter.exe list-services
GitKeyRouter.exe list-identities
GitKeyRouter.exe list-profiles
GitKeyRouter.exe list-routes
GitKeyRouter.exe apply
GitKeyRouter.exe apply --yes
GitKeyRouter.exe apply-profiles
GitKeyRouter.exe apply-profiles --yes
GitKeyRouter.exe parse-url ssh://git@gitlab.example:2222/company/platform/repo.git
GitKeyRouter.exe resolve-profile C:\code\work\repo --url https://gitlab.example/company/repo.git
GitKeyRouter.exe test-service gitlab-office
GitKeyRouter.exe test-route camus0109
GitKeyRouter.exe test-route company/platform --service gitlab-office
GitKeyRouter.exe test-route camus0109 --url https://github.com/camus0109/panel-terraria.git
GitKeyRouter.exe test-route camus0109 --url https://github.com/camus0109/panel-terraria.git --connect
GitKeyRouter.exe test-ssh github-camus
GitKeyRouter.exe test-ssh github-camus --verbose
GitKeyRouter.exe version
GitKeyRouter.exe help
```

`apply` 默认只显示 SSH diff 和 Git rewrite 计划。只有带 `--yes` 才执行修改。`apply-profiles` 采用相同策略，默认只显示条件 Git Config diff。

`test-route --connect` 必须同时提供真实 `--url`，避免程序对虚构仓库发起网络请求。

CLI 诊断退出码：

- `0`：无警告和错误
- `1`：存在警告
- `2`：存在错误或连接测试失败
- `3`：参数错误或程序自身执行失败

## 配置示例

```json
{
  "SchemaVersion": 3,
  "GitServices": [
    {
      "Id": "github.com",
      "DisplayName": "GitHub.com",
      "ProviderKind": "GitHub",
      "HostName": "github.com",
      "SshPort": null,
      "SshUser": "git",
      "WebBaseUrl": "https://github.com",
      "AllowInsecureHttp": false,
      "EnableExtendedSshUrlRewrites": false,
      "IsBuiltIn": true
    }
  ],
  "Identities": [
    {
      "Id": "7b90999f7ce643fbb07eb4b94f802579",
      "ServiceInstanceId": "github.com",
      "DisplayName": "Camus GitHub",
      "AccountName": "camus0109",
      "HostAlias": "github-camus",
      "PrivateKeyPath": "C:\\Users\\fgc01\\.ssh\\id_ed25519_github_camus",
      "PublicKeyPath": "C:\\Users\\fgc01\\.ssh\\id_ed25519_github_camus.pub",
      "EmailOrComment": "camus0109",
      "CreatedAt": "2026-07-18T08:30:45+00:00"
    }
  ],
  "RepositoryRoutes": [
    {
      "ServiceInstanceId": "github.com",
      "NamespacePath": "camus0109",
      "IdentityId": "7b90999f7ce643fbb07eb4b94f802579",
      "Enabled": true
    }
  ]
}
```

## 配置升级

0.3 使用 Schema 3。读取 Schema 1 配置时仍会自动映射到内置 GitHub.com 服务；Schema 2 配置会保留全部服务、身份和仓库路由，并补充空的 Git Profiles 集合。身份、HostAlias、SSH managed block 和原有 GitHub rewrite 输出保持兼容；下次保存时写为 Schema 3。修改配置前仍会创建快照。

## 输入校验

GitHub Owner 和 HostAlias 使用：

```regex
^[A-Za-z0-9_.-]+$
```

额外限制：

- GitHub Owner 不允许斜杠；GitLab、Gitea 和通用服务允许以 `/` 分隔的多级 Namespace
- HostAlias 不允许空格、斜杠、冒号、通配符或控制字符
- HostAlias 不能直接使用已配置服务的真实主机名
- 每个身份的 HostAlias 必须唯一
- 同一 Git 服务中的一个启用 Namespace 只能指向一个身份
- 路由身份必须属于同一个 Git 服务
- 私钥和公钥路径必须为不同的绝对路径

## 本地构建和验证

在安装 .NET 8 SDK 的 Windows PowerShell 中：

```powershell
dotnet restore .\GitKeyRouter.sln
dotnet format .\GitKeyRouter.sln
dotnet build .\GitKeyRouter.sln -c Release
dotnet test .\GitKeyRouter.sln -c Release
dotnet publish .\src\GitKeyRouter.App\GitKeyRouter.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:PublishTrimmed=false `
  -p:EnableCompressionInSingleFile=true
```

也可以运行：

```powershell
.\scripts\Publish-WinX64.ps1
```

暂时跳过测试：

```powershell
.\scripts\Publish-WinX64.ps1 -SkipTests
```

最终发布目录：

```text
artifacts\publish\win-x64\
```

## 测试隔离

进程操作通过接口抽象，绝大多数测试使用内存或临时目录。

Git 集成测试通过：

```text
GIT_CONFIG_GLOBAL=<temporary-file>
```

隔离全局配置，不修改开发机真实 Git 配置。

## 常见错误

### 找不到 git.exe

安装 Git for Windows 或将其加入 `PATH`。GitKeyRouter 不会自动安装。

### 找不到 ssh.exe / ssh-keygen.exe

在 Windows“可选功能”中启用 OpenSSH Client，或确认 Git for Windows 的 OpenSSH 目录存在。

### Permission denied (publickey)

检查：

1. 公钥是否添加到正确 Git 服务账户
2. SSH Config 的 IdentityFile 是否指向正确私钥
3. HostAlias 是否与仓库路由 rewrite 的 Base URL 一致
4. 私钥文件是否存在

### Could not resolve hostname github-camus

通常表示 SSH Config 中缺少对应 `Host github-camus`，或 managed block 尚未同步。

### Host key verification failed

检查 `%USERPROFILE%\.ssh\known_hosts` 和当前网络环境。GitKeyRouter 不会自动删除 host key。

### URL 没有被重写

在“Git 重写配置”检查：

- 对应 Git 服务和 Namespace 路由是否启用
- HTTPS / SSH insteadOf 是否为 `Correct`
- 是否存在更长或冲突的前缀
- 输入 URL 是否包含与 Namespace 对应的完整路径前缀

### config.json 损坏

程序会停止保存并显示 JSON 解析错误，不会自动覆盖损坏文件。请手动修复或从“备份与恢复”恢复。

## 项目结构

```text
src/
  GitKeyRouter.App/             WinForms、CLI、界面编排
  GitKeyRouter.Core/            模型、校验、业务服务、诊断
  GitKeyRouter.Infrastructure/  Git、SSH、进程、文件、备份实现

tests/
  GitKeyRouter.Tests/           单元测试和隔离 Git 集成测试
```

更详细设计见：

- `docs/architecture.md`
- `docs/backup-and-restore.md`
- `docs/troubleshooting.md`
