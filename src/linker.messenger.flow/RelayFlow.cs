﻿using linker.libs;
using linker.libs.extends;
using linker.libs.timer;
using linker.messenger.relay.server;
using System.Text.Json.Serialization;

namespace linker.messenger.flow
{
    public sealed class RelayReportFlow : IFlow
    {
        public long ReceiveBytes { get; private set; }
        public long SendtBytes { get; private set; }
        public string FlowName => "RelayReport";
        public VersionManager Version { get; } = new VersionManager();
        public RelayReportFlow()
        {
        }
        public string GetItems() => string.Empty;
        public void SetItems(string json) { }
        public void SetBytes(long receiveBytes, long sendtBytes) { ReceiveBytes = receiveBytes; SendtBytes = sendtBytes; }
        public void Clear() { ReceiveBytes = 0; SendtBytes = 0; }

        public void AddReceive(long bytes) { ReceiveBytes += bytes; Version.Add(); }
        public void AddSendt(long bytes) { SendtBytes += bytes; Version.Add(); }

    }

    public sealed class RelayReportResolverFlow : RelayServerReportResolver
    {
        private readonly RelayReportFlow relayReportFlow;
        public RelayReportResolverFlow(RelayReportFlow relayReportFlow, RelayServerMasterTransfer relayServerTransfer,IRelayServerMasterStore relayServerMasterStore,IMessengerResolver messengerResolver) : base(relayServerTransfer, relayServerMasterStore, messengerResolver)
        {
            this.relayReportFlow = relayReportFlow;
        }

        public override void AddReceive(long bytes) { relayReportFlow.AddReceive(bytes); }
        public override void AddSendt(long bytes) { relayReportFlow.AddSendt(bytes); }

    }



    public sealed class RelayResolverFlow : RelayServerResolver
    {
        private readonly RelayFlow relayFlow;
        public RelayResolverFlow(RelayFlow relayFlow, RelayServerNodeTransfer relayServerNodeTransfer, ISerializer serializer) : base(relayServerNodeTransfer, serializer)
        {
            this.relayFlow = relayFlow;
        }

        public override void AddReceive(string key, string from, string to, string groupid, long bytes)
        {
            relayFlow.AddReceive(key, from, to, groupid, bytes);
        }
        public override void AddSendt(string key, string from, string to, string groupid, long bytes)
        {
            relayFlow.AddSendt(key, from, to, groupid, bytes);
        }
        public override void AddReceive(string key, long bytes)
        {
            relayFlow.AddReceive(key, bytes);
        }
        public override void AddSendt(string key, long bytes)
        {
            relayFlow.AddSendt(key, bytes);
        }

    }

    public sealed class RelayFlow : IFlow
    {
        public long ReceiveBytes { get; private set; }
        public long SendtBytes { get; private set; }
        public string FlowName => "Relay";
        public VersionManager Version { get; } = new VersionManager();

        private readonly LastTicksManager lastTicksManager = new LastTicksManager();

        private Dictionary<string, RelayFlowItemInfo> flows = new Dictionary<string, RelayFlowItemInfo>();

        public RelayFlow()
        {
            TimerHelper.SetIntervalLong(() =>
            {
                if (lastTicksManager.DiffLessEqual(5000))
                {
                    foreach (var item in flows.Values)
                    {
                        item.DiffReceiveBytes = item.SendtBytes - item.OldSendtBytes;
                        item.DiffSendtBytes = item.ReceiveBytes - item.OldReceiveBytes;

                        item.OldSendtBytes = item.SendtBytes;
                        item.OldReceiveBytes = item.ReceiveBytes;
                    }
                }
            }, () => lastTicksManager.DiffLessEqual(5000) ? 1000 : 30000);
        }

        public void Update()
        {
            lastTicksManager.Update();
        }

        public string GetItems() => flows.ToJson();
        public void SetItems(string json) { flows = json.DeJson<Dictionary<string, RelayFlowItemInfo>>(); }
        public void SetBytes(long receiveBytes, long sendtBytes) { ReceiveBytes = receiveBytes; SendtBytes = sendtBytes; }
        public void Clear() { ReceiveBytes = 0; SendtBytes = 0;flows.Clear(); }

        public void AddReceive(string key, string from, string to, string groupid, long bytes)
        {
            if (flows.TryGetValue(key, out RelayFlowItemInfo messengerFlowItemInfo) == false)
            {
                messengerFlowItemInfo = new RelayFlowItemInfo { FromName = from, ToName = to, GroupId = groupid };
                flows.TryAdd(key, messengerFlowItemInfo);
            }
            ReceiveBytes += bytes;
            messengerFlowItemInfo.ReceiveBytes += bytes;
            Version.Add();
        }
        public void AddSendt(string key, string from, string to, string groupid, long bytes)
        {
            if (flows.TryGetValue(key, out RelayFlowItemInfo messengerFlowItemInfo) == false)
            {
                messengerFlowItemInfo = new RelayFlowItemInfo { FromName = from, ToName = to, GroupId = groupid };
                flows.TryAdd(key, messengerFlowItemInfo);
            }
            SendtBytes += bytes;
            messengerFlowItemInfo.SendtBytes += bytes;
            Version.Add();
        }

        public void AddReceive(string key, long bytes)
        {
            if (flows.TryGetValue(key, out RelayFlowItemInfo messengerFlowItemInfo))
            {
                ReceiveBytes += bytes;
                messengerFlowItemInfo.ReceiveBytes += bytes;
                Version.Add();
            }

        }
        public void AddSendt(string key, long bytes)
        {
            if (flows.TryGetValue(key, out RelayFlowItemInfo messengerFlowItemInfo))
            {
                SendtBytes += bytes;
                messengerFlowItemInfo.SendtBytes += bytes;
                Version.Add();
            }

        }

        public RelayFlowResponseInfo GetFlows(RelayFlowRequestInfo info)
        {
            var items = flows.Values.Where(c => string.IsNullOrWhiteSpace(info.Key) || c.FromName.Contains(info.Key) || c.ToName.Contains(info.Key));
            if (string.IsNullOrWhiteSpace(info.GroupId) == false)
            {
                items = items.Where(c => c.GroupId == info.GroupId);
            }

            switch (info.Order)
            {
                case RelayFlowOrder.Sendt:
                    if (info.OrderType == RelayFlowOrderType.Desc)
                        items = items.OrderByDescending(x => x.SendtBytes);
                    else
                        items = items.OrderBy(x => x.SendtBytes);
                    break;
                case RelayFlowOrder.DiffSendt:
                    if (info.OrderType == RelayFlowOrderType.Desc)
                        items = items.OrderByDescending(x => x.DiffSendtBytes);
                    else
                        items = items.OrderBy(x => x.DiffSendtBytes);
                    break;
                case RelayFlowOrder.Receive:
                    if (info.OrderType == RelayFlowOrderType.Desc)
                        items = items.OrderByDescending(x => x.ReceiveBytes);
                    else
                        items = items.OrderBy(x => x.ReceiveBytes);
                    break;
                case RelayFlowOrder.DiffRecive:
                    if (info.OrderType == RelayFlowOrderType.Desc)
                        items = items.OrderByDescending(x => x.DiffReceiveBytes);
                    else
                        items = items.OrderBy(x => x.DiffReceiveBytes);
                    break;
                default:
                    break;
            }
            RelayFlowResponseInfo resp = new RelayFlowResponseInfo
            {
                Page = info.Page,
                PageSize = info.PageSize,
                Count = flows.Count,
                Data = items.Skip((info.Page - 1) * info.PageSize).Take(info.PageSize).ToList()
            };

            return resp;
        }
    }

    public sealed partial class RelayFlowItemInfo : FlowItemInfo
    {
        public long DiffReceiveBytes { get; set; }
        public long DiffSendtBytes { get; set; }
        public string FromName { get; set; }
        public string ToName { get; set; }

        public string GroupId { get; set; }

        [JsonIgnore]
        public long OldReceiveBytes { get; set; }
        [JsonIgnore]
        public long OldSendtBytes { get; set; }
    }

    public sealed partial class RelayFlowRequestInfo
    {
        public string Key { get; set; } = string.Empty;

        public string GroupId { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 15;
        public RelayFlowOrder Order { get; set; }
        public RelayFlowOrderType OrderType { get; set; }
    }

    public enum RelayFlowOrder : byte
    {
        Sendt = 1,
        DiffSendt = 2,
        Receive = 3,
        DiffRecive = 4
    }
    public enum RelayFlowOrderType : byte
    {
        Desc = 0,
        Asc = 1,
    }

    public sealed partial class RelayFlowResponseInfo
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int Count { get; set; }
        public List<RelayFlowItemInfo> Data { get; set; }
    }
}