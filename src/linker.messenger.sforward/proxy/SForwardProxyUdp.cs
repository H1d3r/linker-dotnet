﻿using linker.libs;
using linker.libs.extends;
using linker.libs.timer;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace linker.plugins.sforward.proxy
{
    public partial class SForwardProxy
    {
        //服务器监听表
        private ConcurrentDictionary<int, AsyncUserUdpToken> udpListens = new ConcurrentDictionary<int, AsyncUserUdpToken>();
        //服务器映射表，服务器收到一个地址的数据包，要知道发给谁
        private ConcurrentDictionary<(uint ip, ushort port), UdpTargetCache> udpConnections = new ConcurrentDictionary<(uint ip, ushort port), UdpTargetCache>();
        //连接缓存表，a连接，保存，b连接查询，找到对应的，就成立映射关系，完成隧道
        private ConcurrentDictionary<ulong, TaskCompletionSource<IPEndPoint>> udptcss = new ConcurrentDictionary<ulong, TaskCompletionSource<IPEndPoint>>();

        //本地服务表，其实不必要，只是缓存一下去定时检测过期没有
        private ConcurrentDictionary<ulong, UdpConnectedCache> udpConnecteds = new ConcurrentDictionary<ulong, UdpConnectedCache>();

        public Func<int, ulong, Task<bool>> UdpConnect { get; set; } = async (port, id) => { return await Task.FromResult(false).ConfigureAwait(false); };

        #region 服务端

        private void StartUdp(int port, byte bufferSize, string groupid)
        {
            Socket socketUdp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socketUdp.Bind(new IPEndPoint(IPAddress.Any, port));
            AsyncUserUdpToken asyncUserUdpToken = new AsyncUserUdpToken
            {
                ListenPort = port,
                SourceSocket = socketUdp,
                GroupId = groupid,
            };
            socketUdp.EnableBroadcast = true;
            socketUdp.WindowsUdpBug();

            _ = BindReceive(asyncUserUdpToken, bufferSize);

            udpListens.AddOrUpdate(port, asyncUserUdpToken, (a, b) => asyncUserUdpToken);
        }
        private async Task BindReceive(AsyncUserUdpToken token, byte bufferSize)
        {
            try
            {
                byte[] buffer = new byte[65 * 1024];
                IPEndPoint tempRemoteEP = new IPEndPoint(IPAddress.Any, IPEndPoint.MinPort);

                string portStr = token.ListenPort.ToString();

                while (true)
                {
                    SocketReceiveFromResult result = await token.SourceSocket.ReceiveFromAsync(buffer, tempRemoteEP).ConfigureAwait(false);
                    if (result.ReceivedBytes == 0)
                    {
                        break;
                    }

                    Memory<byte> memory = buffer.AsMemory(0, result.ReceivedBytes);

                    AddReceive(portStr, token.GroupId, memory.Length);

                    IPEndPoint source = result.RemoteEndPoint as IPEndPoint;
                    (uint ip, ushort port) sourceKey = (NetworkHelper.ToValue(source.Address), (ushort)source.Port);

                    //已经连接
                    if (udpConnections.TryGetValue(sourceKey, out UdpTargetCache cache) && cache != null)
                    {
                        AddSendt(portStr, token.GroupId, memory.Length);
                        cache.LastTicks.Update();
                        await token.SourceSocket.SendToAsync(memory, cache.IPEndPoint).ConfigureAwait(false);
                    }
                    else
                    {
                        //连接的回复
                        if (memory.Length > flagBytes.Length && memory.Slice(0, flagBytes.Length).Span.SequenceEqual(flagBytes))
                        {
                            ulong _id = memory.Slice(flagBytes.Length).ToUInt64();
                            if (udptcss.TryRemove(_id, out TaskCompletionSource<IPEndPoint> _tcs))
                            {
                                _tcs.TrySetResult(source);
                            }
                            continue;
                        }

                        if (udpConnections.TryGetValue(sourceKey, out _))
                        {
                            continue;
                        }
                        udpConnections.TryAdd(sourceKey, null);

                        int length = memory.Length;
                        byte[] buf = ArrayPool<byte>.Shared.Rent(length);
                        memory.CopyTo(buf);

                        TimerHelper.Async(async () =>
                        {
                            ulong id = ns.Increment();
                            try
                            {
                                if (await UdpConnect(token.ListenPort, id).ConfigureAwait(false))
                                {
                                    TaskCompletionSource<IPEndPoint> tcs = new TaskCompletionSource<IPEndPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
                                    udptcss.TryAdd(id, tcs);

                                    try
                                    {
                                        IPEndPoint remote = await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(5000)).ConfigureAwait(false);
                                        (uint ip, ushort port) remoteKey = (NetworkHelper.ToValue(remote.Address), (ushort)remote.Port);

                                        UdpTargetCache cacheSource = new UdpTargetCache { IPEndPoint = remote };
                                        UdpTargetCache cacheRemote = new UdpTargetCache { IPEndPoint = source };
                                        udpConnections.AddOrUpdate(sourceKey, cacheSource, (a, b) => cacheSource);
                                        udpConnections.AddOrUpdate(remoteKey, cacheRemote, (a, b) => cacheRemote);

                                        await token.SourceSocket.SendToAsync(buf.AsMemory(0, length), remote).ConfigureAwait(false);
                                    }
                                    catch (Exception)
                                    {
                                        tcs.TrySetResult(null);
                                    }
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
                                udptcss.TryRemove(id, out _);
                                ArrayPool<byte>.Shared.Return(buf);
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                {
                    LoggerHelper.Instance.Error(ex);
                }
                if (udpListens.TryRemove(token.ListenPort, out token))
                {
                    token.Clear();
                }
            }
        }
        public void StopUdp()
        {
            foreach (var item in udpListens)
            {
                item.Value.Clear();
            }
            udpListens.Clear();
        }
        public virtual void StopUdp(int port)
        {
            if (udpListens.TryRemove(port, out AsyncUserUdpToken udpClient))
            {
                udpClient.Clear();
            }
        }

        #endregion

        /// <summary>
        /// 客户端，收到服务端的udp请求
        /// </summary>
        /// <param name="bufferSize"></param>
        /// <param name="id"></param>
        /// <param name="server"></param>
        /// <param name="service"></param>
        /// <returns></returns>
        public async Task OnConnectUdp(byte bufferSize, ulong id, IPEndPoint server, IPEndPoint service)
        {
            string portStr = service.Port.ToString();
            //连接服务器
            Socket serverUdp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            serverUdp.WindowsUdpBug();

            byte[] buffer = new byte[flagBytes.Length + 8];
            flagBytes.AsMemory().CopyTo(buffer);
            id.ToBytes(buffer.AsMemory(flagBytes.Length));
            await serverUdp.SendToAsync(buffer, server).ConfigureAwait(false);

            //连接本地服务
            buffer = new byte[65 * 1024];
            IPEndPoint tempEp = new IPEndPoint(IPAddress.Any, IPEndPoint.MinPort);

            UdpConnectedCache cache = new UdpConnectedCache { SourceSocket = serverUdp, TargetSocket = null };
            while (true)
            {
                try
                {
                    //从服务端收数据
                    SocketReceiveFromResult result = await serverUdp.ReceiveFromAsync(buffer, tempEp).ConfigureAwait(false);
                    if (result.ReceivedBytes == 0)
                    {
                        cache.Clear();
                        break;
                    }
                    cache.LastTicks.Update();

                    Memory<byte> memory = buffer.AsMemory(0, result.ReceivedBytes);
                    AddReceive(portStr, string.Empty, memory.Length);
                    AddSendt(portStr, string.Empty, memory.Length);
                    //未连接本地服务的，去连接一下
                    if (cache.TargetSocket == null)
                    {
                        cache.TargetSocket = await NewServiceConnect(memory).ConfigureAwait(false);
                        udpConnecteds.TryAdd(id, cache);
                    }
                    else
                    {
                        await cache.TargetSocket.SendToAsync(memory, service).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG) LoggerHelper.Instance.Error(ex);

                    cache.Clear();
                    break;
                }
            }

            async Task<Socket> NewServiceConnect(Memory<byte> memory)
            {
                Socket serviceUdp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                serviceUdp.WindowsUdpBug();
                await serviceUdp.SendToAsync(memory, service).ConfigureAwait(false);
                TimerHelper.Async(async () =>
                {
                    byte[] buffer = new byte[65 * 1024];
                    IPEndPoint tempEp = new IPEndPoint(IPAddress.Any, IPEndPoint.MinPort);
                    while (true)
                    {
                        try
                        {
                            SocketReceiveFromResult result = await serviceUdp.ReceiveFromAsync(buffer, tempEp).ConfigureAwait(false);
                            if (result.ReceivedBytes == 0)
                            {
                                cache.Clear();
                                break;
                            }
                            Memory<byte> memory = buffer.AsMemory(0, result.ReceivedBytes);
                            AddReceive(portStr, string.Empty, memory.Length);
                            AddSendt(portStr, string.Empty, memory.Length);

                            await serverUdp.SendToAsync(memory, server).ConfigureAwait(false);
                            cache.LastTicks.Update();
                        }
                        catch (Exception ex)
                        {
                            if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG) LoggerHelper.Instance.Error(ex);

                            cache.Clear();
                            break;
                        }
                    }
                });
                return serviceUdp;
            }
        }


        private void UdpTask()
        {
            TimerHelper.SetIntervalLong(() =>
            {
                var connections = udpConnections.Where(c => c.Value.Timeout).Select(c => c.Key);
                foreach (var item in connections)
                {
                    udpConnections.TryRemove(item, out _);
                }

                var connecteds = udpConnecteds.Where(c => c.Value.Timeout).Select(c => c.Key);
                foreach (var item in connecteds)
                {
                    if (udpConnecteds.TryRemove(item, out UdpConnectedCache cache))
                    {
                        cache.Clear();
                    }
                }
            }, 60000);
        }
    }

    public sealed class UdpTargetCache
    {
        public IPEndPoint IPEndPoint { get; set; }
        public LastTicksManager LastTicks { get; set; } = new LastTicksManager();
        public bool Timeout => LastTicks.DiffGreater(5 * 60 * 1000);
    }

    public sealed class UdpConnectedCache
    {
        public Socket SourceSocket { get; set; }
        public Socket TargetSocket { get; set; }
        public LastTicksManager LastTicks { get; set; } = new LastTicksManager();
        public bool Timeout => LastTicks.DiffGreater(5 * 60 * 1000);

        public void Clear()
        {
            SourceSocket?.SafeClose();
            SourceSocket = null;

            TargetSocket?.SafeClose();
            TargetSocket = null;

            GC.Collect();
        }
    }

    public sealed class AsyncUserUdpToken
    {
        public int ListenPort { get; set; }
        public string GroupId { get; set; }
        public Socket SourceSocket { get; set; }
        public Socket TargetSocket { get; set; }

        public void Clear()
        {
            SourceSocket?.SafeClose();
            SourceSocket = null;

            TargetSocket?.SafeClose();
            TargetSocket = null;

            GC.Collect();
        }
    }

}
