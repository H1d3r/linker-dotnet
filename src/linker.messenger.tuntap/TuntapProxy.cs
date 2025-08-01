﻿using linker.tunnel;
using linker.tunnel.connection;
using linker.libs;
using linker.tun;
using System.Buffers.Binary;
using linker.messenger.relay.client;
using linker.messenger.signin;
using linker.messenger.pcp;
using linker.messenger.tuntap.cidr;

namespace linker.messenger.tuntap
{
    public interface ITuntapProxyCallback
    {
        public ValueTask Close(ITunnelConnection connection);
        public void Receive(ITunnelConnection connection, ReadOnlyMemory<byte> packet);
    }

    public class TuntapProxy : channel.Channel, ITunnelConnectionReceiveCallback
    {
        public ITuntapProxyCallback Callback { get; set; }
        protected override string TransactionId => "tuntap";

        private readonly OperatingMultipleManager operatingMultipleManager = new OperatingMultipleManager();
        private readonly TuntapConfigTransfer tuntapConfigTransfer;
        private readonly TuntapCidrConnectionManager tuntapCidrConnectionManager;
        private readonly TuntapCidrDecenterManager tuntapCidrDecenterManager;
        private readonly TuntapCidrMapfileManager tuntapCidrMapfileManager;
        public TuntapProxy(ISignInClientStore signInClientStore,
            TunnelTransfer tunnelTransfer, RelayClientTransfer relayTransfer, PcpTransfer pcpTransfer,
            SignInClientTransfer signInClientTransfer, IRelayClientStore relayClientStore, TuntapConfigTransfer tuntapConfigTransfer, TuntapCidrConnectionManager tuntapCidrConnectionManager, TuntapCidrDecenterManager tuntapCidrDecenterManager, TuntapCidrMapfileManager tuntapCidrMapfileManager)
            : base(tunnelTransfer, relayTransfer, pcpTransfer, signInClientTransfer, signInClientStore, relayClientStore)
        {
            this.tuntapConfigTransfer = tuntapConfigTransfer;
            this.tuntapCidrConnectionManager = tuntapCidrConnectionManager;
            this.tuntapCidrDecenterManager = tuntapCidrDecenterManager;
            this.tuntapCidrMapfileManager = tuntapCidrMapfileManager;
        }

        protected override void Connected(ITunnelConnection connection)
        {
            Add(connection);
            connection.BeginReceive(this, null);
            if (tuntapConfigTransfer.Info.TcpMerge)
                connection.StartPacketMerge();
            //有哪些目标IP用了相同目标隧道，更新一下
            tuntapCidrConnectionManager.Update(connection);
        }

        /// <summary>
        /// 收到隧道数据，写入网卡
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="buffer"></param>
        /// <param name="state"></param>
        /// <returns></returns>
#pragma warning disable CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
        public async Task Receive(ITunnelConnection connection, ReadOnlyMemory<byte> buffer, object state)
#pragma warning restore CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
        {
            //LoggerHelper.Instance.Warning($"tuntap write {buffer.Length}");
            Callback.Receive(connection, buffer);
        }
        /// <summary>
        /// 隧道关闭
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="state"></param>
        /// <returns></returns>
        public async Task Closed(ITunnelConnection connection, object state)
        {
            await Callback.Close(connection).ConfigureAwait(false);
            Version.Increment();
        }

        /// <summary>
        /// 收到网卡数据，发送给对方
        /// </summary>
        /// <param name="packet"></param>
        /// <returns></returns>
        public async Task InputPacket(LinkerTunDevicPacket packet)
        {
            //IPV4广播组播、IPV6 多播
            if (packet.IPV4Broadcast || packet.IPV6Multicast)
            {
                if (tuntapConfigTransfer.Info.Switch.HasFlag(TuntapSwitch.Multicast) == false && connections.IsEmpty == false)
                {
                    await Task.WhenAll(connections.Values.Where(c => c != null && c.Connected).Select(c => c.SendAsync(packet.Buffer, packet.Offset, packet.Length)));
                }
                return;
            }

            //IPV4+IPV6 单播
            uint ip = BinaryPrimitives.ReadUInt32BigEndian(packet.DistIPAddress.Span[^4..]);
            if (tuntapCidrConnectionManager.TryGet(ip, out ITunnelConnection connection) == false || connection == null || connection.Connected == false)
            {
                //开始操作，开始失败直接丢包
                if (operatingMultipleManager.StartOperation(ip) == false)
                {
                    return;
                }

                _ = ConnectTunnel(ip).ContinueWith((result, state) =>
                {
                    //结束操作
                    operatingMultipleManager.StopOperation((uint)state);
                    //连接成功就缓存隧道
                    if (result.Result != null)
                    {
                        tuntapCidrConnectionManager.Add((uint)state, result.Result);
                    }
                }, ip);
                return;
            }
            await connection.SendAsync(packet.Buffer, packet.Offset, packet.Length).ConfigureAwait(false);
        }

        /// <summary>
        /// 打洞或者中继
        /// </summary>
        /// <param name="ip"></param>
        /// <returns></returns>
        private async Task<ITunnelConnection> ConnectTunnel(uint ip)
        {
            if (tuntapCidrDecenterManager.FindValue(ip, out string machineId))
            {
                return await ConnectTunnel(machineId, TunnelProtocolType.Quic).ConfigureAwait(false);
            }
            if (tuntapCidrMapfileManager.FindValue(ip, out machineId))
            {
                return await ConnectTunnel(machineId, TunnelProtocolType.Quic).ConfigureAwait(false);
            }
            return null;

        }
    }
}
