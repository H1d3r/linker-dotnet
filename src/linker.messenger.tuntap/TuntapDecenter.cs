﻿using linker.libs;
using linker.messenger.decenter;
using System.Collections.Concurrent;
using linker.messenger.signin;
using System.Net;
using linker.libs.timer;

namespace linker.messenger.tuntap
{
    public sealed class TuntapDecenter : IDecenter
    {
        public string Name => "tuntap";
        public VersionManager PushVersion { get; } = new VersionManager();
        public VersionManager DataVersion { get; } = new VersionManager();

        private readonly ConcurrentDictionary<string, TuntapInfo> tuntapInfos = new ConcurrentDictionary<string, TuntapInfo>();
        public ConcurrentDictionary<string, TuntapInfo> Infos => tuntapInfos;

        public Action OnClear { get; set; } = () => { };
        public Action OnChanged { get; set; } = () => { };

        private readonly VersionManager listVersion = new VersionManager();

        private readonly ISignInClientStore signInClientStore;
        private readonly ISerializer serializer;
        private readonly TuntapConfigTransfer tuntapConfigTransfer;
        private readonly TuntapTransfer tuntapTransfer;
        private readonly SignInClientState signInClientState;
        private readonly ITuntapSystemInformation systemInformation;
        private readonly SignInClientTransfer signInClientTransfer;

        public TuntapDecenter(ISignInClientStore signInClientStore, SignInClientState signInClientState, ISerializer serializer,
            TuntapConfigTransfer tuntapConfigTransfer, TuntapTransfer tuntapTransfer, ITuntapSystemInformation systemInformation,
            SignInClientTransfer signInClientTransfer)
        {
            this.signInClientStore = signInClientStore;
            this.serializer = serializer;
            this.tuntapConfigTransfer = tuntapConfigTransfer;
            this.tuntapTransfer = tuntapTransfer;
            this.signInClientState = signInClientState;
            this.systemInformation = systemInformation;
            this.signInClientTransfer = signInClientTransfer;

            CheckAvailableTask();
        }

        public void Refresh()
        {
            PushVersion.Increment();
        }

        public Memory<byte> GetData()
        {
            if (tuntapTransfer.AppNat)
            {
                tuntapConfigTransfer.Info.Switch |= TuntapSwitch.AppNat;
            }
            else
            {
                tuntapConfigTransfer.Info.Switch &= ~TuntapSwitch.AppNat;
            }

            return serializer.Serialize(new TuntapInfo
            {
                IP = tuntapConfigTransfer.Info.IP,
                Lans = tuntapConfigTransfer.Info.Lans.Where(c => c.IP != null && c.IP.Equals(IPAddress.Any) == false)
                .Select(c => { c.Exists = false; return c; }).ToList(),
                Wan = signInClientState.WanAddress.Address,
                PrefixLength = tuntapConfigTransfer.Info.PrefixLength,
                Name = tuntapConfigTransfer.Info.Name,
                MachineId = signInClientStore.Id,
                Status = tuntapTransfer.Status,
                SetupError = tuntapTransfer.SetupError,
                NatError = tuntapTransfer.NatError,
                SystemInfo = systemInformation.Get(),
                Forwards = tuntapConfigTransfer.Info.Forwards,
                Switch = tuntapConfigTransfer.Info.Switch
            });
        }
        public void AddData(Memory<byte> data)
        {
            TuntapInfo info = serializer.Deserialize<TuntapInfo>(data.Span);
            info.Available = true;
            if (tuntapInfos.TryGetValue(info.MachineId, out var old))
            {
                info.Delay = old.Delay;
            }
            tuntapInfos.AddOrUpdate(info.MachineId, info, (a, b) => info);
            listVersion.Increment();
        }
        public void AddData(List<ReadOnlyMemory<byte>> data)
        {
            List<TuntapInfo> list = data.Select(c => serializer.Deserialize<TuntapInfo>(c.Span)).ToList();
            foreach (var item in list)
            {
                item.Available = true;
                if (tuntapInfos.TryGetValue(item.MachineId, out var old))
                {
                    item.Delay = old.Delay;
                }
                tuntapInfos.AddOrUpdate(item.MachineId, item, (a, b) => item);
            }
            DataVersion.Increment();
            listVersion.Increment();
        }
        public void ClearData()
        {
            tuntapInfos.Clear();
            OnClear();
        }
        public void ProcData()
        {
            OnChanged();
        }

        public bool HasSwitchFlag(string machineId, TuntapSwitch tuntapSwitch)
        {
            return tuntapInfos.TryGetValue(machineId, out var info) && (info.Switch & tuntapSwitch) == tuntapSwitch;
        }

        private void CheckAvailableTask()
        {
            ulong version = listVersion.Value;
            TimerHelper.SetIntervalLong(async () =>
            {
                if (listVersion.Eq(version, out ulong _version) == false)
                {
                    IEnumerable<string> availables = tuntapInfos.Values.Where(c => c.Available).Select(c => c.MachineId);
                    if (availables.Any())
                    {
                        List<string> offlines = await signInClientTransfer.GetOfflines(availables.ToList()).ConfigureAwait(false);
                        if (offlines.Any())
                        {
                            foreach (var item in tuntapInfos.Values.Where(c => offlines.Contains(c.MachineId)))
                            {
                                item.Available = false;
                            }
                            ProcData();
                        }
                    }
                }
                version = _version;
            }, 3000);
        }

    }
}
