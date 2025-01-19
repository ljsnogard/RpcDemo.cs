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
            => throw new NotImplementedException();

        internal BuffTx<byte> RxWriter
            => throw new NotImplementedException();

        public Channel()
        {
            this.tx_ = new RingBuffer<byte>(new byte[Connection.MAX_PACKET_SIZE * 2]);
            this.rx_ = new RingBuffer<byte>(new byte[Connection.MAX_PACKET_SIZE * 2]);
        }

        public UniTask<OneOf<uint, ConnectionError>> SendAsync(BuffRx<byte> data, CancellationToken token = default)
            => throw new NotImplementedException();

        public UniTask<OneOf<BuffRx<byte>, ConnectionError>> RecvAsync(CancellationToken token = default)
            => throw new NotImplementedException();
    }
}
