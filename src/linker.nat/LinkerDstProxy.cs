﻿using linker.libs;
using linker.libs.extends;
using linker.libs.timer;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace linker.nat
{
    /// <summary>
    /// 目标代理，实现类似DNAT的功能
    /// </summary>
    public sealed class LinkerDstProxy
    {
        public bool Running => listenSocketTcp != null;

        private Socket listenSocketTcp;
        private Socket listenSocketUdp;

        ushort proxyPort = 0;
        uint tunIp = 0;

        private ValueTuple<uint, uint>[] lans = [];
        private readonly ConcurrentDictionary<(uint srcIp, ushort srcPort), DstCacheInfo> dic = new();
        private readonly ConcurrentDictionary<(uint srcIp, ushort srcPort, uint dstIp, ushort dstPort), UdpState> udpMap = new();
        private readonly ConcurrentDictionary<uint, IcmpState> icmpMap = new();
        public LinkerDstProxy()
        {
            Clear();
        }

        public bool Setup(IPAddress dstAddr, ValueTuple<IPAddress, byte>[] dsts, ref string error)
        {
            Shutdown();
            try
            {
                if (dsts == null || dsts.Length == 0)
                {
                    lans = [];
                    dic.Clear();
                    return false;
                }

                lans = dsts.Select(c => new ValueTuple<uint, uint>(NetworkHelper.ToNetworkValue(c.Item1, c.Item2), NetworkHelper.ToBroadcastValue(c.Item1, c.Item2))).ToArray();

                listenSocketTcp = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                listenSocketTcp.Bind(new IPEndPoint(IPAddress.Any, 0));
                listenSocketTcp.Listen(int.MaxValue);
                _ = ReceiveTcp();

                listenSocketUdp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                listenSocketUdp.Bind(new IPEndPoint(IPAddress.Any, (listenSocketTcp.LocalEndPoint as IPEndPoint).Port));
                _ = ReceiveUdp();

                tunIp = NetworkHelper.ToValue(dstAddr);

                proxyPort = (ushort)(listenSocketTcp.LocalEndPoint as IPEndPoint).Port;

                error = string.Empty;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            return true;
        }
        private async Task ReceiveTcp()
        {
            int hashcode = listenSocketTcp.GetHashCode();
            try
            {
                while (true)
                {
                    Socket source = await listenSocketTcp.AcceptAsync();
                    IPEndPoint ep = source.RemoteEndPoint as IPEndPoint;
                    (uint srcIp, ushort srcPort) key = (NetworkHelper.ToValue(ep.Address), (ushort)ep.Port);
                    if (dic.TryGetValue(key, out DstCacheInfo cache) == false)
                    {
                        source.SafeClose();
                        continue;
                    }
                    Socket dst = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    dst.BeginConnect(new IPEndPoint(NetworkHelper.ToIP(cache.IP), cache.Port), ConnectCallback, new TcpState { Source = source, Target = dst });
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.Instance.Error(ex);
                if (listenSocketTcp != null && listenSocketTcp.GetHashCode() == hashcode)
                    Shutdown();
            }
        }
        private async void ConnectCallback(IAsyncResult result)
        {
            TcpState state = result.AsyncState as TcpState;
            try
            {
                state.Target.EndConnect(result);
                state.Target.KeepAlive();
                state.Source.KeepAlive();

                using IMemoryOwner<byte> buffer1 = MemoryPool<byte>.Shared.Rent(8192);
                using IMemoryOwner<byte> buffer2 = MemoryPool<byte>.Shared.Rent(8192);

                await Task.WhenAll(
                    CopyToAsync(buffer1.Memory, state.Source, state.Target),
                    CopyToAsync(buffer2.Memory, state.Target, state.Source)
                    ).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LoggerHelper.Instance.Error(ex);
            }
            finally
            {
                state.Source.SafeClose();
                state.Target.SafeClose();
            }
        }
        private static async Task CopyToAsync(Memory<byte> buffer, Socket source, Socket target)
        {
            try
            {
                int bytesRead;
                while ((bytesRead = await source.ReceiveAsync(buffer, SocketFlags.None).ConfigureAwait(false)) != 0)
                {
                    await target.SendAsync(buffer.Slice(0, bytesRead), SocketFlags.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                {
                    LoggerHelper.Instance.Error(ex);
                }
            }
            finally
            {
                source.SafeClose();
                target.SafeClose();
            }
        }

        private async Task ReceiveUdp()
        {
            int hashcode = listenSocketUdp.GetHashCode();
            try
            {
                using IMemoryOwner<byte> memory = MemoryPool<byte>.Shared.Rent(65535);
                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                while (true)
                {
                    SocketReceiveFromResult result = await listenSocketUdp.ReceiveFromAsync(memory.Memory, remoteEndPoint);
                    if (result.ReceivedBytes == 0) continue;

                    IPEndPoint ep = result.RemoteEndPoint as IPEndPoint;
                    (uint srcIp, ushort srcPort) key = (NetworkHelper.ToValue(ep.Address), (ushort)ep.Port);
                    if (dic.TryGetValue(key, out DstCacheInfo cache) == false) continue;

                    (uint srcIp, ushort srcPort, uint dstIp, ushort dstPort) keyUdp = (NetworkHelper.ToValue(ep.Address), (ushort)ep.Port, cache.IP, cache.Port);
                    if (udpMap.TryGetValue(keyUdp, out UdpState state) == false)
                    {
                        state = new UdpState
                        {
                            Source = listenSocketUdp,
                            SourceEP = new IPEndPoint(ep.Address, ep.Port),
                            Target = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp),
                            TargetEP = new IPEndPoint(NetworkHelper.ToIP(cache.IP), cache.Port)
                        };
                        udpMap.AddOrUpdate(keyUdp, state, (a, b) => state);

                        await state.Target.SendToAsync(memory.Memory.Slice(0, result.ReceivedBytes), state.TargetEP);
                        ConnectCallback(keyUdp, state);
                    }
                    else
                    {
                        await state.Target.SendToAsync(memory.Memory.Slice(0, result.ReceivedBytes), state.TargetEP);
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.Instance.Error(ex);
                if (listenSocketUdp != null && listenSocketUdp.GetHashCode() == hashcode)
                    Shutdown();
            }
        }
        private async void ConnectCallback((uint srcIp, ushort srcPort, uint dstIp, ushort dstPort) keyUdp, UdpState state)
        {
            try
            {
                using IMemoryOwner<byte> memory = MemoryPool<byte>.Shared.Rent(65535);
                while (true)
                {
                    SocketReceiveFromResult result = await state.Target.ReceiveFromAsync(memory.Memory, state.TargetEP);
                    if (result.ReceivedBytes == 0) continue;

                    await state.Source.SendToAsync(memory.Memory.Slice(0, result.ReceivedBytes), state.SourceEP);
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.Instance.Error(ex);
            }
            finally
            {
                state.Target.SafeClose();
                udpMap.TryRemove(keyUdp, out _);
            }
        }

        public void Shutdown()
        {
            listenSocketTcp?.SafeClose();
            listenSocketTcp = null;
            listenSocketUdp?.SafeClose();
            listenSocketUdp = null;
        }

        /// <summary>
        /// 从网卡读取之后
        /// </summary>
        /// <param name="packet"></param>
        /// <param name="needChecksum"></param>
        public unsafe void Read(ReadOnlyMemory<byte> packet)
        {
            if (Running == false) return;
            fixed (byte* ptr = packet.Span)
            {
                DstProxyPacket p = new DstProxyPacket(ptr);
                //if (p.Version != 4) return;

                if (p.Protocol == ProtocolType.Tcp || p.Protocol == ProtocolType.Udp)
                {
                    if (dic.TryGetValue((p.DstAddr, p.DstPort), out DstCacheInfo cache))
                    {
                        cache.LastTime = Environment.TickCount64;
                        p.SrcAddr = cache.IP;
                        p.SrcPort = cache.Port;
                        p.IPChecksum = 0;
                        p.PayloadChecksum = 0;

                        if (p.Protocol == ProtocolType.Tcp && (p.TcpFlagFin || p.TcpFlagRst))
                        {
                            cache.Fin = true;
                        }
                    }
                }
            }

        }
        /// <summary>
        /// 写如网卡之前
        /// </summary>
        /// <param name="packet">单个完整TCP/IP包</param>
        /// <returns></returns>
        public unsafe bool Write(ReadOnlyMemory<byte> packet)
        {
            if (Running == false) return true;
            fixed (byte* ptr = packet.Span)
            {
                DstProxyPacket p = new DstProxyPacket(ptr);
                if (p.Version != 4 || p.DstAddr == tunIp || p.DstAddrSpan.IsCast()) return true;

                if (lans.Any(c => p.DstAddr >= c.Item1 && p.DstAddr <= c.Item2) == false)
                {
                    return true;
                }
                return p.Protocol switch
                {
                    ProtocolType.Tcp => WriteTcp(p),
                    ProtocolType.Udp => WriteUdp(p),
                    ProtocolType.Icmp => WriteIcmp(p),
                    _ => true,
                };
            }
        }
        private bool WriteTcp(DstProxyPacket p)
        {
            (uint srcIp, ushort srcPort) key = (p.SrcAddr, p.SrcPort);
            if (dic.TryGetValue(key, out DstCacheInfo cache) == false || cache.IP != p.DstAddr || cache.Port != p.DstPort)
            {
                //仅SYN包建立映射
                if (p.IsOnlySyn == false)
                {
                    return true;
                }
                cache = new DstCacheInfo { IP = p.DstAddr, Port = p.DstPort };
                dic.AddOrUpdate(key, cache, (a, b) => cache);
            }
            //更新最后使用时间
            cache.LastTime = Environment.TickCount64;

            //FIN或RST包，标记为结束
            if (p.TcpFlagFin || p.TcpFlagRst)
            {
                cache.Fin = true;
            }

            //改为代理地址
            p.DstPort = proxyPort;
            p.DstAddr = tunIp;
            //重新计算校验和
            p.IPChecksum = 0;
            p.PayloadChecksum = 0;

            return true;
        }
        private bool WriteUdp(DstProxyPacket p)
        {
            (uint srcIp, ushort srcPort) key = (p.SrcAddr, p.SrcPort);
            if (dic.TryGetValue(key, out DstCacheInfo cache) == false || cache.IP != p.DstAddr || cache.Port != p.DstPort)
            {
                cache = new DstCacheInfo { IP = p.DstAddr, Port = p.DstPort, Fin = true };
                dic.AddOrUpdate(key, cache, (a, b) => cache);
            }
            cache.LastTime = Environment.TickCount64;

            //改为代理地址
            p.DstPort = proxyPort;
            p.DstAddr = tunIp;
            //重新计算校验和
            p.IPChecksum = 0;
            p.PayloadChecksum = 0;

            return true;
        }
        private bool WriteIcmp(DstProxyPacket p)
        {
            if (p.IcmpType == 8)
            {
                if (icmpMap.TryGetValue(p.DstAddr, out IcmpState state) == false)
                {
                    state = new IcmpState();
                    icmpMap.AddOrUpdate(p.DstAddr, state, (a, b) => state);
                    Ping(NetworkHelper.ToIP(p.DstAddr), state);
                }
                if (state.Times > 5 || Environment.TickCount64 - state.LastTime > 15 * 1000)
                {
                    _ = PingAsync(NetworkHelper.ToIP(p.DstAddr), state);
                }

                state.Times++;
                state.LastTime = Environment.TickCount64;
                if (state.Status != IPStatus.Success)
                {
                    return false;
                }
                //交换IP
                (p.DstAddr, p.SrcAddr) = (p.SrcAddr, p.DstAddr);
                //改为response
                p.IcmpType = 0;
                //重新计算校验和
                p.IPChecksum = 0;
                p.PayloadChecksum = 0;
            }
            return true;
        }
        private static void Ping(IPAddress ip, IcmpState state)
        {
            state.Times = 0;
            using Ping ping = new Ping();
            PingReply reply = ping.Send(ip, 1000);
            state.Status = reply.Status;
        }
        private static async Task PingAsync(IPAddress ip, IcmpState state)
        {
            state.Times = 0;
            using Ping ping = new Ping();
            PingReply reply = await ping.SendPingAsync(ip, 1000);
            state.Status = reply.Status;
        }

        private void Clear()
        {
            TimerHelper.SetIntervalLong(() =>
            {
                foreach (var item in dic.Where(c => c.Value.Fin && Environment.TickCount64 - c.Value.LastTime > 5 * 60 * 1000).Select(c => c.Key).ToList())
                {
                    dic.TryRemove(item, out _);
                }
                foreach (var item in icmpMap.Where(c => Environment.TickCount64 - c.Value.LastTime > 30 * 1000).Select(c => c.Key).ToList())
                {
                    icmpMap.TryRemove(item, out _);
                }
            }, 30000);
        }

        sealed class DstCacheInfo
        {
            public long LastTime { get; set; } = Environment.TickCount64;
            public uint IP { get; set; }
            public ushort Port { get; set; }
            public bool Fin { get; set; }
        }

        sealed class TcpState
        {
            public Socket Source { get; set; }
            public Socket Target { get; set; }
        }
        sealed class UdpState
        {
            public Socket Source { get; set; }
            public IPEndPoint SourceEP { get; set; }
            public Socket Target { get; set; }
            public IPEndPoint TargetEP { get; set; }
        }
        sealed class IcmpState
        {
            public long LastTime { get; set; } = Environment.TickCount64;
            public IPStatus Status { get; set; } = IPStatus.Unknown;
            public int Times { get; set; }

        }
        readonly unsafe struct DstProxyPacket
        {
            private readonly byte* ptr;

            /// <summary>
            /// 协议版本
            /// </summary>
            public readonly byte Version => (byte)((*ptr >> 4) & 0b1111);
            public readonly ProtocolType Protocol => (ProtocolType)(*(ptr + 9));

            public readonly byte IcmpType
            {
                get
                {
                    return *PayloadPtr;
                }
                set
                {
                    *PayloadPtr = value;
                }
            }

            /// <summary>
            /// IP头长度
            /// </summary>
            public readonly int IPHeadLength => (*ptr & 0b1111) * 4;
            /// <summary>
            /// IP包荷载数据指针，也就是TCP/UDP头指针
            /// </summary>
            public readonly byte* PayloadPtr => ptr + IPHeadLength;

            /// <summary>
            /// 源地址
            /// </summary>
            public readonly uint SrcAddr
            {
                get
                {
                    return BinaryPrimitives.ReverseEndianness(*(uint*)(ptr + 12));
                }
                set
                {
                    *(uint*)(ptr + 12) = BinaryPrimitives.ReverseEndianness(value);
                }
            }
            /// <summary>
            /// 源端口
            /// </summary>
            public readonly ushort SrcPort
            {
                get
                {
                    return BinaryPrimitives.ReverseEndianness(*(ushort*)(PayloadPtr));
                }
                set
                {
                    *(ushort*)(PayloadPtr) = BinaryPrimitives.ReverseEndianness(value);
                }
            }
            /// <summary>
            /// 目的地址
            /// </summary>
            public readonly uint DstAddr
            {
                get
                {
                    return BinaryPrimitives.ReverseEndianness(*(uint*)(ptr + 16));
                }
                set
                {
                    *(uint*)(ptr + 16) = BinaryPrimitives.ReverseEndianness(value);
                }
            }
            public ReadOnlySpan<byte> DstAddrSpan => new Span<byte>((ptr + 16), 4);


            public readonly byte TcpFlag => *(ptr + IPHeadLength + 13);
            public readonly bool TcpFlagFin => (TcpFlag & 0b000001) != 0;
            public readonly bool TcpFlagSyn => (TcpFlag & 0b000010) != 0;
            public readonly bool TcpFlagRst => (TcpFlag & 0b000100) != 0;
            public readonly bool TcpFlagPsh => (TcpFlag & 0b001000) != 0;
            public readonly bool TcpFlagAck => (TcpFlag & 0b010000) != 0;
            public readonly bool TcpFlagUrg => (TcpFlag & 0b100000) != 0;

            public readonly bool IsPshAck => TcpFlagPsh && TcpFlagAck;
            public readonly bool IsOnlyAck => TcpFlag == 0b00010000;
            public readonly bool IsOnlySyn => TcpFlag == 0b00000010;
            public readonly bool IsSynAck => TcpFlag == 0b00010010;

            /// <summary>
            /// 目标端口
            /// </summary>
            public readonly ushort DstPort
            {
                get
                {
                    return BinaryPrimitives.ReverseEndianness(*(ushort*)(PayloadPtr + 2));
                }
                set
                {
                    *(ushort*)(PayloadPtr + 2) = BinaryPrimitives.ReverseEndianness(value);
                }
            }

            public readonly ushort IPChecksum
            {
                get
                {
                    return BinaryPrimitives.ReverseEndianness(*(ushort*)(ptr + 10));
                }
                set
                {
                    *(ushort*)(ptr + 10) = BinaryPrimitives.ReverseEndianness(value);
                }
            }
            public readonly ushort PayloadChecksum
            {
                get
                {
                    return Protocol switch
                    {
                        ProtocolType.Icmp => BinaryPrimitives.ReverseEndianness(*(ushort*)(PayloadPtr + 2)),
                        ProtocolType.Tcp => BinaryPrimitives.ReverseEndianness(*(ushort*)(PayloadPtr + 16)),
                        ProtocolType.Udp => BinaryPrimitives.ReverseEndianness(*(ushort*)(PayloadPtr + 6)),
                        _ => (ushort)0,
                    };
                }
                set
                {
                    switch (Protocol)
                    {
                        case ProtocolType.Icmp:
                            *(ushort*)(PayloadPtr + 2) = BinaryPrimitives.ReverseEndianness(value);
                            break;
                        case ProtocolType.Tcp:
                            *(ushort*)(PayloadPtr + 16) = BinaryPrimitives.ReverseEndianness(value);
                            break;
                        case ProtocolType.Udp:
                            *(ushort*)(PayloadPtr + 6) = BinaryPrimitives.ReverseEndianness(value);
                            break;
                    }
                }
            }

            /// <summary>
            /// 加载TCP/IP包，必须是一个完整的TCP/IP包
            /// </summary>
            /// <param name="ptr">一个完整的TCP/IP包</param>
            public DstProxyPacket(byte* ptr)
            {
                this.ptr = ptr;
            }
        }
    }
}

