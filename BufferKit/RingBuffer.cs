namespace BufferKit
{
    using System.Diagnostics.CodeAnalysis;

    using Cysharp.Threading.Tasks;

    using OneOf;

    public readonly struct RingBufferError : IBufferError
    {
        private readonly uint code_;

        private RingBufferError(uint code)
            => this.code_ = code;

        public Exception AsException()
            => new Exception(this.ToString());

        internal static readonly ReadOnlyMemory<string> NAMES = new ReadOnlyMemory<string>(["Idle", "Closed", "Incapable"]);

        internal static readonly RingBufferError Idle = new RingBufferError(0);

        public static readonly RingBufferError Closed = new RingBufferError(1);

        public static readonly RingBufferError Incapable = new RingBufferError(2);

        public static bool operator ==(RingBufferError lhs, RingBufferError rhs)
            => lhs.code_ == rhs.code_;

        public static bool operator !=(RingBufferError lhs, RingBufferError rhs)
            => lhs.code_ != rhs.code_;

        public override string ToString()
            => $"RingBufferError.{NAMES.Span[(int)this.code_]}";

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

    public sealed partial class RingBuffer<T> : IBuffer<T>
    {
        private readonly Memory<T> memory_;

        /// <summary>
        /// 控制写者并发数量上限为1
        /// </summary>
        private readonly SemaphoreSlim txSema_;

        /// <summary>
        /// 控制读者并发数量上限为1
        /// </summary>
        private readonly SemaphoreSlim rxSema_;

        /// <summary>
        /// 缓冲区最大容量
        /// </summary>
        private readonly uint capacity_;

        /// <summary>
        /// 用于唤醒等待的写者
        /// </summary>
        private OneOf<Demand, RingBufferError> txDemand_;

        /// <summary>
        /// 用于唤醒等待的读者
        /// </summary>
        private OneOf<Demand, RingBufferError> rxDemand_;

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
            var memory = new Memory<T>(new T[(int)capacity]);
            this.memory_ = memory;
            this.txSema_ = new SemaphoreSlim(1, 1);
            this.rxSema_ = new SemaphoreSlim(1, 1);
            this.txDemand_ = RingBufferError.Idle;
            this.rxDemand_ = RingBufferError.Idle;
            this.capacity_ = (uint)memory.Length;
            this.txPos_ = 0;
            this.rxPos_ = 0;
            this.inversed_ = false;
        }

        public OneOf<(BuffTx<T>, BuffRx<T>), RingBufferError> TrySplit(bool shouldCloseOnDispose = true)
        {
            if (this.IsTxClosed || this.IsRxClosed)
                return RingBufferError.Closed;

            var tx = this.CreateTx(shouldCloseOnDispose);
            var rx = this.CreateRx(shouldCloseOnDispose);
            return (tx, rx);
        }

        public uint Capacity
            => this.capacity_;

        public BuffRx<T> CreateRx(bool shouldCloseOnDispose = false)
            => new BuffRx<T>(this, shouldCloseOnDispose ? TrySetRxClosed : null);

        public BuffTx<T> CreateTx(bool shouldCloseOnDispose = false)
            => new BuffTx<T>(this, shouldCloseOnDispose ? TrySetTxClosed : null);

        /// <summary>
        /// 可供写者填充的数据量
        /// </summary>
        private uint WriterSize
        {
            get
            {
                lock (this)
                {
                    if (this.inversed_)
                        return this.rxPos_ - this.txPos_;
                    else
                        return this.capacity_ - this.txPos_;
                }
            }
        }

        /// <summary>
        /// 可供读者消费的数据量
        /// </summary>
        private uint ReaderSize
        {
            get
            {
                lock (this)
                {
                    if (this.inversed_)
                        return this.rxPos_ == this.txPos_ ? this.capacity_ : this.rxPos_ - this.txPos_;
                    else
                        return this.txPos_ - this.rxPos_;
                }
            }
        }

        /// <summary>
        /// 从内部缓冲区中借出一段可供消费者读取的已填充缓存，该缓存长度不小于给定的长度；如果执行完成，则返回该已填充缓存
        /// </summary>
        /// <param name="length"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async UniTask<OneOf<BuffSegmRef<T>, RingBufferError>> ReadAsync(uint length, CancellationToken token = default)
        {
            if (this.rxDemand_.TryPickT1(out var error, out var _) && error == RingBufferError.Closed)
                return error;
            if (length == 0)
                return new BuffSegmRef<T>(ReadOnlyMemory<T>.Empty);
            if (length > this.capacity_)
                return RingBufferError.Incapable;

            var succ = false;
            try
            {
                await this.rxSema_.WaitAsync(token);
                while (true)
                {
                    var readerSize = this.ReaderSize;
                    if (readerSize == 0 && this.IsTxClosed)
                        return RingBufferError.Closed;

                    if (readerSize > 0)
                    {
                        var sliceLen = Math.Min(length, readerSize);
                        var memory = this.memory_.Slice((int)this.rxPos_, (int)sliceLen);
                        var reclaim = new RingBuffReclaimRef(this);
                        succ = true;
                        return new BuffSegmRef<T>(reclaim, memory);
                    }
                    while (!succ)
                    {
                        if (this.rxDemand_.TryPickT0(out var demand, out error))
                        {
                            await demand.Signal.Task.AttachExternalCancellation(token);
                            break;
                        }
                        else
                        {
                            if (error != RingBufferError.Idle)
                                return error;

                            this.rxDemand_ = new Demand
                            {
                                Amount = length,
                                Signal = new UniTaskCompletionSource(),
                            };
                        }
                    }
                }
            }
            finally
            {
                if (succ && this.rxDemand_.IsT0)
                    this.rxDemand_ = RingBufferError.Idle;
                    
                if (!succ && this.rxSema_.CurrentCount == 0)
                    this.rxSema_.Release();
            }
        }

        /// <summary>
        /// 从内部缓冲区中借出一段未填充缓存，该缓存长度不小于给定的长度；如果执行完成，则返回该未填充缓存
        /// </summary>
        /// <param name="length"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async UniTask<OneOf<BuffSegmMut<T>, RingBufferError>> WriteAsync(uint length, CancellationToken token = default)
        {
            if (this.txDemand_.TryPickT1(out var error, out var _) && error == RingBufferError.Closed)
                return error;
            if (length == 0)
                return new BuffSegmMut<T>(Memory<T>.Empty);
            if (length > this.capacity_)
                return RingBufferError.Incapable;

            var succ = false;
            try
            {
                await this.txSema_.WaitAsync(token);
                while (true)
                {
                    var writerSize = this.WriterSize;
                    if (writerSize == 0 && this.IsRxClosed)
                        return RingBufferError.Closed;

                    if (writerSize > 0)
                    {
                        var sliceLen = Math.Min(length, writerSize);
                        var memory = this.memory_.Slice((int)this.txPos_, (int)sliceLen);
                        var reclaim = new RingBuffReclaimMut(this);
                        succ = true;
                        return new BuffSegmMut<T>(reclaim, memory);
                    }
                    while (!succ)
                    {
                        if (this.txDemand_.TryPickT0(out var demand, out error))
                        {
                            await demand.Signal.Task.AttachExternalCancellation(token);
                            break;
                        }
                        else
                        {
                            if (error != RingBufferError.Idle)
                                return error;

                            this.txDemand_ = new Demand
                            {
                                Amount = length,
                                Signal = new UniTaskCompletionSource(),
                            };
                        }
                    }
                }
            }
            finally
            {
                if (succ && this.txDemand_.IsT0)
                    this.txDemand_ = RingBufferError.Idle;

                if (!succ && this.txSema_.CurrentCount == 0)
                    this.txSema_.Release();
            }
        }

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

        async UniTask<OneOf<BuffSegmRef<T>, IBufferError>> IBuffer<T>.ReadAsync(uint length, CancellationToken token)
        {
            var r = await this.ReadAsync(length, token);
            return r.MapT1((e) => (IBufferError)e);
        }

        async UniTask<OneOf<BuffSegmMut<T>, IBufferError>> IBuffer<T>.WriteAsync(uint length, CancellationToken token)
        {
            var r = await this.WriteAsync(length, token);
            return r.MapT1((e) => (IBufferError)e);
        }

        static void TrySetRxClosed(IBuffer<T> buffer)
        {
            if (buffer is not RingBuffer<T> ringBuff)
                return;
            ringBuff.rxSema_.Wait();
            ringBuff.rxDemand_ = RingBufferError.Closed;
        }

        static void TrySetTxClosed(IBuffer<T> buffer)
        {
            if (buffer is not RingBuffer<T> ringBuff)
                return; 
            ringBuff.txSema_.Wait();
            ringBuff.txDemand_ = RingBufferError.Closed;
        }
    }

    public sealed partial class RingBuffer<T>
    {
        private readonly struct Demand
        {
            public uint Amount { get; init; }

            public UniTaskCompletionSource Signal { get; init; }
        }

        private readonly struct RingBuffReclaimRef : IReclaimRef<T>
        {
            private readonly RingBuffer<T> buffer_;

            public RingBuffReclaimRef(RingBuffer<T> buffer)
                => this.buffer_ = buffer;

            public void Reclaim(ReadOnlyMemory<T> mem, uint offset)
            {
                lock (this.buffer_)
                {
                    var newPos = this.buffer_.rxPos_ + offset;
#if DEBUG
                    if (newPos > this.buffer_.capacity_)
                        throw new ArgumentOutOfRangeException($"txPos({this.buffer_.txPos_}) + offset({offset}) > capacity({this.buffer_.capacity_})");
#endif
                    if (newPos == this.buffer_.capacity_)
                    {
                        if (!this.buffer_.inversed_)
                            throw new Exception("Illegal State");

                        this.buffer_.rxPos_ = 0;
                        this.buffer_.inversed_ = false;
                    }
                    else
                    {
                        if (newPos == this.buffer_.txPos_ && !this.buffer_.inversed_)
                        {
                            this.buffer_.txPos_ = 0;
                            this.buffer_.rxPos_ = 0;
                        }
                        else
                        {
                            this.buffer_.rxPos_ = newPos;
                        }
                    }
                }
                if (this.buffer_.txDemand_.TryPickT0(out var demand, out var _))
                {
                    this.buffer_.txDemand_ = RingBufferError.Idle;
                    demand.Signal.TrySetResult();
                }
                this.buffer_.rxSema_.Release();
            }
        }

        private readonly struct RingBuffReclaimMut : IReclaimMut<T>
        {
            private readonly RingBuffer<T> buffer_;

            public RingBuffReclaimMut(RingBuffer<T> buffer)
                => this.buffer_ = buffer;

            public void Reclaim(Memory<T> mem, uint offset)
            {
                lock (this.buffer_)
                {
                    var newPos = this.buffer_.txPos_ + offset;
#if DEBUG
                    if (newPos > this.buffer_.capacity_)
                        throw new ArgumentOutOfRangeException($"txPos({this.buffer_.txPos_}) + offset({offset}) > capacity({this.buffer_.capacity_})");
#endif
                    if (newPos == this.buffer_.capacity_)
                    {
                        this.buffer_.txPos_ = 0;
                        this.buffer_.inversed_ = true;
                    }
                    else
                    {
                        this.buffer_.txPos_ = newPos;
                    }
                }
                if (this.buffer_.rxDemand_.TryPickT0(out var demand, out var _))
                {
                    this.buffer_.rxDemand_ = RingBufferError.Idle;
                    demand.Signal.TrySetResult();
                }
                this.buffer_.txSema_.Release();
            }
        }
    }
}
