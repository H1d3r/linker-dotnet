﻿using linker.libs;
using linker.tunnel.connection;
using System.Net.NetworkInformation;
using System.Net;
using System.Net.Sockets;
using linker.libs.extends;
using linker.messenger.signin;

namespace linker.messenger.tuntap
{
    public sealed class TuntapPingTransfer
    {
        private readonly TuntapTransfer tuntapTransfer;
        private readonly TuntapConfigTransfer tuntapConfigTransfer;
        private readonly TuntapProxy tuntapProxy;
        private readonly ISignInClientStore signInClientStore;
        private readonly TuntapDecenter tuntapDecenter;

        public TuntapPingTransfer(TuntapTransfer tuntapTransfer, TuntapConfigTransfer tuntapConfigTransfer, TuntapProxy tuntapProxy, ISignInClientStore signInClientStore, TuntapDecenter tuntapDecenter)
        {
            this.tuntapTransfer = tuntapTransfer;
            this.tuntapConfigTransfer = tuntapConfigTransfer;
            this.tuntapProxy = tuntapProxy;
            this.signInClientStore = signInClientStore;
            this.tuntapDecenter = tuntapDecenter;

            PingTask();
           
        }

        private readonly LastTicksManager lastTicksManager = new LastTicksManager();
        public void SubscribePing()
        {
            lastTicksManager.Update();
        }
        private void PingTask()
        {
            TimerHelper.SetIntervalLong(async () =>
            {
                if (tuntapTransfer.Status == TuntapStatus.Running)
                {
                    await Ping();
                }
                return true;
            }, () => tuntapTransfer.Status == TuntapStatus.Running && lastTicksManager.DiffLessEqual(5000) ? 3000 : 30000);
        }
        private async Task Ping()
        {
            if (tuntapTransfer.Status == TuntapStatus.Running && (tuntapConfigTransfer.Switch & TuntapSwitch.ShowDelay) == TuntapSwitch.ShowDelay)
            {
                var items = tuntapDecenter.Infos.Values.Where(c => c.IP != null && c.IP.Equals(IPAddress.Any) == false && (c.Status & TuntapStatus.Running) == TuntapStatus.Running);
                if ((tuntapConfigTransfer.Switch & TuntapSwitch.AutoConnect) != TuntapSwitch.AutoConnect)
                {
                    var connections = tuntapProxy.GetConnections();
                    items = items.Where(c => connections.TryGetValue(c.MachineId, out ITunnelConnection connection) && connection.Connected || c.MachineId == signInClientStore.Id);
                }

                await Task.WhenAll(items.Select(async c =>
                {
                    using Ping ping = new Ping();
                    PingReply pingReply = await ping.SendPingAsync(c.IP, 500);
                    c.Delay = pingReply.Status == IPStatus.Success ? (int)pingReply.RoundtripTime : -1;
                    tuntapDecenter.DataVersion.Add();
                }));
            }
        }

        public async Task SubscribeForwardTest(List<TuntapForwardTestInfo> list)
        {
            await Task.WhenAll(list.Where(c=>c.ConnectAddr.Equals(IPAddress.Any)==false && c.ConnectPort > 0 && c.ListenPort > 0).Select(async c =>
            {
                try
                {
                    var socket = new Socket(c.ConnectAddr.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    await socket.ConnectAsync(new IPEndPoint(c.ConnectAddr, c.ConnectPort)).WaitAsync(TimeSpan.FromMilliseconds(500));
                    socket.SafeClose();
                    c.Error = string.Empty;
                }
                catch (Exception ex)
                {
                    c.Error = ex.Message;
                }
            }));
        }
    }
}
