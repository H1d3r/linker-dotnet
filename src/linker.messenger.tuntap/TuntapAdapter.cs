﻿using linker.libs;
using linker.libs.extends;
using linker.messenger.exroute;
using linker.messenger.signin;
using linker.tun;
using linker.tunnel.connection;
using System.Diagnostics;

namespace linker.messenger.tuntap
{
    public sealed class TuntapAdapter : ILinkerTunDeviceCallback, ITuntapProxyCallback
    {
        private List<LinkerTunDeviceForwardItem> forwardItems = new List<LinkerTunDeviceForwardItem>();


        private readonly TuntapTransfer tuntapTransfer;
        private readonly TuntapConfigTransfer tuntapConfigTransfer;
        private readonly TuntapDecenter tuntapDecenter;
        private readonly TuntapProxy tuntapProxy;
        private readonly ISignInClientStore signInClientStore;
        private readonly ExRouteTransfer exRouteTransfer;

        public TuntapAdapter(TuntapTransfer tuntapTransfer, TuntapConfigTransfer tuntapConfigTransfer, TuntapDecenter tuntapDecenter, TuntapProxy tuntapProxy,
            SignInClientState signInClientState, ISignInClientStore signInClientStore, ExRouteTransfer exRouteTransfer)
        {
            this.tuntapTransfer = tuntapTransfer;
            this.tuntapConfigTransfer = tuntapConfigTransfer;
            this.tuntapDecenter = tuntapDecenter;
            this.tuntapProxy = tuntapProxy;
            this.signInClientStore = signInClientStore;
            this.exRouteTransfer = exRouteTransfer;

            //与服务器连接，刷新一下IP
            signInClientState.OnSignInSuccess += (times) => tuntapConfigTransfer.RefreshIP();

            //初始化网卡
            tuntapTransfer.Init(this);
            //网卡状态发生变化，同步一下信息
            tuntapTransfer.OnSetupBefore += () =>
            {
                tuntapConfigTransfer.SetRunning(true);
                tuntapDecenter.Refresh();
                if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                    LoggerHelper.Instance.Warning("tuntap setup before");
            };
            tuntapTransfer.OnSetupAfter += () =>
            {
                tuntapDecenter.Refresh();
                if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                    LoggerHelper.Instance.Warning("tuntap setup after");
            };
            tuntapTransfer.OnSetupSuccess += () =>
            {
                AddForward();
            };
            tuntapTransfer.OnShutdownBefore += () =>
            {
                tuntapDecenter.Refresh();
                if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                    LoggerHelper.Instance.Warning("tuntap shutdown before");
            };
            tuntapTransfer.OnShutdownAfter += () =>
            {
                tuntapDecenter.Refresh(); DeleteForward(); 
                if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                    LoggerHelper.Instance.Warning("tuntap shutdown after");
                tuntapConfigTransfer.SetRunning(false);
            };

            //配置有更新，去同步一下
            tuntapConfigTransfer.OnUpdate += () => { AddForward(); _ = CheckDevice(); tuntapDecenter.Refresh(); };

            //隧道回调
            tuntapProxy.Callback = this;
            CheckDeviceTask();
        }


        private ulong configVersion = 0;
        private OperatingManager checking = new OperatingManager();
        private void CheckDeviceTask()
        {
            TimerHelper.SetIntervalLong(async () =>
            {
                await CheckDevice();
                return true;
            }, 30000);
        }
        private async Task CheckDevice()
        {
            try
            {
                bool start = checking.StartOperation();

                //开始操作失败，或者网卡正在操作中，或者不需要运行
                if (start == false || tuntapTransfer.Status == TuntapStatus.Operating || tuntapConfigTransfer.Running == false)
                {
                    if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                        LoggerHelper.Instance.Warning($"tuntap check device continue :start:{start},status:{tuntapTransfer.Status},running:{tuntapConfigTransfer.Running}");
                    return;
                }

                //配置发生变化，或者网卡不可用
                if (tuntapConfigTransfer.Version.Eq(configVersion, out ulong version) == false || tuntapTransfer.Status != TuntapStatus.Running || await tuntapTransfer.CheckAvailable() == false)
                {
                    if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                        LoggerHelper.Instance.Warning($"tuntap config version changed, restarting device");
                    configVersion = version;
                    await RetstartDevice();
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                checking.StopOperation();
            }
        }

        public async Task Callback(LinkerTunDevicPacket packet)
        {
            if (packet.IPV4Broadcast || packet.IPV6Multicast)
            {
                if ((tuntapConfigTransfer.Switch & TuntapSwitch.Multicast) == TuntapSwitch.Multicast)
                {
                    return;
                }
            }
            await tuntapProxy.InputPacket(packet);
        }

        public async ValueTask Close(ITunnelConnection connection)
        {
            tuntapDecenter.Refresh();
            await ValueTask.CompletedTask;
        }
        public void Receive(ITunnelConnection connection, ReadOnlyMemory<byte> buffer)
        {
            tuntapTransfer.Write(buffer);
        }
        public async ValueTask NotFound(uint ip)
        {
            tuntapDecenter.Refresh();
            await ValueTask.CompletedTask;
        }

        /// <summary>
        /// 重启网卡
        /// </summary>
        /// <returns></returns>
        public async Task RetstartDevice()
        {
            if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                LoggerHelper.Instance.Warning($"restart, stop device");
            tuntapTransfer.Shutdown();
            await tuntapConfigTransfer.RefreshIPASync();
            tuntapTransfer.Setup(tuntapConfigTransfer.Name, tuntapConfigTransfer.Info.IP, tuntapConfigTransfer.Info.PrefixLength, tuntapConfigTransfer.Info.DisableNat == false);
        }
        /// <summary>
        /// 关闭网卡
        /// </summary>
        public void StopDevice()
        {
            if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                LoggerHelper.Instance.Warning($"stop device");
            tuntapTransfer.Shutdown();
        }

        // <summary>
        /// 添加端口转发
        /// </summary>
        private void AddForward()
        {
            var temp = ParseForwardItems();
            var removes = forwardItems.Except(temp, new LinkerTunDeviceForwardItemComparer());
            if (removes.Any())
            {
                tuntapTransfer.RemoveForward(removes.ToList());
            }
            forwardItems = temp;
            if (tuntapTransfer.Status != TuntapStatus.Normal)
            {
                if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                    LoggerHelper.Instance.Debug($"add tuntap forward {forwardItems.ToJson()}");
                tuntapTransfer.AddForward(forwardItems);
            }
        }
        /// <summary>
        /// 删除端口转发
        /// </summary>
        public void DeleteForward()
        {
            tuntapTransfer.RemoveForward(forwardItems);
        }
        private List<LinkerTunDeviceForwardItem> ParseForwardItems()
        {
            return tuntapConfigTransfer.Info.Forwards.Select(c => new LinkerTunDeviceForwardItem { ListenAddr = c.ListenAddr, ListenPort = c.ListenPort, ConnectAddr = c.ConnectAddr, ConnectPort = c.ConnectPort }).ToList();
        }


    }
}
