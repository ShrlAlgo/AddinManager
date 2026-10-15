using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

using AddInManager.Localization;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

using Button = System.Windows.Controls.Button;
using UpdateResources = AddInManager.Properties.Resources;

namespace AddInManager.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class CCheckForUpdates : IExternalCommand
    {
        private static string s_pendingDirectory;
        private static UIApplication s_pendingApplication;
        private static Stopwatch s_exitTimer;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            LanguageManager.ApplySavedLanguage();
            try
            {
                using (var currentProcess = Process.GetCurrentProcess())
                {
                    var otherRevit = false;
                    foreach (var process in Process.GetProcessesByName("Revit"))
                    {
                        using (process)
                        {
                            otherRevit |= process.Id != currentProcess.Id;
                        }
                    }
                    if (otherRevit)
                    {
                        MessageBox.Show(UpdateResources.UpdateOtherRevit, UpdateResources.BtnCheckUpdates);
                        return Result.Cancelled;
                    }

                    var window = new UpdateWindow();
                    new WindowInteropHelper(window).Owner = currentProcess.MainWindowHandle;
                    if (window.ShowDialog() != true)
                    {
                        return Result.Cancelled;
                    }

                    var application = commandData.Application;
                    var exitCommand = RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit);
                    if (!application.CanPostCommand(exitCommand))
                    {
                        throw new System.InvalidOperationException(UpdateResources.UpdateStartFailed);
                    }
                    s_pendingDirectory = window.DownloadDirectory;
                    PrepareInstaller(window.DownloadDirectory, new UpdateSettings
                    {
                        ProcessId = currentProcess.Id,
                        ProcessStartTicks = currentProcess.StartTime.ToUniversalTime().Ticks,
                        RevitPath = currentProcess.MainModule.FileName,
                        InstallerName = Path.GetFileName(window.InstallerPath),
                        ExpectedHash = window.ExpectedHash,
                        ErrorTitle = UpdateResources.BtnCheckUpdates,
                        ErrorFormat = UpdateResources.UpdateInstallFailedFormat,
                        OtherRevitError = UpdateResources.UpdateOtherRevit,
                        ExitError = UpdateResources.UpdateExitTimedOut,
                        HashError = UpdateResources.UpdateHashMismatch
                    });

                    // 在有效 API 上下文中请求正常退出，让 Revit 自行处理未保存文档。
                    s_pendingApplication = application;
                    s_exitTimer = Stopwatch.StartNew();
                    application.Idling += CancelIfExitWasAborted;
                    application.PostCommand(exitCommand);
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                CancelPendingUpdate();
                message = string.Format(UpdateResources.UpdateCheckFailedFormat, ex.Message);
                return Result.Failed;
            }
        }

        // 仅在 Revit 真正进入正常关闭流程后授权安装，崩溃退出不会触发安装。
        internal static void ConfirmShutdown()
        {
            if (s_pendingDirectory == null) return;
            try
            {
                File.WriteAllText(Path.Combine(s_pendingDirectory, "shutdown"), string.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
            ClearPendingUpdate();
        }

        private static void CancelIfExitWasAborted(object sender, IdlingEventArgs e)
        {
            if (s_exitTimer == null) return;
            // 给已投递的退出命令留出处理时间；原生保存对话框期间不会进入空闲状态。
            if (s_exitTimer.Elapsed < TimeSpan.FromSeconds(2))
            {
                e.SetRaiseWithoutDelay();
                return;
            }
            CancelPendingUpdate();
            MessageBox.Show(UpdateResources.UpdateExitCancelled, UpdateResources.BtnCheckUpdates);
        }

        private static void CancelPendingUpdate()
        {
            try
            {
                if (s_pendingDirectory != null)
                {
                    File.WriteAllText(Path.Combine(s_pendingDirectory, "cancelled"), string.Empty);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
            finally
            {
                ClearPendingUpdate();
            }
        }

        private static void ClearPendingUpdate()
        {
            if (s_pendingApplication != null)
            {
                s_pendingApplication.Idling -= CancelIfExitWasAborted;
            }
            s_pendingDirectory = null;
            s_pendingApplication = null;
            s_exitTimer = null;
        }

        private static void PrepareInstaller(string directory, UpdateSettings settings)
        {
            var scriptPath = Path.Combine(directory, "InstallUpdate.ps1");
            var configPath = Path.Combine(directory, "settings.json");
            using (var script = Assembly.GetExecutingAssembly().GetManifestResourceStream("AddInManager.Infrastructure.InstallUpdate.ps1"))
            using (var output = File.Create(scriptPath))
            {
                if (script == null) throw new System.InvalidOperationException(UpdateResources.UpdateStartFailed);
                script.CopyTo(output);
            }
            using (var output = File.Create(configPath))
            {
                new DataContractJsonSerializer(typeof(UpdateSettings)).WriteObject(output, settings);
            }

            var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            using (var helper = Process.Start(new ProcessStartInfo(powershell)
            {
                Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{scriptPath}\" -ConfigPath \"{configPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            }))
            {
                // 确认独立进程已读入配置并就绪后，才允许关闭 Revit。
                var timer = Stopwatch.StartNew();
                while (!File.Exists(Path.Combine(directory, "ready")))
                {
                    if (helper == null || helper.HasExited || timer.Elapsed > TimeSpan.FromSeconds(10))
                    {
                        File.WriteAllText(Path.Combine(directory, "cancelled"), string.Empty);
                        throw new System.InvalidOperationException(UpdateResources.UpdateStartFailed);
                    }
                    Thread.Sleep(50);
                }
            }
        }

        private sealed class UpdateWindow : Window
        {
            private readonly CancellationTokenSource m_cancellation = new CancellationTokenSource();
            private readonly TextBlock m_status = new TextBlock { TextWrapping = TextWrapping.Wrap };
            private readonly ProgressBar m_progress = new ProgressBar { Height = 6, Margin = new Thickness(0, 14, 0, 14), IsIndeterminate = true };

            public string DownloadDirectory { get; private set; }
            public string InstallerPath { get; private set; }
            public string ExpectedHash { get; private set; }

            public UpdateWindow()
            {
                Title = UpdateResources.BtnCheckUpdates;
                Width = 420;
                SizeToContent = SizeToContent.Height;
                ResizeMode = ResizeMode.NoResize;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                ShowInTaskbar = false;
                var panel = new StackPanel { Margin = new Thickness(18) };
                panel.Children.Add(m_status);
                panel.Children.Add(m_progress);
                var cancel = new Button { Content = UpdateResources.UpdateCancel, Padding = new Thickness(14, 4, 14, 4), HorizontalAlignment = HorizontalAlignment.Right, IsCancel = true };
                panel.Children.Add(cancel);
                Content = panel;
                Closed += (sender, args) => m_cancellation.Cancel();
                Loaded += CheckAndDownload;
            }

            private async void CheckAndDownload(object sender, RoutedEventArgs e)
            {
                m_status.Text = UpdateResources.UpdateChecking;
                try
                {
                    var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
                    using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
                    {
                        client.DefaultRequestHeaders.UserAgent.ParseAdd($"AddInManager/{currentVersion.ToString(3)}");
                        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                        ReleaseInfo release;
                        using (var response = await client.GetAsync("https://api.github.com/repos/ShrlAlgo/AddinManager/releases/latest", m_cancellation.Token))
                        {
                            response.EnsureSuccessStatusCode();
                            using (var stream = await response.Content.ReadAsStreamAsync())
                            {
                                release = new DataContractJsonSerializer(typeof(ReleaseInfo)).ReadObject(stream) as ReleaseInfo;
                            }
                        }
                        m_cancellation.Token.ThrowIfCancellationRequested();
                        if (!Version.TryParse(release?.TagName?.TrimStart('v', 'V'), out var latestVersion))
                        {
                            throw new InvalidDataException(UpdateResources.UpdateVersionInvalid);
                        }
                        latestVersion = new Version(latestVersion.Major, latestVersion.Minor, Math.Max(0, latestVersion.Build), Math.Max(0, latestVersion.Revision));
                        if (latestVersion <= currentVersion)
                        {
                            MessageBox.Show(this, string.Format(UpdateResources.UpdateUpToDateFormat, currentVersion.ToString(3)), Title);
                            Close();
                            return;
                        }

                        var installerName = $"RevitAddinManager{latestVersion.ToString(3)}.exe";
                        var asset = release.Assets?.FirstOrDefault(a => a.Name == installerName);
                        if (asset == null || asset.Size <= 0 || asset.Digest == null || !System.Text.RegularExpressions.Regex.IsMatch(asset.Digest, "^sha256:[0-9a-fA-F]{64}$"))
                        {
                            throw new InvalidDataException(UpdateResources.UpdateInstallerMissing);
                        }
                        if (MessageBox.Show(this, string.Format(UpdateResources.UpdateAvailableFormat, latestVersion.ToString(3), currentVersion.ToString(3)), Title, MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
                        {
                            Close();
                            return;
                        }

                        DownloadDirectory = Path.Combine(Path.GetTempPath(), "AddInManager", "Updates", Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(DownloadDirectory);
                        InstallerPath = Path.Combine(DownloadDirectory, installerName);
                        ExpectedHash = asset.Digest.Substring(7);
                        m_cancellation.CancelAfter(TimeSpan.FromMinutes(10));
                        m_status.Text = string.Format(UpdateResources.UpdateDownloadingFormat, 0);
                        m_progress.IsIndeterminate = false;
                        // 只下载本仓库对应标签的安装包，并核对 GitHub 返回的大小和 SHA-256。
                        var url = "https://github.com/ShrlAlgo/AddinManager/releases/download/" + Uri.EscapeDataString(release.TagName) + "/" + installerName;
                        using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, m_cancellation.Token))
                        {
                            response.EnsureSuccessStatusCode();
                            using (var input = await response.Content.ReadAsStreamAsync())
                            using (var output = File.Create(InstallerPath))
                            {
                                var buffer = new byte[81920];
                                long total = 0;
                                int count;
                                while ((count = await input.ReadAsync(buffer, 0, buffer.Length, m_cancellation.Token)) > 0)
                                {
                                    await output.WriteAsync(buffer, 0, count, m_cancellation.Token);
                                    total += count;
                                    m_progress.Value = Math.Min(100, total * 100.0 / asset.Size);
                                    m_status.Text = string.Format(UpdateResources.UpdateDownloadingFormat, (int)m_progress.Value);
                                }
                                if (total != asset.Size) throw new InvalidDataException(UpdateResources.UpdateHashMismatch);
                            }
                        }
                        using (var input = File.OpenRead(InstallerPath))
                        using (var hash = SHA256.Create())
                        {
                            var actualHash = BitConverter.ToString(hash.ComputeHash(input)).Replace("-", string.Empty);
                            if (!actualHash.Equals(ExpectedHash, StringComparison.OrdinalIgnoreCase))
                            {
                                throw new InvalidDataException(UpdateResources.UpdateHashMismatch);
                            }
                        }
                        m_cancellation.Token.ThrowIfCancellationRequested();
                        DialogResult = true;
                    }
                }
                catch (System.OperationCanceledException)
                {
                    if (IsLoaded)
                    {
                        MessageBox.Show(this, UpdateResources.UpdateCheckTimedOut, Title);
                        Close();
                    }
                }
                catch (Exception ex)
                {
                    if (IsLoaded)
                    {
                        MessageBox.Show(this, string.Format(UpdateResources.UpdateCheckFailedFormat, ex.Message), Title);
                        Close();
                    }
                }
                finally
                {
                    m_cancellation.Dispose();
                    if (DialogResult != true && DownloadDirectory != null)
                    {
                        try { Directory.Delete(DownloadDirectory, true); }
                        catch (Exception ex) { Debug.WriteLine(ex); }
                    }
                }
            }
        }

        [DataContract]
        private sealed class ReleaseInfo
        {
            [DataMember(Name = "tag_name")] public string TagName { get; set; }
            [DataMember(Name = "assets")] public ReleaseAsset[] Assets { get; set; }
        }

        [DataContract]
        private sealed class ReleaseAsset
        {
            [DataMember(Name = "name")] public string Name { get; set; }
            [DataMember(Name = "size")] public long Size { get; set; }
            [DataMember(Name = "digest")] public string Digest { get; set; }
        }

        [DataContract]
        private sealed class UpdateSettings
        {
            [DataMember] public int ProcessId { get; set; }
            [DataMember] public long ProcessStartTicks { get; set; }
            [DataMember] public string RevitPath { get; set; }
            [DataMember] public string InstallerName { get; set; }
            [DataMember] public string ExpectedHash { get; set; }
            [DataMember] public string ErrorTitle { get; set; }
            [DataMember] public string ErrorFormat { get; set; }
            [DataMember] public string OtherRevitError { get; set; }
            [DataMember] public string ExitError { get; set; }
            [DataMember] public string HashError { get; set; }
        }
    }

    // 更新入口不依赖项目文档，允许在 Revit 启动页使用。
    public class CCheckForUpdatesAvailability : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
        {
            return true;
        }
    }
}
