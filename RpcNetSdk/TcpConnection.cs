namespace RpcNetSdk
{
    using System.Collections.Concurrent;
    using System.Net.Sockets;

    using Cysharp.Threading.Tasks;

    using DotNext.Threading;

    using OneOf;

    public sealed class TcpConnection: IConnection
    {
        /// <summary>
        /// 为了简化问题作出的每个数据包最大长度限制，会话数据缓冲会参考这个数值
        /// </summary>
        public static readonly uint MAX_PACKET_SIZE = 512;

        /// <summary>
        /// 通信所用的 Tcp Socket
        /// </summary>
        private readonly Socket socket_;

        /// <summary>
        /// 活跃 Stream 的查询字典
        /// </summary>
        private readonly ConcurrentDictionary<uint, Stream> activeStreams_;

        /// <summary>
        /// 闲置 Stream 对象的先进先出队列
        /// </summary>
        private readonly ConcurrentQueue<Stream> idleStreams_;

        /// <summary>
        /// 闲置 Stream ID 的先进先出队列，用于会话号重用
        /// </summary>
        private readonly ConcurrentQueue<uint> idleIdsQueue_;

        /// <summary>
        /// 当前连接中使用到的最大的流ID
        /// </summary>
        private readonly Atomic<uint> maxStreamId_;

        public TcpConnection(Socket socket)
        {
            this.socket_ = socket;
            this.activeStreams_ = new ConcurrentDictionary<uint, Stream>();
            this.idleStreams_ = new ConcurrentQueue<Stream>();
            this.idleIdsQueue_ = new ConcurrentQueue<uint>();
            this.maxStreamId_ = new Atomic<uint>();
        }

        public UniTask<OneOf<uint, ConnectionError>> SendAsync(BuffRx data, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public UniTask<OneOf<BuffRx, ConnectionError>> RecvAsync(CancellationToken token = default)
        {
            throw new NotImplementedException();
        }
    }

    internal sealed class Stream
    {
        private readonly Memory<byte> buffer_;

        private uint offset_;
    }
}
