﻿using linker.libs;
using linker.libs.extends;
using linker.libs.timer;
using linker.messenger.signin;
using linker.tunnel;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Quic;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace linker.messenger.tunnel
{
    public sealed class TunnelNetworkTransfer
    {
        private readonly ISignInClientStore signInClientStore;
        private readonly SignInClientState signInClientState;
        private readonly ITunnelClientStore tunnelClientStore;
        private readonly IMessengerSender messengerSender;
        private readonly ISerializer serializer;

        public TunnelNetworkTransfer(ISignInClientStore signInClientStore, SignInClientState signInClientState, ITunnelClientStore tunnelClientStore, IMessengerSender messengerSender, ISerializer serializer, TunnelTransfer tunnelTransfer)
        {
            this.signInClientStore = signInClientStore;
            this.signInClientState = signInClientState;
            this.tunnelClientStore = tunnelClientStore;
            this.messengerSender = messengerSender;
            this.serializer = serializer;

            signInClientState.OnSignInBrfore += async () => { RefreshRouteLevel(); tunnelTransfer.Refresh(); await Task.CompletedTask; };
            tunnelTransfer.Refresh();

            TestQuic();

            RefreshRouteLevel();
            GetNet();

        }
        /// <summary>
        /// 刷新网关等级数据
        /// </summary>
        private void RefreshRouteLevel()
        {
            LoggerHelper.Instance.Info($"tunnel route level getting.");
            tunnelClientStore.Network.RouteLevel = NetworkHelper.GetRouteLevel(signInClientStore.Server.Host, out List<IPAddress> ips);
            LoggerHelper.Instance.Warning($"route ips:{string.Join(",", ips.Select(c => c.ToString()))}");
            tunnelClientStore.Network.RouteIPs = ips.ToArray();
            var ipv6 = NetworkHelper.GetIPV6();
            LoggerHelper.Instance.Warning($"tunnel local ip6 :{string.Join(",", ipv6.Select(c => c.ToString()))}");
            var ipv4 = NetworkHelper.GetIPV4();
            LoggerHelper.Instance.Warning($"tunnel local ip4 :{string.Join(",", ipv4.Select(c => c.ToString()))}");
            tunnelClientStore.Network.LocalIPs = ipv6.Concat(ipv4).ToArray();
            LoggerHelper.Instance.Warning($"tunnel route level:{tunnelClientStore.Network.RouteLevel}");

            tunnelClientStore.SetNetwork(tunnelClientStore.Network);
        }

        private async Task<bool> GetIsp()
        {
            try
            {
                using HttpClient httpClient = new HttpClient();
                string str = await httpClient.GetStringAsync($"http://ip-api.com/json").WaitAsync(TimeSpan.FromMilliseconds(3000)).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(str) == false)
                {
                    TunnelNetInfo net = str.DeJson<TunnelNetInfo>();
                    tunnelClientStore.Network.Net.Isp = net.Isp;
                    tunnelClientStore.Network.Net.CountryCode = net.CountryCode;
                    return true;
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.Instance.Warning(ex);
            }
            return false;
        }
        private async Task<bool> GetPosition()
        {
            try
            {
                using HttpClient httpClient = new HttpClient();
                string str = await httpClient.GetStringAsync($"https://api.myip.la/en?json").WaitAsync(TimeSpan.FromMilliseconds(5000)).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(str) == false)
                {
                    JsonNode json = JsonObject.Parse(str);
                    tunnelClientStore.Network.Net.City = json["location"]["city"].ToString();
                    tunnelClientStore.Network.Net.Lat = double.Parse(json["location"]["latitude"].ToString());
                    tunnelClientStore.Network.Net.Lon = double.Parse(json["location"]["longitude"].ToString());
                    return true;
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.Instance.Warning(ex);
            }
            return false;
        }
        private void GetNet()
        {
            TimerHelper.Async(async () =>
            {
                bool isp = false, city = false;
                for (int i = 0; i < 10; i++)
                {
                    if (isp == false) isp = await GetIsp();
                    if (city == false) city = await GetPosition();
                    if (isp && city)
                    {
                        break;
                    }
                    await Task.Delay(10000);
                }
                await tunnelClientStore.SetNetwork(tunnelClientStore.Network);
                await messengerSender.SendOnly(new MessageRequestWrap
                {
                    Connection = signInClientState.Connection,
                    MessengerId = (ushort)SignInMessengerIds.PushArg,
                    Payload = serializer.Serialize(new SignInPushArgInfo
                    {
                        Key = "tunnelNet",
                        Value = new SignInArgsNetInfo { Lat = tunnelClientStore.Network.Net.Lat, Lon = tunnelClientStore.Network.Net.Lon, City = tunnelClientStore.Network.Net.City }.ToJson()
                    })
                });
            });
        }

        private void TestQuic()
        {
            if (OperatingSystem.IsWindows())
            {
                if (QuicListener.IsSupported == false)
                {
                    try
                    {
                        if (File.Exists("msquic-openssl.dll"))
                        {
                            LoggerHelper.Instance.Warning($"copy msquic-openssl.dll -> msquic.dll，please restart linker");
                            File.Move("msquic.dll", "msquic.dll.temp", true);
                            File.Move("msquic-openssl.dll", "msquic.dll", true);

                            if (Environment.UserInteractive == false && OperatingSystem.IsWindows())
                            {
                                Environment.Exit(1);
                            }
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
                try
                {
                    if (File.Exists("msquic.dll.temp"))
                    {
                        File.Delete("msquic.dll.temp");
                    }
                    if (File.Exists("msquic-openssl.dll"))
                    {
                        File.Delete("msquic-openssl.dll");
                    }
                }
                catch (Exception)
                {
                }
            }
        }


        public TunnelLocalNetworkInfo GetLocalNetwork()
        {
            return new TunnelLocalNetworkInfo
            {
                MachineId = signInClientState.Connection?.Id ?? string.Empty,
                HostName = Dns.GetHostName(),
                Lans = GetInterfaces(),
                Routes = tunnelClientStore.Network.RouteIPs,
            };
        }
        private static byte[] ipv6LocalBytes = new byte[] { 254, 128, 0, 0, 0, 0, 0, 0 };
        private TunnelInterfaceInfo[] GetInterfaces()
        {
            return NetworkInterface.GetAllNetworkInterfaces().Select(c => new TunnelInterfaceInfo
            {
                Name = c.Name,
                Desc = c.Description,
                Mac = Regex.Replace(c.GetPhysicalAddress().ToString(), @"(.{2})", $"$1-").Trim('-'),
                Ips = c.GetIPProperties().UnicastAddresses.Select(c => c.Address).Where(c => c.AddressFamily == AddressFamily.InterNetwork || (c.AddressFamily == AddressFamily.InterNetworkV6 && c.GetAddressBytes().AsSpan(0, 8).SequenceEqual(ipv6LocalBytes) == false)).ToArray()
            }).Where(c => c.Ips.Length > 0 && c.Ips.Any(d => d.Equals(IPAddress.Loopback)) == false).ToArray();
        }

    }
}
