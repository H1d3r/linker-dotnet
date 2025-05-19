﻿using linker.libs;
using System.Collections.Concurrent;

namespace linker.messenger
{
    /// <summary>
    /// 信标消息发送
    /// </summary>
    public interface IMessengerSender
    {
        /// <summary>
        /// 发送并等待回复
        /// </summary>
        /// <param name="msg"></param>
        /// <returns></returns>
        public Task<MessageResponeInfo> SendReply(MessageRequestWrap msg);

        /// <summary>
        /// 仅发送
        /// </summary>
        /// <param name="msg"></param>
        /// <returns></returns>
        public Task<bool> SendOnly(MessageRequestWrap msg);
        /// <summary>
        /// 仅回复
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="messengerId"></param>
        /// <returns></returns>
        public ValueTask<bool> ReplyOnly(MessageResponseWrap msg, ushort messengerId);
        /// <summary>
        /// 回复
        /// </summary>
        /// <param name="wrap"></param>
        public ushort Response(MessageResponseWrap wrap);
    }

    /// <summary>
    /// 消息发送器
    /// </summary>
    public class MessengerSender : IMessengerSender
    {
        public NumberSpaceUInt32 requestIdNumberSpace = new NumberSpaceUInt32(0);
        private ConcurrentDictionary<uint, ReplyWrapInfo> sends = new ConcurrentDictionary<uint, ReplyWrapInfo>();

        public MessengerSender()
        {
        }

        public virtual void AddReceive(ushort id, long bytes) { }
        public virtual void AddSendt(ushort id, long bytes) { }
        public async Task<MessageResponeInfo> SendReply(MessageRequestWrap msg)
        {
            if (msg.Connection == null || msg.Connection.Connected == false)
            {
                return new MessageResponeInfo { Code = MessageResponeCodes.NOT_CONNECT };
            }

            if (msg.RequestId == 0)
            {
                uint id = msg.RequestId;
                Interlocked.CompareExchange(ref id, requestIdNumberSpace.Increment(), 0);
                msg.RequestId = id;
            }

            msg.Reply = true;
            if (msg.Timeout <= 0)
            {
                msg.Timeout = 15000;
            }
            TaskCompletionSource<MessageResponeInfo> tcs = new TaskCompletionSource<MessageResponeInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            sends.TryAdd(msg.RequestId, new ReplyWrapInfo { Tcs = tcs, MessengerId = msg.MessengerId });

            bool res = await SendOnly(msg).ConfigureAwait(false);
            if (res == false)
            {
                sends.TryRemove(msg.RequestId, out _);
                tcs.TrySetResult(new MessageResponeInfo { Code = MessageResponeCodes.NOT_CONNECT });
            }

            try
            {
                return await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(msg.Timeout)).ConfigureAwait(false);
            }
            catch (Exception)
            {
                tcs.TrySetResult(new MessageResponeInfo { Code = MessageResponeCodes.NOT_CONNECT });
                sends.TryRemove(msg.RequestId, out _);
                return new MessageResponeInfo { Code = MessageResponeCodes.TIMEOUT };
            }
        }
        public async Task<bool> SendOnly(MessageRequestWrap msg)
        {
            if (msg.Connection == null || msg.Connection.Connected == false)
            {
                return false;
            }

            try
            {
                if (msg.RequestId == 0)
                {
                    uint id = msg.RequestId;
                    Interlocked.CompareExchange(ref id, requestIdNumberSpace.Increment(), 0);
                    msg.RequestId = id;
                }

                byte[] bytes = msg.ToArray(out int length);

                AddSendt(msg.MessengerId, bytes.Length);

                bool res = await msg.Connection.SendAsync(bytes.AsMemory(0, length)).ConfigureAwait(false);
                msg.Return(bytes);
                return res;
            }
            catch (Exception ex)
            {
                LoggerHelper.Instance.Error(ex);
            }
            return false;
        }

        public async ValueTask<bool> ReplyOnly(MessageResponseWrap msg, ushort messengerId)
        {
            if (msg.Connection == null)
            {
                return false;
            }

            try
            {

                byte[] bytes = msg.ToArray(out int length);

                AddSendt(messengerId, length);

                bool res = await msg.Connection.SendAsync(bytes.AsMemory(0, length)).ConfigureAwait(false);
                msg.Return(bytes);
                return res;
            }
            catch (Exception ex)
            {
                if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                    LoggerHelper.Instance.Error(ex);
            }
            return false;
        }

        public ushort Response(MessageResponseWrap wrap)
        {
            if (sends.TryRemove(wrap.RequestId, out ReplyWrapInfo info))
            {
                byte[] bytes = new byte[wrap.Payload.Length];
                wrap.Payload.CopyTo(bytes);

                AddReceive(info.MessengerId, bytes.Length);
                info.Tcs.TrySetResult(new MessageResponeInfo { Code = wrap.Code, Data = bytes, Connection = wrap.Connection });
                return info.MessengerId;
            }
            return 0;
        }
    }

    public sealed class ReplyWrapInfo
    {
        public TaskCompletionSource<MessageResponeInfo> Tcs { get; set; }
        public ushort MessengerId { get; set; }
    }
    public sealed class MessageResponeInfo
    {
        public MessageResponeCodes Code { get; set; }
        public ReadOnlyMemory<byte> Data { get; set; }
        public IConnection Connection { get; set; }

    }
}

