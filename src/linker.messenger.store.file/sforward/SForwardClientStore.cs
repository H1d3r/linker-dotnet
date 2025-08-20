﻿using linker.libs.extends;
using linker.libs;
using linker.messenger.sforward;
using linker.messenger.sforward.client;
using LiteDB;

namespace linker.messenger.store.file.sforward
{
    public sealed class SForwardClientStore : ISForwardClientStore
    {
        private readonly FileConfig fileConfig;
        private readonly Storefactory dBfactory;
        private readonly ILiteCollection<SForwardInfo191> liteCollection;

        public SForwardClientStore(FileConfig fileConfig, Storefactory dBfactory)
        {
            this.fileConfig = fileConfig;

            this.dBfactory = dBfactory;
            liteCollection = dBfactory.GetCollection<SForwardInfo191>("sforward");
            liteCollection.UpdateMany(c => new SForwardInfo191 { Started = false }, c => c.Started == true);
        }
        public int Count()
        {
            return liteCollection.Count();
        }

        public IEnumerable<SForwardInfo191> Get()
        {
            return liteCollection.FindAll();
        }

        public SForwardInfo191 Get(long id)
        {
            return liteCollection.FindOne(x => x.Id == id);
        }

        public SForwardInfo191 Get(string domain)
        {
            return liteCollection.FindOne(x => x.Domain == domain);
        }

        public SForwardInfo191 GetPort(int port)
        {
            return liteCollection.FindOne(c => c.RemotePort == port);
        }
        public bool Add(SForwardInfo191 info)
        {
            SForwardInfo191 old = liteCollection.FindOne(c => info.RemotePort > 0 && c.RemotePort == info.RemotePort || string.IsNullOrWhiteSpace(info.Domain) == false && c.Domain == info.Domain);
            if (old != null && old.Id != info.Id)
            {
                return false;
            }

            if (PortRange(info.Domain, out int min, out int max))
            {
                info.RemotePortMin = min;
                info.RemotePortMax = max;
            }
            if (info.Id != 0)
            {
                liteCollection.UpdateMany(c => new SForwardInfo191
                {
                    RemotePort = info.RemotePort,
                    Name = info.Name,
                    LocalEP = info.LocalEP,
                    Domain = info.Domain,
                    Started = info.Started,
                    RemotePortMin = info.RemotePortMin,
                    RemotePortMax = info.RemotePortMax,
                    NodeId = info.NodeId
                }, c => c.Id == info.Id);
            }
            else
            {

                liteCollection.Insert(info);
                if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                    LoggerHelper.Instance.Info($"add sforward {info.ToJson()}");
            }
            return true;
        }
        public bool Update(long id, bool started, string msg)
        {
            return liteCollection.UpdateMany(c => new SForwardInfo191 { Started = started, Msg = msg }, c => c.Id == id) > 0;
        }
        public bool Update(long id, bool started)
        {
            return liteCollection.UpdateMany(c => new SForwardInfo191 { Started = started }, c => c.Id == id) > 0;
        }
        public bool Update(long id, string localMsg)
        {
            return liteCollection.UpdateMany(c => new SForwardInfo191 { LocalMsg = localMsg }, c => c.Id == id) > 0;
        }

        public bool UpdateNodeId1(long id, string nodeid1)
        {
            return liteCollection.UpdateMany(c => new SForwardInfo191 { NodeId1 = nodeid1 }, c => c.Id == id) > 0;
        }

        public bool Remove(long id)
        {
            return liteCollection.Delete(id);
        }

        private bool PortRange(string str, out int min, out int max)
        {
            min = 0; max = 0;

            if (string.IsNullOrWhiteSpace(str)) return false;

            string[] arr = str.Split('/');
            return arr.Length == 2 && int.TryParse(arr[0], out min) && int.TryParse(arr[1], out max);
        }

    }
}
