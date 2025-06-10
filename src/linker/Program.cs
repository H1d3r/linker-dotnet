﻿using linker.libs;
using System.ServiceProcess;
using System.Diagnostics;
using linker.messenger.entry;
using System.Text;
using System.Text.Json;

namespace linker
{
    internal class Program
    {
        static async Task Main(string[] args)
        {

#if DEBUG
#else
            //添加防火墙，不添加ICMP
            linker.libs.FireWallHelper.Write(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
#endif
            //全局异常
            AppDomain.CurrentDomain.UnhandledException += (a, b) =>
            {
                LoggerHelper.Instance.Error(b.ExceptionObject + "");
            };
            /*
            TaskScheduler.UnobservedTaskException += (a, b) =>
            {
                if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                    LoggerHelper.Instance.Error(b.Exception + "");
            };
            */


            //线程数
            //ThreadPool.SetMinThreads(1024, 1024);
            //ThreadPool.SetMaxThreads(65535, 65535);

            string serviceDirectory = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            Directory.SetCurrentDirectory(serviceDirectory);

            //windows服务运行
            if (Environment.UserInteractive == false && OperatingSystem.IsWindows())
            {
                ServiceBase[] ServicesToRun;
                ServicesToRun = new ServiceBase[]
                {
                    new Service()
                };
                ServiceBase.Run(ServicesToRun);
            }
            //正常运行
            else
            {
                Run(args);
                await Helper.Await().ConfigureAwait(false);
            }

        }

        public static void Run(string[] args)
        {
            LinkerMessengerEntry.Initialize();
            LinkerMessengerEntry.Build();

            using JsonDocument json = ParseArgs(args);
            LinkerMessengerEntry.Setup(ExcludeModule.None, json);

            LoggerHelper.Instance.Warning($"current version : {VersionHelper.Version}");
            LoggerHelper.Instance.Warning($"linker env is docker : {Environment.GetEnvironmentVariable("SNLTTY_LINKER_IS_DOCKER")}");
            LoggerHelper.Instance.Warning($"linker env os : {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
            LoggerHelper.Instance.Debug($"linker are running....");

            GCHelper.FlushMemory();
            GCHelper.EmptyWorkingSet();
        }

        private static JsonDocument ParseArgs(string[] args)
        {
            JsonDocument json = null;
            try
            {
                json = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(args[0])));
            }
            catch (Exception ex)
            {
                if (args.Length > 0)
                {
                    LoggerHelper.Instance.Error(args[0]);
                    LoggerHelper.Instance.Error($"args parse fail {ex}");
                }
            }
            return json;
        }
    }

}