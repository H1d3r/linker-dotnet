﻿using linker.libs;
using System.Collections.Concurrent;
using linker.messenger.decenter;
using linker.messenger.signin;

namespace linker.messenger.sforward.client
{
    public sealed class SForwardDecenter : IDecenter
    {
        public string Name => "sforward";
        public VersionManager PushVersion { get; } = new VersionManager();
        public VersionManager DataVersion { get; } = new VersionManager();
        public ConcurrentDictionary<string, int> CountDic { get; } = new ConcurrentDictionary<string, int>();

        private readonly ISignInClientStore signInClientStore;
        private readonly ISForwardClientStore sForwardClientStore;
        private readonly ISerializer serializer;
        public SForwardDecenter(ISignInClientStore signInClientStore, ISForwardClientStore sForwardClientStore, ISerializer serializer, SForwardClientTransfer sForwardClientTransfer)
        {
            this.signInClientStore = signInClientStore;
            this.sForwardClientStore = sForwardClientStore;
            this.serializer = serializer;
            sForwardClientTransfer.OnChanged += Refresh;
        }

        public void Refresh()
        {
            PushVersion.Increment();
        }

        public Memory<byte> GetData()
        {
            SForwardCountInfo info = new SForwardCountInfo { MachineId = signInClientStore.Id, Count = sForwardClientStore.Count() };
            CountDic.AddOrUpdate(info.MachineId, info.Count, (a, b) => info.Count);
            DataVersion.Increment();
            return serializer.Serialize(info);
        }
        public void SetData(Memory<byte> data)
        {
            SForwardCountInfo info = serializer.Deserialize<SForwardCountInfo>(data.Span);
            CountDic.AddOrUpdate(info.MachineId, info.Count, (a, b) => info.Count);
            DataVersion.Increment();
        }
        public void SetData(List<ReadOnlyMemory<byte>> data)
        {
            List<SForwardCountInfo> list = data.Select(c => serializer.Deserialize<SForwardCountInfo>(c.Span)).ToList();
            foreach (var info in list)
            {
                CountDic.AddOrUpdate(info.MachineId, info.Count, (a, b) => info.Count);
            }
            DataVersion.Increment();
        }
    }

  
}
