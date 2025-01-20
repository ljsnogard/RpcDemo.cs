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
    public sealed class SimpleMux
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
        /// Local Port Keyed dictionary
        /// </summary>
        private readonly SortedDictionary<ushort, OneOf<Channel, Hub>> localPortsDict_;

        /// <summary>
        /// 闲置 port 的先进先出队列，用于重用
        /// </summary>
        private readonly List<ushort> localPortsQueue_;

        /// <summary>
        /// 当前连接中使用到的最大的 port
        /// </summary>
        private readonly Atomic<ushort> maxPort_;

        public SimpleMux(RingBuffer<byte> tx, RingBuffer<byte> rx)
        {
            this.tx_ = tx;
            this.rx_ = rx;
            this.localPortsDict_ = new SortedDictionary<ushort, OneOf<Channel, Hub>>();
            this.localPortsQueue_ = new List<ushort>();
            this.maxPort_ = new Atomic<ushort>();
        }
    }
}
