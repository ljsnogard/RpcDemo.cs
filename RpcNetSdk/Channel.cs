namespace RpcNetSdk
{
    using BufferKit;

    using Cysharp.Threading.Tasks;

    using OneOf;

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

    public sealed class Channel
    {
        private readonly RingBuffer<byte> tx_;

        private readonly RingBuffer<byte> rx_;

        internal BuffRx<byte> TxReader
            => this.tx_.CreateRx(true);

        internal BuffTx<byte> RxWriter
            => this.rx_.CreateTx(true);

        public Channel(uint txBuffSize, uint rxBuffSize)
        {
            this.tx_ = new RingBuffer<byte>(txBuffSize);
            this.rx_ = new RingBuffer<byte>(rxBuffSize);
        }

        public UniTask<OneOf<uint, ConnectionError>> SendAsync(BuffRx<byte> data, CancellationToken token = default)
            => throw new NotImplementedException();

        public UniTask<OneOf<BuffRx<byte>, ConnectionError>> RecvAsync(CancellationToken token = default)
            => throw new NotImplementedException();
    }
}
