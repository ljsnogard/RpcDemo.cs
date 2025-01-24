namespace RpcMuxSdk
{
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;

    using BufferKit;

    using Cysharp.Threading.Tasks;

    using DotNext.Threading; // To use Atomic<ushort>

    using OneOf;

    public readonly struct Port: IComparable<Port>, IEquatable<Port>
    {
        public readonly UInt32 code;

        public Port(UInt32 code)
            => this.code = code;

        public int CompareTo(Port other)
            => this.code.CompareTo(other.code);

        public bool TryGetU16PortCode(out UInt16 code)
        {
            if (this.code <= UInt16.MaxValue)
            {
                code = (UInt16)this.code;
                return true;
            }
            code = default;
            return false;
        }

        public bool Equals(Port other)
            => this.code == other.code;

        public override string ToString()
            => $"Port({this.code})";

        public override int GetHashCode()
            => HashCode.Combine(typeof(Port), this.code);

        public override bool Equals([NotNullWhen(true)] object? obj)
            => obj is Port port && port.code == this.code;

        public static bool operator == (Port lhs, Port rhs)
            => lhs.code == rhs.code;

        public static bool operator != (Port lhs, Port rhs)
            => lhs.code != rhs.code;
    }

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
        private readonly SortedDictionary<Port, OneOf<Channel, Hub>> localPortsDict_;

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
            this.localPortsDict_ = new SortedDictionary<Port, OneOf<Channel, Hub>>();
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
        internal readonly struct PacketHeader
        {
            public byte Flags { get; init; }
            public uint PayloadSize { get; init; }
            public Port SrcPort { get; init; }
            public Port DstPort { get; init; }

            public uint Sequence { get; init; }

            public ushort ExtraHeaderSize { get; init; }

            /// <summary>
            /// 指示报文中表示发送端 port 要使用的数据类型, 0双字节, 1四字节
            /// </summary>
            public static readonly byte K_B0_SRCPORT_REPR = 0b_0000_0001;

            /// <summary>
            /// 指示报文中表示接收端 port 要使用的数据类型, 0双字节, 1四字节
            /// </summary>
            public static readonly byte K_B0_DSTPORT_REPR = 0b_0000_0010;
            
            /// <summary>
            /// 指示报文中表示荷载长度要使用的数据类型, 0双字节, 1四字节
            /// </summary>
            public static readonly byte K_B0_PAYLOAD_REPR = 0b_0000_0100;

            /// <summary>
            /// 指示报文中表示报文序列号的数据类型, 0双字节, 1四字节
            /// </summary>
            public static readonly byte K_B0_SEQSIZE_REPR = 0b_0000_1000;

            /// <summary>
            /// 指示本数据报荷载是数据或是信令, 若为信令则置1
            /// </summary>
            public static readonly byte K_B0_CHANNEL_SIGN = 0b_0001_0000;

            /// <summary>
            /// 指示本数据报是否包含附加头部的长度, 如无附加头部长度则为 0
            /// </summary>
            public static readonly byte K_B0_EXTRA_HEADER = 0b_1100_0000;

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

            public static async UniTask<OneOf<PacketHeader, IBufferError>> PeekHeaderAsync(BuffRx<byte> rx, CancellationToken token = default)
            {
                const uint FLAGS_SIZE = 1;
                var maybeFlags = await rx.PeekAsync(0, token);
                if (!maybeFlags.TryPickT0(out var flagsSegm, out var error))
                    return OneOf<PacketHeader, IBufferError>.FromT1(error);

                throw new NotImplementedException();
            }
        }
    }
}
