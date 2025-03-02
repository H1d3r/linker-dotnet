﻿using linker.libs;
using linker.messenger.signin;
using linker.messenger.sync;

namespace linker.messenger.store.file.signIn
{
    public sealed class SignInSyncServer : ISync
    {
        public string Name => "SignInServer";

        private readonly ISignInClientStore signInClientStore;
        private readonly ISerializer serializer;
        public SignInSyncServer(ISignInClientStore signInClientStore, ISerializer serializer)
        {
            this.signInClientStore = signInClientStore;
            this.serializer = serializer;
        }
        public Memory<byte> GetData()
        {
            return serializer.Serialize(signInClientStore.Server.Host);
        }

        public void SetData(Memory<byte> data)
        {
            signInClientStore.SetHost(serializer.Deserialize<string>(data.Span));
        }
    }
    public sealed class SignInSyncSecretKey : ISync
    {
        public string Name => "SignInSecretKey";

        private readonly ISignInClientStore signInClientStore;
        private readonly ISerializer serializer;
        public SignInSyncSecretKey(ISignInClientStore signInClientStore, ISerializer serializer)
        {
            this.signInClientStore = signInClientStore;
            this.serializer = serializer;
        }
        public Memory<byte> GetData()
        {
            return serializer.Serialize(signInClientStore.Server.SecretKey);
        }

        public void SetData(Memory<byte> data)
        {
            signInClientStore.SetSecretKey(serializer.Deserialize<string>(data.Span));
        }
    }
    public sealed class SignInSyncUserId : ISync
    {
        public string Name => "SignInUserId";

        private readonly ISignInClientStore signInClientStore;
        private readonly ISerializer serializer;
        public SignInSyncUserId(ISignInClientStore signInClientStore, ISerializer serializer)
        {
            this.signInClientStore = signInClientStore;
            this.serializer = serializer;
        }
        public Memory<byte> GetData()
        {
            return serializer.Serialize(signInClientStore.Server.UserId);
        }

        public void SetData(Memory<byte> data)
        {
            signInClientStore.SetUserId(serializer.Deserialize<string>(data.Span));
        }
    }
    public sealed class SignInSyncGroupSecretKey : ISync
    {
        public string Name => "GroupSecretKey";

        private readonly ISignInClientStore signInClientStore;
        private readonly ISerializer serializer;
        public SignInSyncGroupSecretKey(ISignInClientStore signInClientStore, ISerializer serializer)
        {
            this.signInClientStore = signInClientStore;
            this.serializer = serializer;
        }
        public Memory<byte> GetData()
        {
            return serializer.Serialize(signInClientStore.Group.Password);
        }

        public void SetData(Memory<byte> data)
        {
            signInClientStore.SetGroupPassword(serializer.Deserialize<string>(data.Span));
        }
    }
}
