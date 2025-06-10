﻿using linker.libs;
using linker.libs.timer;
using linker.libs.web;
using linker.messenger.api;
using Microsoft.Extensions.DependencyInjection;
namespace linker.messenger.logger
{
    public static class Entry
    {
        public static ServiceCollection AddLogger(this ServiceCollection serviceCollection)
        {
            LoggerConsole();
            return serviceCollection;
        }
        public static ServiceProvider UseLogger(this ServiceProvider serviceProvider)
        {
            
            return serviceProvider;
        }

        public static ServiceCollection AddLoggerClient(this ServiceCollection serviceCollection)
        {
            
            serviceCollection.AddSingleton<LoggerApiController>();
            return serviceCollection;
        }
        public static ServiceProvider UseLoggerClient(this ServiceProvider serviceProvider)
        {
            linker.messenger.api.IWebServer apiServer = serviceProvider.GetService<linker.messenger.api.IWebServer>();
            apiServer.AddPlugins(new List<IApiController> { serviceProvider.GetService<LoggerApiController>() });

            IAccessStore accessStore= serviceProvider.GetService<IAccessStore>();
            ILoggerStore loggerStore= serviceProvider.GetService<ILoggerStore>();
            if (accessStore.HasAccess(AccessValue.LoggerLevel) == false)
            {
                loggerStore.SetLevel( libs.LoggerTypes.WARNING);
                loggerStore.Confirm();
            }

            return serviceProvider;
        }


        private static void LoggerConsole()
        {
            if (Directory.Exists(Path.Join(Helper.CurrentDirectory, "logs")) == false)
            {
                Directory.CreateDirectory(Path.Join(Helper.CurrentDirectory, "logs"));
            }
            LoggerHelper.Instance.OnLogger += (model) =>
            {
                ConsoleColor currentForeColor = Console.ForegroundColor;
                switch (model.Type)
                {
                    case LoggerTypes.DEBUG:
                        Console.ForegroundColor = ConsoleColor.Blue;
                        break;
                    case LoggerTypes.INFO:
                        Console.ForegroundColor = ConsoleColor.White;
                        break;
                    case LoggerTypes.WARNING:
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        break;
                    case LoggerTypes.ERROR:
                        Console.ForegroundColor = ConsoleColor.Red;
                        break;
                    default:
                        break;
                }
                string line = $"[{model.Type,-7}][{model.Time:yyyy-MM-dd HH:mm:ss}]:{model.Content}";
                Console.WriteLine(line);
                Console.ForegroundColor = currentForeColor;
                try
                {
                    using StreamWriter sw = File.AppendText(Path.Join(Helper.CurrentDirectory, "logs", $"{DateTime.Now:yyyy-MM-dd}.log"));
                    sw.WriteLine(line);
                    sw.Flush();
                    sw.Close();
                    sw.Dispose();
                }
                catch (Exception)
                {
                }
            };
            TimerHelper.SetIntervalLong(() =>
            {
                string[] files = Directory.GetFiles(Path.Combine(Helper.CurrentDirectory, "logs")).OrderBy(c => c).ToArray();
                for (int i = 0; i < files.Length - 180; i++)
                {
                    try
                    {
                        File.Delete(files[i]);
                    }
                    catch (Exception)
                    {
                    }
                }
            }, 60 * 1000);
        }

    }
}
