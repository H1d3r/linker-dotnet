﻿using linker.libs;
using linker.libs.extends;
using System.Buffers;
using System.Net.Security;
using System.Net;
using System.Text.Json.Serialization;
using System.Text;
using System.Net.Sockets;
using System.IO.Pipelines;

namespace linker.tunnel.connection
{
    /// <summary>
    /// TCP隧道
    /// </summary>
    public sealed class TunnelConnectionTcp : ITunnelConnection
    {
        public TunnelConnectionTcp()
        {
        }

        public string RemoteMachineId { get; init; }
        public string RemoteMachineName { get; init; }
        public string TransactionId { get; init; }
        public string TransactionTag { get; init; }
        public string TransportName { get; init; }
        public string Label { get; init; }
        public TunnelMode Mode { get; init; }
        public TunnelProtocolType ProtocolType { get; init; }
        public TunnelType Type { get; init; }
        public string NodeId { get; init; }
        public TunnelDirection Direction { get; init; }
        public IPEndPoint IPEndPoint { get; init; }
        public bool SSL { get; init; }
        public byte BufferSize { get; init; } = 3;
        public bool Connected => Socket != null && LastTicks.Expired(15000) == false;
        public int Delay { get; private set; }
        public long SendBytes { get; private set; }
        public long ReceiveBytes { get; private set; }

        private long sendRemaining = 0;
        public long SendBufferRemaining { get => sendRemaining; }
        public long SendBufferFree { get => maxSendRemaining - sendRemaining; }
        private const long maxSendRemaining = 1 * 1024 * 1024;

        public LastTicksManager LastTicks { get; private set; } = new LastTicksManager();

        [JsonIgnore]
        public byte[] PacketBuffer { get; set; } = Helper.EmptyArray;


        [JsonIgnore]
        public SslStream Stream { get; init; }

        [JsonIgnore]
        public Socket Socket { get; init; }


        private ITunnelConnectionReceiveCallback callback;
        private CancellationTokenSource cancellationTokenSource;
        private object userToken;

        private readonly LastTicksManager pingTicks = new LastTicksManager();
        private readonly byte[] pingBytes = Encoding.UTF8.GetBytes($"{Helper.GlobalString}.tcp.ping");
        private readonly byte[] pongBytes = Encoding.UTF8.GetBytes($"{Helper.GlobalString}.tcp.pong");

        private Pipe pipeSender;
        private Pipe pipeWriter;
        /// <summary>
        /// 开始接收数据
        /// </summary>
        /// <param name="callback">数据回调</param>
        /// <param name="userToken">自定义数据</param>
        public void BeginReceive(ITunnelConnectionReceiveCallback callback, object userToken)
        {
            if (this.callback != null) return;

            this.callback = callback;
            this.userToken = userToken;

            cancellationTokenSource = new CancellationTokenSource();

            pipeSender = new Pipe(new PipeOptions(pauseWriterThreshold: maxSendRemaining, resumeWriterThreshold: (maxSendRemaining / 2), useSynchronizationContext: false));
            pipeWriter = new Pipe(new PipeOptions(pauseWriterThreshold: maxSendRemaining, resumeWriterThreshold: (maxSendRemaining / 2), useSynchronizationContext: false));
            _ = ProcessWrite();
            _ = Sender();
            _ = Recver();
            _ = ProcessHeart();
        }

        private async Task ProcessWrite()
        {
            try
            {
                int length = 0;
                while (cancellationTokenSource.IsCancellationRequested == false)
                {
                    if (Stream != null)
                    {
                        Memory<byte> memory = pipeWriter.Writer.GetMemory(8 * 1024);
                        length = await Stream.ReadAsync(memory).ConfigureAwait(false);
                        if (length == 0)
                        {
                            break;
                        }
                        pipeWriter.Writer.Advance(length);
                        await pipeWriter.Writer.FlushAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        Memory<byte> memory = pipeWriter.Writer.GetMemory(8 * 1024);
                        length = await Socket.ReceiveAsync(memory, SocketFlags.None).ConfigureAwait(false);
                        if (length == 0) break;
                        pipeWriter.Writer.Advance(length);
                        await pipeWriter.Writer.FlushAsync().ConfigureAwait(false);
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
                Dispose();
            }
        }
        private async Task Recver()
        {
            IMemoryOwner<byte> packetBuffer = MemoryPool<byte>.Shared.Rent(4 * 1024);
            while (cancellationTokenSource.IsCancellationRequested == false)
            {
                try
                {
                    ReadResult result = await pipeWriter.Reader.ReadAsync().ConfigureAwait(false);
                    if (result.IsCompleted && result.Buffer.IsEmpty)
                    {
                        cancellationTokenSource.Cancel();
                        break;
                    }

                    ReadOnlySequence<byte> buffer = result.Buffer;
                    ReceiveBytes += buffer.Length;
                    long offset = 0;

                    do
                    {
                        //读取包长度
                        int packetLength = 0;
                        if (buffer.First.Length >= 4)
                        {
                            packetLength = buffer.First.ToInt32();
                        }
                        else
                        {
                            //长度标识跨段了
                            buffer.Slice(0, 4).CopyTo(packetBuffer.Memory.Span);
                            packetLength = packetBuffer.Memory.ToInt32();
                        }
                        //数据量不够
                        if (packetLength + 4 > buffer.Length) break;

                        //复制一份
                        ReadOnlySequence<byte> temp = buffer.Slice(4, packetLength);
                        if (packetBuffer.Memory.Length < temp.Length)
                        {
                            packetBuffer.Dispose();
                            packetBuffer = MemoryPool<byte>.Shared.Rent((int)temp.Length);
                        }
                        temp.CopyTo(packetBuffer.Memory.Span);
                        //处理数据包
                        await WritePacket(packetBuffer.Memory.Slice(0, packetLength)).ConfigureAwait(false);

                        //移动位置
                        offset += 4 + packetLength;
                        //去掉已处理部分
                        buffer = buffer.Slice(4 + packetLength);

                    } while (buffer.Length > 4);

                    //告诉管道已经处理了多少数据，检查了多少数据
                    pipeWriter.Reader.AdvanceTo(result.Buffer.GetPosition(offset), result.Buffer.End);
                }
                catch (Exception ex)
                {
                    if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                    {
                        LoggerHelper.Instance.Error(ex);
                    }
                    Dispose();
                }
            }
            packetBuffer.Dispose();
        }
        private async Task WritePacket(ReadOnlyMemory<byte> packet)
        {

            LastTicks.Update();
            if (packet.Length == pingBytes.Length && packet.Span.Slice(0, pingBytes.Length - 4).SequenceEqual(pingBytes.AsSpan(0, pingBytes.Length - 4)))
            {
                if (packet.Span.SequenceEqual(pingBytes))
                {
                    await SendPingPong(pongBytes).ConfigureAwait(false);
                    return;
                }
                else if (packet.Span.SequenceEqual(pongBytes))
                {
                    Delay = (int)pingTicks.Diff();
                    return;
                }
            }
            try
            {
                await callback.Receive(this, packet, this.userToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LoggerHelper.Instance.Error(ex);
                if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                    LoggerHelper.Instance.Error(string.Join(",", packet.ToArray()));
            }
        }

        private async Task ProcessHeart()
        {
            try
            {
                while (cancellationTokenSource.IsCancellationRequested == false)
                {
                    if (Connected == false)
                    {
                        Dispose();
                        break;
                    }

                    if (LastTicks.DiffGreater(3000))
                    {
                        pingTicks.Update();
                        await SendPingPong(pingBytes).ConfigureAwait(false);

                    }
                    await Task.Delay(3000).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                {
                    LoggerHelper.Instance.Error(ex);
                }
            }
        }
        private async Task SendPingPong(byte[] data)
        {
            int length = 4 + data.Length;

            byte[] heartData = ArrayPool<byte>.Shared.Rent(length);
            data.Length.ToBytes(heartData.AsSpan());
            data.AsMemory().CopyTo(heartData.AsMemory(4));

            await SendAsync(heartData.AsMemory(0, length));

            ArrayPool<byte>.Shared.Return(heartData);
        }

        private async Task Sender()
        {
            while (cancellationTokenSource.IsCancellationRequested == false)
            {
                try
                {
                    ReadResult result = await pipeSender.Reader.ReadAsync().ConfigureAwait(false);
                    if (result.IsCompleted && result.Buffer.IsEmpty)
                    {
                        cancellationTokenSource.Cancel();
                        break;
                    }
                    if (result.Buffer.IsEmpty)
                    {
                        continue;
                    }

                    ReadOnlySequence<byte> buffer = result.Buffer;
                    foreach (ReadOnlyMemory<byte> memoryBlock in result.Buffer)
                    {
                        if (Stream != null)
                        {
                            await Stream.WriteAsync(memoryBlock).ConfigureAwait(false);
                        }
                        else
                        {
                            int sendt = 0;
                            do
                            {
                                ReadOnlyMemory<byte> sendBlock = memoryBlock.Slice(sendt);
                                int remaining = await Socket.SendAsync(sendBlock, SocketFlags.None).ConfigureAwait(false);
                                if (remaining == 0) break;

                                sendt += remaining;
                            } while (sendt < memoryBlock.Length);
                        }
                        Interlocked.Add(ref sendRemaining, -memoryBlock.Length);
                    }
                    pipeSender.Reader.AdvanceTo(buffer.End);
                    LastTicks.Update();
                }
                catch (Exception ex)
                {
                    if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                    {
                        LoggerHelper.Instance.Error(ex);
                    }
                    Dispose();
                }
            }
        }

        private readonly object _writeLock = new object();
        public async Task<bool> SendAsync(ReadOnlyMemory<byte> data)
        {
            if (callback == null) return false;

            lock (_writeLock)
            {
                Memory<byte> memory = pipeSender.Writer.GetMemory(data.Length);
                data.CopyTo(memory);
                pipeSender.Writer.Advance(data.Length);
                Interlocked.Add(ref sendRemaining, data.Length);
            }
            await pipeSender.Writer.FlushAsync().ConfigureAwait(false);
            return true;
        }
        public async Task<bool> SendAsync(byte[] buffer, int offset, int length)
        {
            return await SendAsync(buffer.AsMemory(offset, length)).ConfigureAwait(false);
        }

        public void Dispose()
        {
            LastTicks.Clear();
            if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                LoggerHelper.Instance.Error($"tunnel connection {this.GetHashCode()} writer offline {ToString()}");

            callback?.Closed(this, userToken);
            callback = null;
            userToken = null;
            cancellationTokenSource?.Cancel();

            Interlocked.Exchange(ref sendRemaining, 0);

            Stream?.Close();
            Stream?.Dispose();

            Socket?.SafeClose();

            try
            {
                pipeSender?.Writer.Complete();
                pipeSender?.Reader.Complete();

                pipeWriter?.Writer.Complete();
                pipeWriter?.Reader.Complete();
            }
            catch (Exception)
            { }
            GC.Collect();

        }
        public override string ToString()
        {
            return this.ToJsonFormat();
        }
        public bool Equals(ITunnelConnection connection)
        {
            return connection != null && GetHashCode() == connection.GetHashCode() && TransactionId == connection.TransactionId && IPEndPoint.Equals(connection.IPEndPoint);
        }
    }
}
