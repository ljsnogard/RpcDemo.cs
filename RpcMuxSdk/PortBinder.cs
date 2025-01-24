namespace RpcMuxSdk
{
    using Cysharp.Threading.Tasks;

    using OneOf;

    public sealed class PortBinder
    {
        public ushort LocalPort { get; init; }

        internal SimpleMux Mux { get; init; }

        public UniTask<Listener> GetListenerAsync(CancellationToken token = default)
            => throw new NotImplementedException();

        public UniTask<Telegraph> GetTelegraphAsync(CancellationToken token = default)
            => throw new NotImplementedException();

        public UniTask<Channel> GetChannelAsync(CancellationToken token = default)
            => throw new NotImplementedException();
    }
}
