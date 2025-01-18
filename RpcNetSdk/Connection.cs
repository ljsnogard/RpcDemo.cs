namespace RpcNetSdk
{
    using Cysharp.Threading.Tasks;
    using OneOf;

    public readonly struct ConnectionError
    { }

    public interface IConnection
    {
        /// <summary>
        /// 在连接中发送一份完整的数据，如果全部数据发送成功，返回发送的字节数，否则返回错误
        /// </summary>
        /// <param name="data"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public UniTask<OneOf<uint, ConnectionError>> SendAsync
            (BuffRx data
            , CancellationToken token = default);

        /// <summary>
        /// 从连接中接收一份完整的数据，如果可以开始接收，返回一个数据读取对象，否则返回错误
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public UniTask<OneOf<BuffRx, ConnectionError>> RecvAsync(CancellationToken token = default);
    }
}
