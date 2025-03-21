﻿using linker.libs;
using linker.libs.web;
using Microsoft.Extensions.DependencyInjection;
namespace linker.messenger.api
{
    public static class Entry
    {
        public static ServiceCollection AddApiClient(this ServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton<IApiServer, ApiServer>();
            serviceCollection.AddSingleton<IWebServer, WebServer>();
            return serviceCollection;
        }
        public static ServiceProvider UseApiClient(this ServiceProvider serviceProvider)
        {
            IApiStore apiStore = serviceProvider.GetService<IApiStore>();
            IAccessStore accessStore = serviceProvider.GetService<IAccessStore>();
            if (apiStore.Info.ApiPort > 0 && accessStore.HasAccess(AccessValue.Api))
            {
                LoggerHelper.Instance.Info($"start client api");
                IApiServer server = serviceProvider.GetService<IApiServer>();
                server.Websocket(apiStore.Info.ApiPort, apiStore.Info.ApiPassword);
                LoggerHelper.Instance.Warning($"client api listen:{apiStore.Info.ApiPort}");
                if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                    LoggerHelper.Instance.Warning($"client api password:{apiStore.Info.ApiPassword}");
            }

            if (apiStore.Info.WebPort > 0 && accessStore.HasAccess(AccessValue.Web))
            {
                LoggerHelper.Instance.Info($"start client web");
                IWebServer webServer = serviceProvider.GetService<IWebServer>();
                webServer.Start(apiStore.Info.WebPort, apiStore.Info.WebRoot);
                LoggerHelper.Instance.Warning($"client web listen:{apiStore.Info.WebPort}");
            }
            return serviceProvider;
        }
    }
}
