﻿using linker.libs.web;
using linker.messenger.relay.client;
using linker.messenger.relay.messenger;
using linker.messenger.relay.server;
using linker.messenger.relay.server.validator;
using linker.messenger.sync;
using Microsoft.Extensions.DependencyInjection;
namespace linker.messenger.relay
{
    public static class Entry
    {
        public static ServiceCollection AddRelayClient(this ServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton<RelayClientTransfer>();
            serviceCollection.AddSingleton<RelayClientMessenger>();

            serviceCollection.AddSingleton<RelaySyncDefault>();

            serviceCollection.AddSingleton<RelayApiController>();

            serviceCollection.AddSingleton<RelayClientTestTransfer>();

            return serviceCollection;
        }
        public static ServiceProvider UseRelayClient(this ServiceProvider serviceProvider)
        {
            IMessengerResolver messengerResolver = serviceProvider.GetService<IMessengerResolver>();
            messengerResolver.AddMessenger(new List<IMessenger> { serviceProvider.GetService<RelayClientMessenger>() });

            SyncTreansfer syncTreansfer = serviceProvider.GetService<SyncTreansfer>();
            syncTreansfer.AddSyncs(new List<ISync> {  serviceProvider.GetService<RelaySyncDefault>() });

            linker.messenger.api.IWebServer apiServer = serviceProvider.GetService<linker.messenger.api.IWebServer>();
            apiServer.AddPlugins(new List<IApiController> { serviceProvider.GetService<RelayApiController>() });

            RelayClientTestTransfer relayClientTestTransfer = serviceProvider.GetService<RelayClientTestTransfer>();

            return serviceProvider;
        }


        public static ServiceCollection AddRelayServer(this ServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton<RelayServerMessenger>();
            serviceCollection.AddSingleton<RelayServerNodeTransfer>();
            serviceCollection.AddSingleton<RelayServerMasterTransfer>();
            serviceCollection.AddSingleton<RelayServerReportResolver>();
            serviceCollection.AddSingleton<RelayServerResolver>();

            serviceCollection.AddSingleton<IRelayServerCaching, RelayServerCachingMemory>();

            serviceCollection.AddSingleton<RelayServerValidatorTransfer>();

            serviceCollection.AddSingleton<IRelayServerWhiteListStore, RelayServerWhiteListStore>();
            serviceCollection.AddSingleton<IRelayServerCdkeyStore, RelayServerCdkeyStore>();

            return serviceCollection;
        }
        public static ServiceProvider UseRelayServer(this ServiceProvider serviceProvider)
        {
            IMessengerResolver messengerResolver = serviceProvider.GetService<IMessengerResolver>();
            messengerResolver.AddMessenger(new List<IMessenger> { serviceProvider.GetService<RelayServerMessenger>() });

            ResolverTransfer resolverTransfer = serviceProvider.GetService<ResolverTransfer>();
            resolverTransfer.AddResolvers(new List<IResolver>
            {
                serviceProvider.GetService<RelayServerReportResolver>(),
                serviceProvider.GetService<RelayServerResolver>(),
            });

            RelayServerNodeTransfer relayServerNodeTransfer = serviceProvider.GetService<RelayServerNodeTransfer>();
            RelayServerMasterTransfer relayServerMasterTransfer = serviceProvider.GetService<RelayServerMasterTransfer>();
            return serviceProvider;
        }
    }
}
