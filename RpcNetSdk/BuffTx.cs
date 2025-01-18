namespace RpcNetSdk
{
    using Cysharp.Threading.Tasks;
    using OneOf;

    public readonly struct BuffTxError
    { }

    public struct BuffTx
    {
        public UniTask<OneOf<Memory<byte>, BuffTxError>> WriteAsync
            ( uint length
            , CancellationToken token = default)
        {
            throw new NotImplementedException();
        }
    }
}
