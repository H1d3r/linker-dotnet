﻿using linker.libs;
using linker.messenger.decenter;
using linker.messenger.signin;
using System.Collections.Concurrent;
namespace linker.messenger.tunnel
{
    public sealed class TunnelDecenter : IDecenter
    {
        public string Name => "tunnel";
        public VersionManager PushVersion { get; } = new VersionManager();
        public VersionManager DataVersion { get; } = new VersionManager();
        public ConcurrentDictionary<string, TunnelRouteLevelInfo> Config { get; } = new ConcurrentDictionary<string, TunnelRouteLevelInfo>();

        private readonly ITunnelClientStore tunnelClientStore;
        private readonly TunnelNetworkTransfer tunnelNetworkTransfer;
        private readonly ISerializer serializer;
        private readonly SignInClientState signInClientState;

        public TunnelDecenter(ITunnelClientStore tunnelClientStore, TunnelNetworkTransfer tunnelNetworkTransfer, ISerializer serializer, SignInClientState signInClientState)
        {
            this.tunnelClientStore = tunnelClientStore;
            tunnelClientStore.OnChanged += Refresh;
            this.tunnelNetworkTransfer = tunnelNetworkTransfer;
            this.serializer = serializer;
            this.signInClientState = signInClientState;

        }
        public void Refresh()
        {
            PushVersion.Increment();
        }
        public Memory<byte> GetData()
        {
            return serializer.Serialize(GetLocalRouteLevel());
        }
        public void AddData(Memory<byte> data)
        {
            TunnelRouteLevelInfo tunnelTransportRouteLevelInfo = serializer.Deserialize<TunnelRouteLevelInfo>(data.Span);
            Config.AddOrUpdate(tunnelTransportRouteLevelInfo.MachineId, tunnelTransportRouteLevelInfo, (a, b) => tunnelTransportRouteLevelInfo);
        }
        public void AddData(List<ReadOnlyMemory<byte>> data)
        {
            List<TunnelRouteLevelInfo> list = data.Select(c => serializer.Deserialize<TunnelRouteLevelInfo>(c.Span)).ToList();
            foreach (var item in list)
            {
                Config.AddOrUpdate(item.MachineId, item, (a, b) => item);
            }
        }
        public void ClearData()
        {
            Config.Clear();
        }
        public void ProcData()
        {
        }

        private TunnelRouteLevelInfo GetLocalRouteLevel()
        {
            return new TunnelRouteLevelInfo
            {
                MachineId = signInClientState.Connection?.Id ?? string.Empty,
                RouteLevel = tunnelClientStore.Network.RouteLevel,
                NeedReboot = false,
                PortMapLan = tunnelClientStore.PortMapPrivate,
                PortMapWan = tunnelClientStore.PortMapPublic,
                RouteLevelPlus = tunnelClientStore.RouteLevelPlus,
                Net = tunnelClientStore.Network.Net
            };
        }
    }
}
