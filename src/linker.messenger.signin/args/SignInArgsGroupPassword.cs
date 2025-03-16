﻿namespace linker.messenger.signin.args
{
    /// <summary>
    /// 添加分组密码
    /// </summary>
    public sealed class SignInArgsGroupPasswordClient : ISignInArgs
    {
        public string Name => "group";

        private readonly ISignInClientStore signInClientStore;
        public SignInArgsGroupPasswordClient(ISignInClientStore signInClientStore)
        {
            this.signInClientStore = signInClientStore;
        }

      

        public async Task<string> Invoke(string host, Dictionary<string, string> args)
        {
            args.TryAdd("signin-gpwd", signInClientStore.Group.Password);
            await Task.CompletedTask.ConfigureAwait(false);
            return string.Empty;
        }

        public async Task<string> Validate(SignInfo signInfo, SignCacheInfo cache)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            return string.Empty;
        }
    }

    /// <summary>
    /// 验证分组密码
    /// </summary>
    public sealed class SignInArgsGroupPasswordServer : ISignInArgs
    {
        public string Name => "group";
        public SignInArgsGroupPasswordServer()
        {
        }
        public async Task<string> Invoke(string host, Dictionary<string, string> args)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            return string.Empty;
        }

        /// <summary>
        /// 验证参数
        /// </summary>
        /// <param name="signInfo">新登录参数</param>
        /// <param name="cache">之前的登录信息</param>
        /// <returns></returns>
        public async Task<string> Validate(SignInfo signInfo, SignCacheInfo cache)
        {
            if (signInfo.Args.TryGetValue("signin-gpwd", out string gpwd) && string.IsNullOrWhiteSpace(gpwd) == false)
            {
                signInfo.GroupId = $"{signInfo.GroupId}->{gpwd}";
            }
            await Task.CompletedTask.ConfigureAwait(false);
            return string.Empty;
        }


    }
}
