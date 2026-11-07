using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace PBZTrainer
{
    public partial class App : Application
    {
        /// <summary>启动参数 --page &lt;tag&gt; 可直接打开某个页面（basic/advanced/add/inv/spawn/map/completion/minimap/esp/fav）。</summary>
        public static string StartupPage;

        private static string ParsePage(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], "--page", StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }

        private static string LogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "影之刃零修改器", "crash.log");

        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += (s, args) =>
            {
                Log(args.Exception);
                MessageBox.Show(args.Exception.ToString(), "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
                Shutdown(1);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, args) => Log(args.ExceptionObject as Exception);
            StartupPage = ParsePage(e.Args);
            base.OnStartup(e);
        }

        private static void Log(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                File.AppendAllText(LogPath, DateTime.Now.ToString("s") + " " + ex + Environment.NewLine + Environment.NewLine);
            }
            catch { }
        }
    }
}
