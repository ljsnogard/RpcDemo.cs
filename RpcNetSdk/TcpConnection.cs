namespace RpcNetSdk
{
    using System.Collections.Concurrent;
    using System.Net.Sockets;

    using Cysharp.Threading.Tasks;

    using OneOf;

    public sealed class TcpConnection: IConnection
    {
        public static readonly uint MAX_PACKET_SIZE = 512;

        private readonly TcpClient tcpClient_;

        private readonly ConcurrentDictionary<uint, Session> sessionDict_;

        /// <summary>
        /// 
        /// </summary>
        private readonly ConcurrentQueue<Session> sessionsQueue_;

        public TcpConnection(TcpClient tcpClient)
        {
            this.tcpClient_ = tcpClient;
            this.sessionDict_ = new ConcurrentDictionary<uint, Session>();
            this.sessionsQueue_ = new ConcurrentQueue<Session>();
        }

        public UniTask<OneOf<uint, ConnectionError>> SendAsync
            ( BuffRx data
            , CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public UniTask<OneOf<BuffRx, ConnectionError>> RecvAsync(CancellationToken token = default)
        {
            throw new NotImplementedException();
        }
    }

    public sealed class Session
    {
        public readonly uint Id;
        internal readonly Memory<byte> buffer_;
    }
}
