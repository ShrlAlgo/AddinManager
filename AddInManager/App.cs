using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;

using AddInManager.Localization;
using AddInManager.Properties;

using Autodesk.Revit.UI;
using Autodesk.Windows;


namespace AddInManager
{
    internal class App : IExternalApplication
    {
        public static string RevitVersion { get; set; }
        private const string RibbonPulldownInternalName = "AddinManager.Options";
        private static Autodesk.Revit.UI.RibbonPanel s_ribbonPanel;
        private static PulldownButton s_pulldownButton;
        private static PushButton s_manualButton;
        private static PushButton s_facelessButton;
        private static PushButton s_readOnlyButton;
        private static PushButton s_logViewerButton;
        private static PushButton s_dependencyGraphButton;
        private static PushButton s_checkUpdatesButton;
#if NET48
        private static readonly Dictionary<string, string> KnownAssemblies = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            { "System.Resources.Extensions", "System.Resources.Extensions.dll" },
            { "System.Runtime.CompilerServices.Unsafe", "System.Runtime.CompilerServices.Unsafe.dll" }
        };
        private static readonly ConcurrentDictionary<string, Assembly> AssemblyCache = new ConcurrentDictionary<string, Assembly>(StringComparer.Ordinal);
        private static readonly string BaseDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
#endif

        public Result OnStartup(UIControlledApplication application)
        {
#if NET48
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
#endif
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            DebugTools.DebugLogger.Instance.StartCapture();
            FileUtils.CleanupTempFolders();
            LanguageManager.ApplySavedLanguage();
            CreateRibbonPanel(application);
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            Commands.CCheckForUpdates.ConfirmShutdown();
#if NET48
            AppDomain.CurrentDomain.AssemblyResolve -= CurrentDomain_AssemblyResolve;
#endif
            AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
            DebugTools.DebugLogger.Instance.StopCapture();
            return Result.Cancelled;
        }

        private static void CreateRibbonPanel(UIControlledApplication application)
        {
            RevitVersion = application.ControlledApplication.VersionNumber;
            s_ribbonPanel = application.CreateRibbonPanel(Resources.RibbonPanelName);
            var pulldownButtonData = new PulldownButtonData(RibbonPulldownInternalName, Resources.RibbonPulldownDisplayName);
            s_pulldownButton = (PulldownButton)s_ribbonPanel.AddItem(pulldownButtonData);
            s_pulldownButton.Image = ToImageSource(Resources.Develop_16);
            s_pulldownButton.LargeImage = ToImageSource(Resources.Develop_32);
            s_manualButton = AddPushButton(s_pulldownButton, typeof(CAddInManagerManual), Resources.RibbonManualMode);
            s_facelessButton = AddPushButton(s_pulldownButton, typeof(CAddInManagerFaceless), Resources.RibbonManualModeFaceless);
            s_readOnlyButton = AddPushButton(s_pulldownButton, typeof(CAddInManagerReadOnly), Resources.RibbonReadOnlyMode);
            s_pulldownButton.AddSeparator();
            s_logViewerButton = AddPushButton(s_pulldownButton, typeof(Commands.CDebugLogViewer), Resources.RibbonDebugLogViewer);
            s_dependencyGraphButton = AddPushButton(s_pulldownButton, typeof(Commands.CDebugDependencyGraph), Resources.RibbonDependencyAnalyzer);
            s_pulldownButton.AddSeparator();
            s_checkUpdatesButton = AddPushButton(s_pulldownButton, typeof(Commands.CCheckForUpdates), Resources.BtnCheckUpdates);
            s_checkUpdatesButton.AvailabilityClassName = typeof(Commands.CCheckForUpdatesAvailability).FullName;
            var tab = ComponentManager.Ribbon.FindTab("Modify");
            if (tab == null) return;
            var adwPanel = new Autodesk.Windows.RibbonPanel();
            adwPanel.CopyFrom(GetRibbonPanel(s_ribbonPanel));
            tab.Panels.Add(adwPanel);
        }

        internal static BitmapImage ToImageSource(Bitmap bitmap)
        {
            var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            var image = new BitmapImage();
            image.BeginInit();
            ms.Seek(0, SeekOrigin.Begin);
            image.StreamSource = ms;
            image.EndInit();
            return image;
        }

        private static readonly FieldInfo RibbonPanelField = typeof(Autodesk.Revit.UI.RibbonPanel).GetField("m_RibbonPanel", BindingFlags.Instance | BindingFlags.NonPublic);

        public static Autodesk.Windows.RibbonPanel GetRibbonPanel(Autodesk.Revit.UI.RibbonPanel panel)
        {
            return RibbonPanelField.GetValue(panel) as Autodesk.Windows.RibbonPanel;
        }

        private static PushButton AddPushButton(PulldownButton pullDownButton, Type command, string buttonText)
        {
            var buttonData = new PushButtonData(command.FullName, buttonText, Assembly.GetAssembly(command).Location, command.FullName);
            return pullDownButton.AddPushButton(buttonData);
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                // 终止阶段只记录，不创建可能再次触发资源或线程异常的 WPF 窗口。
                var ex = e.ExceptionObject as Exception;
                DebugTools.DebugLogger.Instance.Error(ex ?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown error"),
                    $"UnhandledException; IsTerminating={e.IsTerminating}");
            }
            catch
            {
                // 异常记录失败不干扰宿主的终止流程。
            }
        }

#if NET48
        private Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            var requestedAssemblyName = new AssemblyName(args.Name).Name;
            if (AssemblyCache.TryGetValue(requestedAssemblyName, out var cachedAssembly))
            {
                return cachedAssembly;
            }
            if (KnownAssemblies.TryGetValue(requestedAssemblyName, out var assemblyFile))
            {
                var assemblyPath = Path.Combine(BaseDirectory, assemblyFile);

                try
                {
                    var assembly = Assembly.LoadFrom(assemblyPath);
                    AssemblyCache.TryAdd(requestedAssemblyName, assembly);
                    return assembly;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(ex.Message);
                }
            }

            return null;
        }
#endif

        internal static void RefreshRibbonLanguage()
        {
            if (s_pulldownButton == null)
            {
                return;
            }

            s_pulldownButton.ItemText = Resources.RibbonPulldownDisplayName;

            s_ribbonPanel?.Title = Resources.RibbonPanelName;
            s_manualButton?.ItemText = Resources.RibbonManualMode;
            s_facelessButton?.ItemText = Resources.RibbonManualModeFaceless;
            s_readOnlyButton?.ItemText = Resources.RibbonReadOnlyMode;
            s_logViewerButton?.ItemText = Resources.RibbonDebugLogViewer;
            s_dependencyGraphButton?.ItemText = Resources.RibbonDependencyAnalyzer;
            s_checkUpdatesButton?.ItemText = Resources.BtnCheckUpdates;
        }
    }
}
