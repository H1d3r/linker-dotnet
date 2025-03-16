﻿namespace linker.messenger.signin.args
{
    /// <summary>
    /// 登录参数处理
    /// </summary>
    public sealed partial class SignInArgsTransfer
    {
        private List<ISignInArgs> startups = new List<ISignInArgs>();

        public SignInArgsTransfer()
        {
        }

        /// <summary>
        /// 加载所有登录参数实现类
        /// </summary>
        /// <param name="list"></param>
        public void AddArgs(List<ISignInArgs> list)
        {
            startups = startups.Concat(list).Distinct().ToList();
        }

        /// <summary>
        /// 删除实现类
        /// </summary>
        /// <param name="names"></param>
        public void RemoveArgs(List<string> names)
        {
            foreach (string name in names)
            {
                ISignInArgs item = startups.FirstOrDefault(c => c.Name == name);
                if (item != null)
                    startups.Remove(item);
            }
        }

        /// <summary>
        /// 客户端调用
        /// </summary>
        /// <param name="host"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        public async Task<string> Invoke(string host, Dictionary<string, string> args)
        {
            foreach (var item in startups)
            {
                string result = await item.Invoke(host, args).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(result) == false)
                {
                    return result;
                }
            }
            return string.Empty;
        }
        /// <summary>
        /// 服务器调用
        /// </summary>
        /// <param name="signInfo"></param>
        /// <param name="cache"></param>
        /// <returns></returns>
        public async Task<string> Validate(SignInfo signInfo, SignCacheInfo cache)
        {
            foreach (var item in startups)
            {
                string result = await item.Validate(signInfo, cache).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(result) == false)
                {
                    return result;
                }
            }
            return string.Empty;
        }
    }
}
