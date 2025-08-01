﻿using linker.libs;
using linker.libs.extends;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace linker.plugins.sforward.proxy
{
    public partial class SForwardProxy
    {
        private ConcurrentDictionary<int, AsyncUserToken> tcpListens = new ConcurrentDictionary<int, AsyncUserToken>();
        private ConcurrentDictionary<ulong, TaskCompletionSource<Socket>> tcpConnections = new ConcurrentDictionary<ulong, TaskCompletionSource<Socket>>();
        private ConcurrentDictionary<ulong, AsyncUserToken> httpConnections = new ConcurrentDictionary<ulong, AsyncUserToken>();

        public Func<int, ulong, Task<bool>> TunnelConnect { get; set; } = async (port, id) => { return await Task.FromResult(false).ConfigureAwait(false); };
        public Func<string, int, ulong, Task<bool>> WebConnect { get; set; } = async (host, port, id) => { return await Task.FromResult(false).ConfigureAwait(false); };

        #region 服务端


        private void StartTcp(int port, bool isweb, byte bufferSize, string groupid)
        {
            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, port);
            Socket socket = new Socket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            //socket.IPv6Only(localEndPoint.AddressFamily, false);
            socket.Bind(localEndPoint);
            socket.Listen(int.MaxValue);
            AsyncUserToken userToken = new AsyncUserToken
            {
                ListenPort = port,
                SourceSocket = socket,
                IsWeb = isweb,
                BufferSize = bufferSize,
                GroupId = groupid
            };
            SocketAsyncEventArgs acceptEventArg = new SocketAsyncEventArgs
            {
                UserToken = userToken,
                SocketFlags = SocketFlags.None,
            };

            acceptEventArg.Completed += IO_Completed;
            StartAccept(acceptEventArg);

            tcpListens.AddOrUpdate(port, userToken, (a, b) => userToken);
        }
        private void StartAccept(SocketAsyncEventArgs acceptEventArg)
        {
            acceptEventArg.AcceptSocket = null;
            AsyncUserToken token = (AsyncUserToken)acceptEventArg.UserToken;
            try
            {
                if (token.SourceSocket.AcceptAsync(acceptEventArg) == false)
                {
                    ProcessAccept(acceptEventArg);
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.Instance.Error(ex);
                token.Clear();
            }
        }
        private void IO_Completed(object sender, SocketAsyncEventArgs e)
        {
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Accept:
                    ProcessAccept(e);
                    break;
                default:
                    break;
            }
        }
        private void ProcessAccept(SocketAsyncEventArgs e)
        {
            try
            {
                if (e.AcceptSocket != null)
                {
                    AsyncUserToken acceptToken = (AsyncUserToken)e.UserToken;
                    Socket socket = e.AcceptSocket;
                    if (socket != null && socket.RemoteEndPoint != null)
                    {
                        socket.KeepAlive();
                        AsyncUserToken userToken = new AsyncUserToken
                        {
                            SourceSocket = socket,
                            ListenPort = acceptToken.ListenPort,
                            IsWeb = acceptToken.IsWeb,
                            BufferSize = acceptToken.BufferSize,
                            GroupId = acceptToken.GroupId,
                        };
                        _ = BindReceive(userToken);
                    }
                    StartAccept(e);
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.Instance.Error(ex);
            }
        }
        private async Task BindReceive(AsyncUserToken token)
        {
            ulong id = ns.Increment();
            byte[] buffer1 = new byte[(1 << token.BufferSize) * 1024];
            byte[] buffer2 = new byte[(1 << token.BufferSize) * 1024];
            try
            {
                int length = await token.SourceSocket.ReceiveAsync(buffer1.AsMemory(), SocketFlags.None).ConfigureAwait(false);
                //是回复连接。传过来了id，去配一下
                if (length > flagBytes.Length && buffer1.AsSpan(0, flagBytes.Length).SequenceEqual(flagBytes))
                {
                    ulong _id = buffer1.AsSpan(flagBytes.Length).ToUInt64();
                    if (tcpConnections.TryRemove(_id, out TaskCompletionSource<Socket> _tcs))
                    {
                        _tcs.TrySetResult(token.SourceSocket);
                    }
                    return;
                }
                string key = token.ListenPort.ToString();
               
                //是web的，去获取host请求头，匹配不同的服务
                if (token.IsWeb)
                {
                    httpConnections.TryAdd(id, token);
                    key = token.Host = GetHost(buffer1.AsMemory(0, length));
                    if (string.IsNullOrWhiteSpace(token.Host))
                    {
                        CloseClientSocket(token);
                        return;
                    }
                    if (await WebConnect(token.Host, token.ListenPort, id).ConfigureAwait(false) == false)
                    {
                        CloseClientSocket(token);
                        return;
                    }
                }
                else
                {
                    //纯TCP的，直接拿端口去匹配
                    if (await TunnelConnect(token.ListenPort, id).ConfigureAwait(false) == false)
                    {
                        CloseClientSocket(token);
                        return;
                    }
                }

                //等待回复
                TaskCompletionSource<Socket> tcs = new TaskCompletionSource<Socket>(TaskCreationOptions.RunContinuationsAsynchronously);
                tcpConnections.TryAdd(id, tcs);
                try
                {
                    token.TargetSocket = await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(5000)).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    CloseClientSocket(token);
                    return;
                }

                Add(key, token.GroupId, length, length);
                //数据
                await token.TargetSocket.SendAsync(buffer1.AsMemory(0, length)).ConfigureAwait(false);

                //两端交换数据
                await Task.WhenAll(CopyToAsync(key, token.GroupId, buffer1, token.SourceSocket, token.TargetSocket), CopyToAsync(key, token.GroupId, buffer2, token.TargetSocket, token.SourceSocket)).ConfigureAwait(false);

                CloseClientSocket(token);
            }
            catch (Exception ex)
            {
                if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                {
                    LoggerHelper.Instance.Error(ex);
                }
                CloseClientSocket(token);
            }
            finally
            {
                tcpConnections.TryRemove(id, out _);
                httpConnections.TryRemove(id, out _);
            }
        }

        private void CloseClientSocket(AsyncUserToken token)
        {
            if (token == null) return;
            token.Clear();
        }
        public void StopTcp()
        {
            foreach (var item in tcpListens)
            {
                CloseClientSocket(item.Value);
            }
            tcpListens.Clear();
        }
        public void StopTcp(int port)
        {
            if (tcpListens.TryRemove(port, out AsyncUserToken userToken))
            {
                CloseClientSocket(userToken);
            }
        }
        public void StopHttp(string host)
        {
            foreach (var item in httpConnections.Where(c=>c.Value.Host == host).Select(c=>c.Key).ToList())
            {
                if (httpConnections.TryRemove(item,out var token))
                {
                    CloseClientSocket(token);
                }
            }
        }



        private readonly byte[] hostBytes = Encoding.UTF8.GetBytes("Host: ");
        private readonly byte[] wrapBytes = Encoding.UTF8.GetBytes("\r\n");
        private readonly byte[] colonBytes = Encoding.UTF8.GetBytes(":");
        private string GetHost(Memory<byte> buffer)
        {
            int start = buffer.Span.IndexOf(hostBytes);
            if (start < 0) return string.Empty;
            start += hostBytes.Length;

            int length = buffer.Span.Slice(start).IndexOf(wrapBytes);

            int length1 = buffer.Span.Slice(start, length).IndexOf(colonBytes);
            if (length1 > 0) length = length1;

            return Encoding.UTF8.GetString(buffer.Slice(start, length).Span);
        }


        #endregion

        /// <summary>
        /// 客户端，收到服务端的连接请求
        /// </summary>
        /// <param name="key"></param>
        /// <param name="bufferSize"></param>
        /// <param name="id"></param>
        /// <param name="server"></param>
        /// <param name="service"></param>
        /// <returns></returns>
        public async Task OnConnectTcp(string key, byte bufferSize, ulong id, IPEndPoint server, IPEndPoint service)
        {
            Socket sourceSocket = null;
            Socket targetSocket = null;
            byte[] buffer1 = new byte[(1 << bufferSize) * 1024];
            byte[] buffer2 = new byte[(1 << bufferSize) * 1024];
            try
            {
                //连接服务器
                sourceSocket = new Socket(server.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                await sourceSocket.ConnectAsync(server).ConfigureAwait(false);

                //连接本地服务
                targetSocket = new Socket(service.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                await targetSocket.ConnectAsync(service).ConfigureAwait(false);

                //给服务器回复，带上id
                flagBytes.AsMemory().CopyTo(buffer1);
                id.ToBytes(buffer1.AsMemory(flagBytes.Length));
                await sourceSocket.SendAsync(buffer1.AsMemory(0, flagBytes.Length + 8)).ConfigureAwait(false);

                //交换数据即可
                await Task.WhenAll(CopyToAsync($"{key}->{service}", string.Empty, buffer1, sourceSocket, targetSocket), CopyToAsync($"{key}->{service}", string.Empty, buffer2, targetSocket, sourceSocket)).ConfigureAwait(false);

            }
            catch (Exception)
            {
                sourceSocket?.SafeClose();
                targetSocket?.SafeClose();
            }
            finally
            {
            }
        }

        /// <summary>
        /// 读取数据，然后发送给对方，用户两端交换数据
        /// </summary>
        /// <param name="key"></param>
        /// <param name="groupid"></param>
        /// <param name="buffer"></param>
        /// <param name="source"></param>
        /// <param name="target"></param>
        /// <returns></returns>
        private async Task CopyToAsync(string key, string groupid, Memory<byte> buffer, Socket source, Socket target)
        {
            try
            {
                int bytesRead;
                while ((bytesRead = await source.ReceiveAsync(buffer, SocketFlags.None).ConfigureAwait(false)) != 0)
                {
                    Add(key, groupid, bytesRead, bytesRead);
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

    }

    public sealed class AsyncUserToken
    {
        public int ListenPort { get; set; }
        public string Host { get; set; }
        public string GroupId { get; set; }
        public bool IsWeb { get; set; }
        public Socket SourceSocket { get; set; }
        public Socket TargetSocket { get; set; }

        public byte BufferSize { get; set; }

        public void Clear()
        {
            SourceSocket?.SafeClose();
            TargetSocket?.SafeClose();

            GC.Collect();
        }
    }
}
