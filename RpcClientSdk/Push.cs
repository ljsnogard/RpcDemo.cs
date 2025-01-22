namespace RpcClientSdk
{
    using BufferKit;

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
            ( IAsyncEnumerable<Header> headers
            , BuffRx<byte> payloadReader
            , CancellationToken token = default);
    }
}
