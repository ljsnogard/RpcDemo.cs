namespace RpcMuxSdk
{
    using BufferKit;

    using Cysharp.Threading.Tasks;

    using OneOf;

    using System.Diagnostics;

    internal readonly struct ChannelId: IEquatable<ChannelId>, IComparable<ChannelId>
    {
        public ushort LocalPort { get; init; }
        public ushort RemotePort { get; init; }

        public bool Equals(ChannelId other)
            => this.LocalPort == other.LocalPort && this.RemotePort == other.RemotePort;

        public int CompareTo(ChannelId other)
        {
            var a = this.RemotePort.CompareTo(other.RemotePort);
            if (a == 0)
                return this.LocalPort.CompareTo(other.LocalPort);
            else
                return a;
        }

        public override int GetHashCode()
            => HashCode.Combine(this.LocalPort, this.RemotePort);
    }

    public readonly struct ChannelError
    {
        public OneOf<RingBufferError, BuffIoError, BuffSegmError> InnerError { get; init; }
    }

    public sealed class Channel
    {
        /// <summary>
        /// Mux 的 Tx
        /// </summary>
        private readonly BuffTx<byte> tx_;

        private readonly RingBuffer<byte> txBuff_;

        /// <summary>
        /// 从 Mux 中接收的数据的缓冲，已去除头部信息，即只有报文中的 Payload 部分
        /// </summary>
        private readonly RingBuffer<byte> rxBuff_;

        private readonly ushort localPort_;

        private readonly ushort remotePort_;

        internal BuffTx<byte> RxWriter
            => this.rxBuff_.CreateTx(false);

        public Channel
            ( BuffTx<byte> tx
            , uint txBuffSize
            , uint rxBuffSize
            , ushort localPort_
            , ushort remotePort_)
        {
            this.tx_ = tx;
            this.txBuff_ = new RingBuffer<byte>(txBuffSize);
            this.rxBuff_ = new RingBuffer<byte>(rxBuffSize);
            this.localPort_ = localPort_;
            this.remotePort_ = remotePort_;
        }

        /// <summary>
        /// 发送任意长度的数据，此方法会自动将数据切分为数据包发送
        /// </summary>
        /// <param name="data"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        /// <remarks>
        /// <para>因为这只是一个 Demo, 简单起见的实现, 因此具有包括但不仅限于如下的隐患: </para>
        /// <para>1. 如果 data 的数据流中断, 有可能引起其他 channel 的数据失效; </para>
        /// <para>2. 如果接收方数据拥堵或者网络中断, 发送方无法知悉, 也无法实现重发; </para>
        /// <para>3. 如果发送方故障导致没有正确发送末段数据, 接收方将只能死等; </para>
        /// <para>4. 数据切分成数据包的大小完全取决于 data 取出的大小, 有可能有效荷载太小导致传输效率低; </para>
        /// </remarks>
        public UniTask<OneOf<uint, ChannelError>> SendAsync(BuffRx<byte> data, CancellationToken token = default)
            => throw new NotImplementedException();

        public async UniTask<OneOf<ReadOnlyMemory<ReaderBuffSegm<byte>>, ChannelError>> RecvAsync(uint length, CancellationToken token = default)
        {
            var result = await this.rxBuff_.ReadAsync(length, token);
            return result.MapT1((e) => new ChannelError { InnerError = e });
        }
    }
}
