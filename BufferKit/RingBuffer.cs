namespace BufferKit
{
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks;

    using Cysharp.Threading.Tasks;

    using OneOf;

    internal readonly struct SliceInfo
    {
        public uint Offset { get; init; }
        public uint Length { get; init; }
    }

    internal readonly struct Demand
    {
        public uint Amount { get; init; }
        public TaskCompletionSource<SliceInfo> Signal { get; init; }
    }

    public readonly struct RingBufferError : IBufferError
    {
        private readonly uint code_;

        private RingBufferError(uint code)
            => this.code_ = code;

        internal static readonly RingBufferError Idle = new RingBufferError(0);

        public static readonly RingBufferError Closed = new RingBufferError(1);

        public static bool operator ==(RingBufferError lhs, RingBufferError rhs)
            => lhs.code_ == rhs.code_;

        public static bool operator !=(RingBufferError lhs, RingBufferError rhs)
            => lhs.code_ != rhs.code_;

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (obj is RingBufferError other)
                return this.code_ == other.code_;
            else
                return false;
        }

        public override int GetHashCode()
            => HashCode.Combine(typeof(RingBufferError), this.code_);
    }

    public sealed class RingBuffer<T> : IBufferInternal<T>
    {
        private readonly T[] memory_;

        private readonly ReaderWriterLockSlim txRwlock_;

        private readonly ReaderWriterLockSlim rxRwlock_;

        private OneOf<Demand, RingBufferError> txDemand_;

        private OneOf<Demand, RingBufferError> rxDemand_;

        /// <summary>
        /// 缓冲区最大容量
        /// </summary>
        private readonly uint capacity_;

        /// <summary>
        /// 写者位置到缓冲区头部的单位数量
        /// </summary>
        private uint txPos_;

        /// <summary>
        /// 读者位置到缓冲区头部的单位数量
        /// </summary>
        private uint rxPos_;

        /// <summary>
        /// 是否处于读者写者位置反转状态
        /// </summary>
        private bool inversed_;

        public RingBuffer(uint capacity)
        {
            this.memory_ = new T[(int)capacity];
            this.txRwlock_ = new ReaderWriterLockSlim();
            this.rxRwlock_ = new ReaderWriterLockSlim();
            this.txDemand_ = RingBufferError.Idle;
            this.rxDemand_ = RingBufferError.Idle;
            this.capacity_ = capacity;
            this.txPos_ = 0;
            this.rxPos_ = 0;
            this.inversed_ = false;
        }

        public uint Capacity => this.capacity_;

        /// <summary>
        /// 可供写者填充的数据量
        /// </summary>
        public uint WriterSize
        {
            get
            {
                if (this.inversed_)
                    return this.rxPos_ - this.txPos_;
                else
                    return this.capacity_ - this.txPos_ + this.rxPos_;
            }
        }

        /// <summary>
        /// 可供读者消费的数据量
        /// </summary>
        public uint ReaderSize
        {
            get
            {
                if (this.inversed_)
                    return this.rxPos_ - this.txPos_;
                else
                    return this.txPos_ - this.rxPos_;
            }
        }

        /// <summary>
        /// 从内部缓冲区中借出一段可供消费者读取的已填充缓存，该缓存长度不小于给定的长度；如果执行完成，则返回该已填充缓存
        /// </summary>
        /// <param name="length"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public UniTask<OneOf<BuffSegmRef<T>, RingBufferError>> ReadAsync(uint length, CancellationToken token = default)
            => throw new NotImplementedException();

        /// <summary>
        /// 从内部缓冲区中借出一段未填充缓存，该缓存长度不小于给定的长度；如果执行完成，则返回该未填充缓存
        /// </summary>
        /// <param name="length"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public UniTask<OneOf<BuffSegmMut<T>, RingBufferError>> WriteAsync(uint length, CancellationToken token = default)
            => throw new NotImplementedException();

        public bool IsRxClosed
        {
            get
            {
                if (this.rxDemand_.TryPickT0(out var _, out var err))
                    return false;
                return err == RingBufferError.Closed;
            }
        }

        public bool IsTxClosed
        {
            get
            {
                if (this.txDemand_.TryPickT0(out var _, out var err))
                    return false;
                return err == RingBufferError.Closed;
            }
        }

        UniTask<OneOf<BuffSegmRef<T>, IBufferError>> IBuffer<T>.ReadAsync(uint length, CancellationToken token)
            => throw new NotImplementedException();

        UniTask<OneOf<BuffSegmMut<T>, IBufferError>> IBuffer<T>.WriteAsync(uint length, CancellationToken token)
            => throw new NotImplementedException();

        void IBufferInternal<T>.TrySetRxClosed()
            => throw new NotImplementedException();

        void IBufferInternal<T>.TrySetTxClosed()
            => throw new NotImplementedException();
    }
}
