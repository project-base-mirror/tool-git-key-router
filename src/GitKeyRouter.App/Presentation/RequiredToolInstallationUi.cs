using System.Diagnostics;
using System.Text;
using GitKeyRouter.Core.Models;

namespace GitKeyRouter.App.Presentation;

public static class RequiredToolInstallationUi
{
    public static async Task<bool> CheckAndOfferAsync(
        IWin32Window owner,
        ApplicationServices services,
        Action<string> status,
        bool showHealthyMessage)
    {
        status("正在检测必需软件...");
        RequiredToolInstallPlan plan;
        try
        {
            plan = await services.RequiredToolInstallerService.BuildPlanAsync();
        }
        catch (Exception exception)
        {
            status("必需软件检测失败");
            MessageBox.Show(
                owner,
                exception.Message,
                "软件检测失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }

        if (!plan.HasMissingTools)
        {
            status("必需软件均已安装");
            if (showHealthyMessage)
            {
                MessageBox.Show(
                    owner,
                    "Git、SSH 和 ssh-keygen 均已安装并可正常检测。",
                    "软件环境正常",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            return true;
        }

        var message = FormatPlan(plan);
        if (!plan.CanInstallAllAutomatically)
        {
            var openResult = MessageBox.Show(
                owner,
                message + Environment.NewLine + Environment.NewLine
                + "部分组件无法自动安装。是否打开对应的官方安装入口？",
                "检测到缺失软件",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (openResult == DialogResult.Yes)
            {
                OpenManualInstallLocations(plan);
            }

            status("存在缺失的必需软件");
            return false;
        }

        var installResult = MessageBox.Show(
            owner,
            message + Environment.NewLine + Environment.NewLine
            + "是否现在下载并安装？安装过程中可能出现 UAC 确认窗口。",
            "安装必需软件",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (installResult != DialogResult.Yes)
        {
            status("已取消必需软件安装");
            return false;
        }

        status("正在下载并安装必需软件...");
        var result = await services.RequiredToolInstallerService.InstallMissingAsync();
        if (!result.Success || result.Value is null)
        {
            status("必需软件安装失败");
            UiHelpers.ShowErrors(owner, result);
            return false;
        }

        status("必需软件安装完成");
        MessageBox.Show(
            owner,
            FormatResult(result.Value),
            "安装完成",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        return true;
    }

    private static string FormatPlan(RequiredToolInstallPlan plan)
    {
        var builder = new StringBuilder();
        builder.AppendLine("检测到以下必需软件缺失：");
        builder.AppendLine();
        foreach (var item in plan.MissingTools)
        {
            builder.AppendLine($"• {item.DisplayName}");
            builder.AppendLine($"  原因：{item.Reason}");
            builder.AppendLine($"  安装：{item.InstallMethod}");
        }

        builder.AppendLine();
        builder.AppendLine("程序只使用 WinGet、Windows 可选功能或官方安装页面，不会运行来源不明的安装包。");
        return builder.ToString().TrimEnd();
    }

    private static string FormatResult(RequiredToolInstallResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("必需软件已安装并重新检测：");
        builder.AppendLine();
        builder.AppendLine($"Git：{Describe(result.After.Git)}");
        builder.AppendLine($"SSH：{Describe(result.After.Ssh)}");
        builder.AppendLine($"ssh-keygen：{Describe(result.After.SshKeygen)}");
        if (result.Steps.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("执行步骤：");
            foreach (var step in result.Steps)
            {
                builder.AppendLine($"• {step.DisplayName}：{(step.Success ? "成功" : "失败")}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string Describe(ExecutableInfo executable)
        => executable.Exists
            ? $"已找到（{executable.Version ?? executable.SelectedPath ?? "版本未知"}）"
            : "仍然缺失";

    private static void OpenManualInstallLocations(RequiredToolInstallPlan plan)
    {
        foreach (var uri in plan.MissingTools
                     .Where(item => !item.CanInstallAutomatically)
                     .Select(item => item.ManualInstallUri)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = uri,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Failure to open one official URI should not block the remaining entries.
            }
        }
    }
}
