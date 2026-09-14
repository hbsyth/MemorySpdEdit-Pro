using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SetupApp;

internal static class Program
{
    private const string AppDisplayName = "MemorySpdEdit Pro";
    private const string AppDisplayNameCn = "内存SPD修改工具 Pro";
    private const string AppVersion = "Ver.001";
    private const string AppPublisher = "SuperGun";
    private const string AppUrl = "https://github.com/hbsyth/MemorySpdEdit-Pro";
    private const string ExeName = "MemorySpdEdit-Pro.exe";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MemorySpdEditPro";
    private const string PayloadResource = "SetupApp.Payload.MemorySpdEdit-Pro.exe";

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        string defaultDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            AppDisplayName);

        using var form = new Form
        {
            Text = $"{AppDisplayName} {AppVersion} 安装程序",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterScreen,
            ClientSize = new Size(460, 220),
            Font = new Font("Microsoft YaHei UI", 9F),
        };

        var lblTitle = new Label
        {
            Text = $"{AppDisplayNameCn}\n{AppDisplayName} {AppVersion}",
            Location = new Point(20, 16),
            Size = new Size(420, 48),
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
        };

        var lblPath = new Label
        {
            Text = "安装目录：",
            Location = new Point(20, 78),
            AutoSize = true,
        };

        var txtPath = new TextBox
        {
            Text = defaultDir,
            Location = new Point(20, 102),
            Width = 330,
        };

        var btnBrowse = new Button
        {
            Text = "浏览...",
            Location = new Point(360, 100),
            Size = new Size(80, 28),
        };
        btnBrowse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = "选择安装目录",
                SelectedPath = txtPath.Text,
            };
            if (dlg.ShowDialog(form) == DialogResult.OK)
                txtPath.Text = dlg.SelectedPath;
        };

        var chkDesktop = new CheckBox
        {
            Text = "创建桌面快捷方式",
            Checked = true,
            Location = new Point(20, 140),
            AutoSize = true,
        };

        var btnInstall = new Button
        {
            Text = "安装",
            Location = new Point(260, 172),
            Size = new Size(88, 30),
        };
        var btnCancel = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Location = new Point(352, 172),
            Size = new Size(88, 30),
        };

        btnInstall.Click += (_, _) =>
        {
            try
            {
                string dir = txtPath.Text.Trim();
                if (string.IsNullOrWhiteSpace(dir))
                {
                    MessageBox.Show(form, "请选择安装目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                btnInstall.Enabled = false;
                btnBrowse.Enabled = false;
                Cursor.Current = Cursors.WaitCursor;

                Install(dir, chkDesktop.Checked);

                var result = MessageBox.Show(
                    form,
                    "安装完成。是否立即运行？",
                    "安装成功",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);
                if (result == DialogResult.Yes)
                    Process.Start(new ProcessStartInfo(Path.Combine(dir, ExeName)) { UseShellExecute = true });

                form.DialogResult = DialogResult.OK;
                form.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(form, "安装失败：\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                btnInstall.Enabled = true;
                btnBrowse.Enabled = true;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        };

        form.AcceptButton = btnInstall;
        form.CancelButton = btnCancel;
        form.Controls.AddRange([lblTitle, lblPath, txtPath, btnBrowse, chkDesktop, btnInstall, btnCancel]);
        Application.Run(form);
    }

    private static void Install(string installDir, bool createDesktopShortcut)
    {
        Directory.CreateDirectory(installDir);
        string targetExe = Path.Combine(installDir, ExeName);

        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource)
            ?? throw new InvalidOperationException("安装包内未找到程序本体，请重新下载 Setup。"))
        using (var fs = File.Create(targetExe))
            stream.CopyTo(fs);

        string uninstallCmd = $"\"{targetExe}\" --uninstall-placeholder";
        // Dedicated uninstall helper written next to app
        string uninstallPs1 = Path.Combine(installDir, "Uninstall.ps1");
        File.WriteAllText(uninstallPs1, BuildUninstallScript(installDir, createDesktopShortcut));

        string uninstallBat = Path.Combine(installDir, "Uninstall.bat");
        File.WriteAllText(uninstallBat,
            "@echo off\r\n" +
            "powershell -NoProfile -ExecutionPolicy Bypass -File \"%~dp0Uninstall.ps1\"\r\n");

        string startMenuDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            "Programs",
            AppDisplayName);
        Directory.CreateDirectory(startMenuDir);
        CreateShortcut(Path.Combine(startMenuDir, AppDisplayName + ".lnk"), targetExe, AppDisplayNameCn);
        CreateShortcut(Path.Combine(startMenuDir, "卸载 " + AppDisplayName + ".lnk"), uninstallBat, "卸载 " + AppDisplayName);

        string? desktopLnk = null;
        if (createDesktopShortcut)
        {
            desktopLnk = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                AppDisplayName + ".lnk");
            CreateShortcut(desktopLnk, targetExe, AppDisplayNameCn);
        }

        using var key = Registry.LocalMachine.CreateSubKey(UninstallKey)
            ?? throw new InvalidOperationException("无法写入卸载信息（请以管理员身份运行安装程序）。");
        key.SetValue("DisplayName", $"{AppDisplayName} {AppVersion}");
        key.SetValue("DisplayVersion", "0.0.1");
        key.SetValue("Publisher", AppPublisher);
        key.SetValue("URLInfoAbout", AppUrl);
        key.SetValue("InstallLocation", installDir);
        key.SetValue("DisplayIcon", targetExe);
        key.SetValue("UninstallString", $"\"{uninstallBat}\"");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)(new FileInfo(targetExe).Length / 1024), RegistryValueKind.DWord);
        if (desktopLnk != null)
            key.SetValue("DesktopShortcut", desktopLnk);
    }

    private static string BuildUninstallScript(string installDir, bool hadDesktop)
    {
        string startMenu = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            "Programs",
            AppDisplayName).Replace("'", "''");
        string desktop = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            AppDisplayName + ".lnk").Replace("'", "''");
        string dir = installDir.Replace("'", "''");

        return $@"$ErrorActionPreference = 'SilentlyContinue'
Add-Type -AssemblyName System.Windows.Forms
Remove-Item -LiteralPath '{startMenu}' -Recurse -Force
{(hadDesktop ? $"Remove-Item -LiteralPath '{desktop}' -Force" : "")}
Remove-Item -Path 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MemorySpdEditPro' -Recurse -Force
Start-Sleep -Milliseconds 400
Remove-Item -LiteralPath '{dir}' -Recurse -Force
[void][System.Windows.Forms.MessageBox]::Show('已卸载 {AppDisplayName}。','卸载完成')
";
    }

    private static void CreateShortcut(string lnkPath, string targetPath, string description)
    {
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("无法创建快捷方式（WScript.Shell 不可用）。");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(lnkPath);
        shortcut.TargetPath = targetPath;
        shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath)!;
        shortcut.Description = description;
        shortcut.IconLocation = targetPath + ",0";
        shortcut.Save();
        Marshal.FinalReleaseComObject(shortcut);
        Marshal.FinalReleaseComObject(shell);
    }
}
