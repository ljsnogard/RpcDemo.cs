namespace RpcClientSdk
{
    using Cysharp.Threading.Tasks;

    using OneOf;

    using RpcNetSdk;
    using BufferKit;

    public readonly struct ClientError
    {

    }

    public sealed class Client
    {
        /// <summary>
        /// 发送请求
        /// </summary>
        /// <param name="location">所请求执行的程序的位置 Uri 表示</param>
        /// <param name="headers">零个或多个请求头数据</param>
        /// <param name="payload">请求体自身的数据</param>
        /// <returns></returns>
        public UniTask<OneOf<Response, ClientError>> Request
            ( Uri location
            , IAsyncEnumerable<HeaderRx> headers
            , BuffRx<byte> payload)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// 拉取数据流
        /// </summary>
        /// <param name="location"></param>
        /// <returns></returns>
        public UniTask<OneOf<IPullAgent, ClientError>> Pull(Uri location)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// 推送数据流
        /// </summary>
        /// <param name="location"></param>
        /// <returns></returns>
        public UniTask<OneOf<IPushAgent, ClientError>> Push(Uri location)
        {
            throw new NotImplementedException();
        }
    }
}
