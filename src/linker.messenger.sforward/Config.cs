﻿using linker.messenger.sforward.server;
using System.Net;


namespace linker.messenger.sforward
{
    public partial class SForwardInfo
    {
        public SForwardInfo() { }
        /// <summary>
        /// 穿透id
        /// </summary>
        public long Id { get; set; }
        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// 域名
        /// </summary>
        public string Domain { get; set; }
        /// <summary>
        /// 端口，
        /// </summary>
        public int RemotePort { get; set; }
        /// <summary>
        /// 缓冲区
        /// </summary>
        public byte BufferSize { get; set; } = 3;
        /// <summary>
        /// 本地服务
        /// </summary>
        public IPEndPoint LocalEP { get; set; }
        /// <summary>
        /// 已启动
        /// </summary>
        public bool Started { get; set; }
        /// <summary>
        /// 服务器错误信息
        /// </summary>
        public string Msg { get; set; }
        /// <summary>
        /// 本地错误信息
        /// </summary>
        public string LocalMsg { get; set; }

        /// <summary>
        /// 端口范围
        /// </summary>
        public int RemotePortMin { get; set; }
        /// <summary>
        /// 端口范围
        /// </summary>
        public int RemotePortMax { get; set; }


    }

    public sealed class SForwardInfo191 : SForwardInfo
    {
        public string NodeId { get; set; } = string.Empty;
        public string NodeId1 { get; set; } = string.Empty;
    }

    public sealed class SForwardConfigServerInfo
    {
        /// <summary>
        /// web端口
        /// </summary>
        public int WebPort { get; set; }
        /// <summary>
        /// 开放端口范围
        /// </summary>
        public int[] TunnelPortRange { get; set; } = new int[] { 10000, 60000 };

        public DistributedInfo Distributed { get; set; } = new DistributedInfo { };
    }
    public sealed class DistributedInfo
    {
        public SForwardServerNodeInfo Node { get; set; } = new SForwardServerNodeInfo { };
        public SForwardServerMasterInfo Master { get; set; } = new SForwardServerMasterInfo { };
    }

    /// <summary>
    /// 往服务器添加穿透
    /// </summary>
    public partial class SForwardAddInfo
    {
        /// <summary>
        /// 域名
        /// </summary>
        public string Domain { get; set; }
        /// <summary>
        /// 或者端口。域名优先
        /// </summary>
        public int RemotePort { get; set; }
    }
    public sealed partial class SForwardAddInfo191 : SForwardAddInfo
    {
        /// <summary>
        /// 节点id
        /// </summary>
        public string NodeId { get; set; } = string.Empty;
        public string MachineId { get; set; } = string.Empty;
        public string GroupId { get; set; } = string.Empty;

        public bool Validated { get; set; }
        public List<SForwardCdkeyInfo> Cdkey { get; set; } = [];
    }

    /// <summary>
    /// 添加穿透结果
    /// </summary>
    public sealed partial class SForwardAddResultInfo
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }
        /// <summary>
        /// 失败信息
        /// </summary>
        public string Message { get; set; }
        /// <summary>
        /// buffsize
        /// </summary>
        public byte BufferSize { get; set; }
    }

    public partial class SForwardAddForwardInfo
    {
        public string MachineId { get; set; }
        public SForwardInfo Data { get; set; }
    }
    public sealed partial class SForwardAddForwardInfo191 : SForwardAddForwardInfo
    {
        public new SForwardInfo191 Data { get; set; }
    }
    public sealed partial class SForwardRemoveForwardInfo
    {
        public string MachineId { get; set; }
        public int Id { get; set; }
    }

    /// <summary>
    /// 服务器穿透代理信息
    /// </summary>
    public sealed partial class SForwardProxyInfo
    {
        /// <summary>
        /// 请求编号
        /// </summary>
        public ulong Id { get; set; }
        /// <summary>
        /// 域名
        /// </summary>
        public string Domain { get; set; }
        /// <summary>
        /// 端口
        /// </summary>
        public int RemotePort { get; set; }
        /// <summary>
        /// bufsize
        /// </summary>
        public byte BufferSize { get; set; } = 3;
    }


    public sealed partial class SForwardCountInfo
    {
        public string MachineId { get; set; }
        public int Count { get; set; }
    }
}
