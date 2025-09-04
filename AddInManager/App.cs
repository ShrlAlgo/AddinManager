using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using AddInManager.Properties;

using Autodesk.Private.InfoCenter;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Windows;


namespace AddInManager
{
    internal class App : IExternalApplication
    {
        public static string RevitVersion { get; set; }
        private static readonly Dictionary<string, string> KnownAssemblies = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            { "System.Resources.Extensions", "System.Resources.Extensions.dll" },
            { "System.Runtime.CompilerServices.Unsafe", "System.Runtime.CompilerServices.Unsafe.dll" }
        };
        private static readonly ConcurrentDictionary<string, Assembly> AssemblyCache = new ConcurrentDictionary<string, Assembly>(StringComparer.Ordinal);
        private static readonly string BaseDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        public Result OnStartup(UIControlledApplication application)
        {
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
            CreateRibbonPanel(application);
            return Result.Succeeded;
        }
        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Cancelled;
        }
        private static void CreateRibbonPanel(UIControlledApplication application)
        {
            RevitVersion = application.ControlledApplication.VersionNumber;
            var ribbonPanel = application.CreateRibbonPanel("开发工具");
            var pulldownButtonData = new PulldownButtonData("选项", "插件管理");
            var pulldownButton = (PulldownButton)ribbonPanel.AddItem(pulldownButtonData);
            pulldownButton.Image = ToImageSource(Resources.Develop_16);
            pulldownButton.LargeImage = ToImageSource(Resources.Develop_32);
            AddPushButton(pulldownButton, typeof(CAddInManagerManual), "插件管理(手动模式)");
            AddPushButton(pulldownButton, typeof(CAddInManagerFaceless), "插件管理(手动模式,无界面)");
            AddPushButton(pulldownButton, typeof(CAddInManagerReadOnly), "插件管理(只读模式)");
            var tab = ComponentManager.Ribbon.FindTab("Modify");
            if (tab != null)
            {
                var adwPanel = new Autodesk.Windows.RibbonPanel();
                adwPanel.CopyFrom(GetRibbonPanel(ribbonPanel));
                tab.Panels.Add(adwPanel);
            }

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

        private static void AddPushButton(PulldownButton pullDownButton, Type command, string buttonText)
        {
            var buttonData = new PushButtonData(command.FullName, buttonText, Assembly.GetAssembly(command).Location, command.FullName);
            pullDownButton.AddPushButton(buttonData);
        }

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
                    Debug.WriteLine(ex.Message);
                }
            }

            return null;
        }

    }
}
