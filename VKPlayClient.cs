using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VKPlay.Commons;

namespace VKPlayLibrary
{
    public class VKPlayClient : LibraryClient
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public override string Icon => VKPlayLauncher.Icon;

        public override bool IsInstalled => VKPlayLauncher.IsInstalled;

        public override void Open()
        {
            VKPlayLauncher.StartClient();
        }

        public override void Shutdown()
        {
            var mainProc = Process.GetProcessesByName("GameCenter").FirstOrDefault();
            if (mainProc == null)
            {
                logger.Info("VKPlay client is no longer running, no need to shut it down.");
                return;
            }

            var procRes = ProcessStarter.StartProcessWait(CmdLineTools.TaskKill, $"/f /pid {mainProc.Id}", null, out var stdOut, out var stdErr);
            if (procRes != 0)
            {
                logger.Error($"Failed to close VKPlay client: {procRes}, {stdErr}");
            }
        }
    }
}