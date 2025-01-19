namespace RpcNetSdk
{
    using System.Collections.Generic;

    using BufferKit;

    using Cysharp.Threading.Tasks;

    using DotNext.Threading;

    using OneOf;

    public readonly struct ConnectionError
    { }

    /// <summary>
    /// 用于在一个全双工可靠传输信道上同时进行多个活动会话的连接
    /// </summary>
    public sealed class Connection
    {
        /// <summary>
        /// 为了简化问题作出的每个数据包最大长度限制，会话数据缓冲会参考这个数值
        /// </summary>
        public static readonly uint MAX_PACKET_SIZE = 512;

        /// <summary>
        /// 已编码的外送数据缓冲管道
        /// </summary>
        private readonly RingBuffer<byte> tx_;

        /// <summary>
        /// 接收数据并解码的缓冲管道
        /// </summary>
        private readonly RingBuffer<byte> rx_;

        /// <summary>
        /// 已连接的 channel
        /// </summary>
        private readonly SortedDictionary<ChannelId, Channel> activeChannels_;

        /// <summary>
        /// 半连接的 channel 
        /// </summary>
        private readonly SortedDictionary<ChannelId, Channel> pendingChannels_;

        /// <summary>
        /// 闲置 port 的先进先出队列，用于会话号重用
        /// </summary>
        private readonly List<ushort> portsQueue_;

        /// <summary>
        /// 当前连接中使用到的最大的 port
        /// </summary>
        private readonly Atomic<ushort> maxPort_;

        public Connection(RingBuffer<byte> tx, RingBuffer<byte> rx)
        {
            this.tx_ = tx;
            this.rx_ = rx;
            this.activeChannels_ = new SortedDictionary<ChannelId, Channel>();
            this.pendingChannels_ = new SortedDictionary<ChannelId, Channel>();
            this.portsQueue_ = new List<ushort>();
            this.maxPort_ = new Atomic<ushort>();
        }

        public async UniTask<OneOf<Channel, ConnectionError>> GetChannelAsync(ushort port, CancellationToken token = default)
        {
            using var readLockHolder = await this.pendingChannels_.AcquireReadLockAsync(token);
            throw new NotImplementedException();
        }
    }
}
