namespace RpcMuxSdk
{
    using System.Collections.Generic;

    using BufferKit;

    using Cysharp.Threading.Tasks;

    using DotNext.Threading; // To use Atomic<ushort>

    using OneOf;

    public readonly struct MuxError
    { }

    public readonly struct Descriptor
    {

    }

    /// <summary>
    /// 用于在一个全双工可靠传输信道上同时进行多个活动会话的连接
    /// </summary>
    public sealed partial class SimpleMux
    {
        /// <summary>
        /// 为了简化问题作出的每个数据包最大长度限制，会话数据缓冲会参考这个数值
        /// </summary>
        public static readonly uint MAX_PACKET_SIZE = 512;

        /// <summary>
        /// 为了简化问题规定每个数据报文的头部长度为8字节，分别是 2字节报文类型, 2字节报文长度, 2字节 local port, 2字节 remote port
        /// </summary>
        public static readonly uint PACKET_HEADER_SIZE = 8;

        /// <summary>
        /// 数据包最大长度和头部长度推算出的数据包荷载长度
        /// </summary>
        public static readonly uint MAX_PAYLOAD_SIZE = MAX_PACKET_SIZE - PACKET_HEADER_SIZE;

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

        public UniTask<OneOf<PortBinder, MuxError>> BindAsync(ushort port, CancellationToken token = default)
            => throw new NotImplementedException();

        public UniTask<OneOf<Channel, MuxError>> AcceptAsync(Descriptor descriptor, CancellationToken token = default)
            => throw new NotImplementedException();

        public UniTask<OneOf<BuffTx<byte>, MuxError>> RejectAsync(Descriptor descriptor, CancellationToken token = default)
            => throw new NotImplementedException();
    }

    public sealed partial class SimpleMux
    {
        public readonly struct PacketHeader
        {
            public ushort Flags { get; init; }
            public ushort PayloadSize { get; init; }
            public ushort LocalPort { get; init; }
            public ushort RemotePort {  get; init; }

            //public static readonly byte K_FLAGS_PACK_NORM = 0b_0000_0000;
            public static readonly byte K_FLAGS_PACK_HEAD = 0b_0000_0001;
            public static readonly byte K_FLAGS_PACK_TAIL = 0b_0000_0010;
            //public static readonly byte K_FLAGS_PACK_ONCE = 0b_0000_0011;

            public static void WriteU16BE(ushort value, Span<byte> buffer)
            {
                var msp = value >> 8;
                var lsp = value - (msp << 8);

                buffer[0] = (byte)(msp >> 8);
                buffer[1] = (byte)lsp;
            }

            public static ushort ReadU16BE(ReadOnlySpan<byte> buffer)
            {
                var msp = buffer[0] << 8;
                var lsp = buffer[1];
                return (ushort)(msp + lsp);
            }

            public static PacketHeader ReadBigEndianBytes(ReadOnlySpan<byte> buffer)
            {
                var flags = PacketHeader.ReadU16BE(buffer);
                var payloadSize = PacketHeader.ReadU16BE(buffer.Slice(2));
                var localPort = PacketHeader.ReadU16BE(buffer.Slice(4));
                var remotePort = PacketHeader.ReadU16BE(buffer.Slice(6));
                return new PacketHeader
                {
                    Flags = flags,
                    PayloadSize = payloadSize,
                    LocalPort = localPort,
                    RemotePort = remotePort
                };
            }

            public void WriteBigEndianBytes(Span<byte> buffer)
            {
                PacketHeader.WriteU16BE(this.Flags, buffer);
                PacketHeader.WriteU16BE(this.PayloadSize, buffer.Slice(2));
                PacketHeader.WriteU16BE(this.LocalPort, buffer.Slice(4));
                PacketHeader.WriteU16BE(this.RemotePort, buffer.Slice(6));
            }
        }
    }
}
