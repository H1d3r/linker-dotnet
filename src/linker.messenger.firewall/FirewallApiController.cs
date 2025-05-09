﻿using linker.libs.api;
using linker.libs.extends;
using linker.libs;
using linker.messenger.signin;
using linker.messenger.api;

namespace linker.messenger.firewall
{
    public sealed class FirewallApiController : IApiController
    {
        private readonly FirewallTransfer firewallTransfer;
        private readonly IMessengerSender messengerSender;
        private readonly SignInClientState signInClientState;
        private readonly IAccessStore accessStore;
        private readonly ISignInClientStore signInClientStore;
        private readonly ISerializer serializer;

        public FirewallApiController(FirewallTransfer firewallTransfer, IMessengerSender messengerSender, SignInClientState signInClientState, IAccessStore accessStore, ISignInClientStore signInClientStore, ISerializer serializer)
        {
            this.firewallTransfer = firewallTransfer;
            this.messengerSender = messengerSender;
            this.signInClientState = signInClientState;
            this.accessStore = accessStore;
            this.signInClientStore = signInClientStore;
            this.serializer = serializer;
        }

        /// <summary>
        /// 获取列表
        /// </summary>
        /// <param name="param"></param>
        /// <returns></returns>
        public async Task<List<FirewallRuleInfo>> Get(ApiControllerParamsInfo param)
        {
            FirewallSearchForwardInfo info = param.Content.DeJson<FirewallSearchForwardInfo>();
            if (info.MachineId == signInClientStore.Id)
            {
                if (accessStore.HasAccess(AccessValue.FirewallSelf) == false) return new List<FirewallRuleInfo>();
                return firewallTransfer.Get(info.Data).ToList();
            }
            if (accessStore.HasAccess(AccessValue.FirewallOther) == false) return new List<FirewallRuleInfo>();

            var resp = await messengerSender.SendReply(new MessageRequestWrap
            {
                Connection = signInClientState.Connection,
                MessengerId = (ushort)FirewallMessengerIds.GetForward,
                Payload = serializer.Serialize(info)
            }).ConfigureAwait(false);
            if (resp.Code == MessageResponeCodes.OK)
            {
                return serializer.Deserialize<List<FirewallRuleInfo>>(resp.Data.Span);
            }
            return new List<FirewallRuleInfo>();
        }

        /// <summary>
        /// 添加
        /// </summary>
        /// <param name="param"></param>
        /// <returns></returns>
        public async Task<bool> Add(ApiControllerParamsInfo param)
        {
            FirewallAddForwardInfo info = param.Content.DeJson<FirewallAddForwardInfo>();
            if (info.MachineId == signInClientStore.Id)
            {
                if (accessStore.HasAccess(AccessValue.FirewallSelf) == false) return false;
                return firewallTransfer.Add(info.Data);
            }
            if (accessStore.HasAccess(AccessValue.FirewallOther) == false) return false;

            return await messengerSender.SendOnly(new MessageRequestWrap
            {
                Connection = signInClientState.Connection,
                MessengerId = (ushort)FirewallMessengerIds.AddForward,
                Payload = serializer.Serialize(info)
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// 删除
        /// </summary>
        /// <param name="param"></param>
        /// <returns></returns>
        public async Task<bool> Remove(ApiControllerParamsInfo param)
        {
            FirewallRemoveForwardInfo info = param.Content.DeJson<FirewallRemoveForwardInfo>();
            if (info.MachineId == signInClientStore.Id)
            {
                if (accessStore.HasAccess(AccessValue.FirewallSelf) == false) return false;
                return firewallTransfer.Remove(info.Id);
            }

            if (accessStore.HasAccess(AccessValue.FirewallOther) == false) return false;
            return await messengerSender.SendOnly(new MessageRequestWrap
            {
                Connection = signInClientState.Connection,
                MessengerId = (ushort)FirewallMessengerIds.RemoveForward,
                Payload = serializer.Serialize(info)
            }).ConfigureAwait(false);
        }
        /// <summary>
        /// 状态
        /// </summary>
        /// <param name="param"></param>
        /// <returns></returns>
        public async Task<bool> State(ApiControllerParamsInfo param)
        {
            FirewallStateForwardInfo info = param.Content.DeJson<FirewallStateForwardInfo>();
            if (info.MachineId == signInClientStore.Id)
            {
                if (accessStore.HasAccess(AccessValue.FirewallSelf) == false) return false;
                return firewallTransfer.State(info.State);
            }

            if (accessStore.HasAccess(AccessValue.FirewallOther) == false) return false;
            return await messengerSender.SendOnly(new MessageRequestWrap
            {
                Connection = signInClientState.Connection,
                MessengerId = (ushort)FirewallMessengerIds.StateForward,
                Payload = serializer.Serialize(info)
            }).ConfigureAwait(false);
        }
    }
}
