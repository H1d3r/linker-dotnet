﻿using linker.messenger.api;
using System.Collections;
using System.Reflection;

namespace linker.messenger.store.file
{
    public partial class ConfigClientInfo
    {
        /// <summary>
        /// 客户端管理接口配置
        /// </summary>
        public ApiClientInfo CApi { get; set; } = new ApiClientInfo();

        private Dictionary<string, AccessTextInfo> accesss;
        [SaveJsonIgnore]
        public Dictionary<string, AccessTextInfo> Accesss
        {
            get
            {
                if (accesss == null)
                {
                    accesss = new Dictionary<string, AccessTextInfo>();
                    Type enumType = typeof(AccessValue);
                    // 获取所有字段值
                    foreach (var value in Enum.GetValues(enumType))
                    {
                        // 获取字段信息
                        FieldInfo fieldInfo = enumType.GetField(value.ToString());
                        var attribute = fieldInfo.GetCustomAttribute<AccessDisplayAttribute>(false);
                        if (attribute != null)
                        {
                            accesss.TryAdd(fieldInfo.Name, new AccessTextInfo { Text = attribute.Value, Value = (int)value });
                        }
                    }
                }

                return accesss;
            }
        }
        /// <summary>
        /// 管理权限
        /// </summary>
        public long Access { get; set; } = -1;

        private BitArray accessBits;
        public BitArray AccessBits
        {
            get
            {
                if (Access != -1)
                {
                    accessBits = new BitArray(Convert.ToString(Access, 2).Reverse().Select(c => c == '1').ToArray());
                    Access = -1;
                }
                else if (accessBits == null)
                {
                    accessBits = new BitArray(Accesss.Count,true);
                }
                return accessBits;
            }
            set
            {
                accessBits = value;
            }
        }
    }
}
