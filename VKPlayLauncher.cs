using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using VKPlay.Commons;

namespace VKPlayLibrary
{
    public class VKPlayLauncher
    {
        private static readonly ILogger logger = LogManager.GetLogger();


        public static string ClientExecPath
        {
            get
            {
                var path = InstallationPath;
                return string.IsNullOrEmpty(path) ? string.Empty : GetExecutablePath(path);
            }
        }

        public static string InstallationPath
        {
            get
            {
                var progs = Programs.GetUnistallProgramsList().FirstOrDefault(a =>
                    a.DisplayName == "VK Play Игровой центр" &&
                        !a.InstallLocation.IsNullOrEmpty() &&
                        File.Exists(Path.Combine(a.InstallLocation, "GameCenter.exe")));
                if (progs == null)
                {
                    return string.Empty;
                }
                else
                {
                    logger.Debug($"VKInstallPath: {progs.InstallLocation}");
                    return progs.InstallLocation;
                }
            }
        }

        internal static string GetExecutablePath(string rootPath)
        {
            return Path.Combine(rootPath, "GameCenter.exe");
        }

        public static bool IsInstalled
        {
            get
            {
                var path = InstallationPath;
                return !string.IsNullOrEmpty(path) && Directory.Exists(path);
            }
        }

        public static string Icon => Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"Resources\vkplay-ico.png");

        public static void StartClient()
        {
            ProcessStarter.StartProcess(ClientExecPath, string.Empty);
        }
    }
}
