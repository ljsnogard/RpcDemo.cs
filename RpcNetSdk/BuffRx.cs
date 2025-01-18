namespace RpcNetSdk
{
    using Cysharp.Threading.Tasks;
    using OneOf;

    public readonly struct BuffRxrror
    { }

    public struct BuffRx
    {
        public UniTask<OneOf<ReadOnlyMemory<byte>, BuffRxrror>> ReadAsync
            ( uint length
            , CancellationToken token = default)
        {
            throw new NotImplementedException();
        }
    }
}
