namespace RpcClientSdk
{
    using Cysharp.Threading.Tasks;

    using OneOf;
    using OneOf.Types;

    using RpcNetSdk;

    public readonly struct PushError
    { }

    public interface IPushAgent
    {
        public Uri Location { get; }

        public UniTask<OneOf<PushError, None>> SendItemAsync
            (IAsyncEnumerable<(string, BuffRx)> headers
            , BuffRx payload
            , CancellationToken token = default);
    }
}
