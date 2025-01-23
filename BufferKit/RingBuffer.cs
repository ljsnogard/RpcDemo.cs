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
        private readonly SemaphoreSlim writerSema_;

        /// <summary>
        /// 控制读者并发数量上限为1
        /// </summary>
        private readonly SemaphoreSlim readerSema_;

        /// <summary>
        /// 缓冲区最大容量
        /// </summary>
        private readonly uint capacity_;

        /// <summary>
        /// 用于唤醒等待的写者
        /// </summary>
        private OneOf<Demand, RingBufferError> writerDemand_;

        /// <summary>
        /// 用于唤醒等待的读者
        /// </summary>
        private OneOf<Demand, RingBufferError> readerDemand_;

        /// <summary>
        /// 写者位置到缓冲区头部的单位数量
        /// </summary>
        private uint writerOffset_;

        /// <summary>
        /// 读者位置到缓冲区头部的单位数量
        /// </summary>
        private uint readerOffset_;

        /// <summary>
        /// 是否处于读者写者位置反转状态
        /// </summary>
        private bool inversed_;

        public RingBuffer(uint capacity)
        {
            var memory = new Memory<T>(new T[(int)capacity]);
            this.memory_ = memory;
            this.writerSema_ = new SemaphoreSlim(1, 1);
            this.readerSema_ = new SemaphoreSlim(1, 1);
            this.writerDemand_ = RingBufferError.Idle;
            this.readerDemand_ = RingBufferError.Idle;
            this.capacity_ = (uint)memory.Length;
            this.writerOffset_ = 0;
            this.readerOffset_ = 0;
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
        public uint WriterSize
        {
            get
            {
                lock (this)
                {
                    if (this.inversed_)
                        return this.readerOffset_ - this.writerOffset_;
                    else
                        return this.capacity_ - this.writerOffset_;
                }
            }
        }

        /// <summary>
        /// 可供读者消费的数据量
        /// </summary>
        public uint ReaderSize
        {
            get
            {
                lock (this)
                {
                    if (this.inversed_)
                        return this.readerOffset_ == this.writerOffset_ ? this.capacity_ : this.readerOffset_ - this.writerOffset_;
                    else
                        return this.writerOffset_ - this.readerOffset_;
                }
            }
        }

        /// <summary>
        /// 从内部缓冲区中借出一段可供消费者读取的已填充缓存，该缓存长度不小于给定的长度；如果执行完成，则返回该已填充缓存
        /// </summary>
        /// <param name="length"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async UniTask<OneOf<ReaderBuffSegm<T>, RingBufferError>> ReadAsync(uint length, CancellationToken token = default)
        {
            if (this.readerDemand_.TryPickT1(out var error, out var _) && error == RingBufferError.Closed)
                return error;
            if (length == 0)
                return new ReaderBuffSegm<T>(ReadOnlyMemory<T>.Empty);
            var succ = false;
            try
            {
                await this.readerSema_.WaitAsync(token);
                while (true)
                {
                    var readerSize = this.ReaderSize;
                    if (readerSize == 0 && this.IsTxClosed)
                        return RingBufferError.Closed;

                    if (readerSize > 0)
                    {
                        var sliceLen = Math.Min(length, readerSize);
                        var memory = this.memory_.Slice((int)this.readerOffset_, (int)sliceLen);
                        var reclaim = new ReclaimReaderBuffSegm(this);
                        succ = true;
                        return new ReaderBuffSegm<T>(reclaim, memory);
                    }
                    Demand demand;
                    lock (this)
                    {
                        while (true)
                        {
                            if (this.readerDemand_.TryPickT0(out demand, out error))
                            {
                                break;
                            }
                            else
                            {
                                if (error != RingBufferError.Idle)
                                    return error;
                                this.readerDemand_ = new Demand
                                {
                                    Amount = length,
                                    Signal = new UniTaskCompletionSource(),
                                };
                            }
                        }
                    }
                    await demand.Signal.Task.AttachExternalCancellation(token);
                }
            }
            finally
            {
                if (succ && this.readerDemand_.IsT0)
                    this.readerDemand_ = RingBufferError.Idle;
                    
                if (!succ && this.readerSema_.CurrentCount == 0)
                    this.readerSema_.Release();
            }
        }

        /// <summary>
        /// 从内部缓冲区中借出一段未填充缓存，该缓存长度不小于给定的长度；如果执行完成，则返回该未填充缓存
        /// </summary>
        /// <param name="length"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async UniTask<OneOf<WriterBuffSegm<T>, RingBufferError>> WriteAsync(uint length, CancellationToken token = default)
        {
            if (this.writerDemand_.TryPickT1(out var error, out var _) && error == RingBufferError.Closed)
                return error;
            if (length == 0)
                return new WriterBuffSegm<T>(Memory<T>.Empty);
            var succ = false;
            try
            {
                await this.writerSema_.WaitAsync(token);
                while (true)
                {
                    var writerSize = this.WriterSize;
                    if (writerSize == 0 && this.IsRxClosed)
                        return RingBufferError.Closed;

                    if (writerSize > 0)
                    {
                        var sliceLen = Math.Min(length, writerSize);
                        var memory = this.memory_.Slice((int)this.writerOffset_, (int)sliceLen);
                        var reclaim = new ReclaimWriterBuffSegm(this);
                        succ = true;
                        return new WriterBuffSegm<T>(reclaim, memory);
                    }
                    Demand demand;
                    lock (this)
                    {
                        while (true)
                        {
                            if (this.writerDemand_.TryPickT0(out demand, out error))
                            {
                                break;
                            }
                            else
                            {
                                if (error != RingBufferError.Idle)
                                    return error;
                                this.writerDemand_ = new Demand
                                {
                                    Amount = length,
                                    Signal = new UniTaskCompletionSource(),
                                };
                            }
                        }
                    }
                    await demand.Signal.Task.AttachExternalCancellation(token);
                }
            }
            finally
            {
                if (succ && this.writerDemand_.IsT0)
                    this.writerDemand_ = RingBufferError.Idle;

                if (!succ && this.writerSema_.CurrentCount == 0)
                    this.writerSema_.Release();
            }
        }

        public bool IsRxClosed
        {
            get
            {
                if (this.readerDemand_.TryPickT0(out var _, out var err))
                    return false;
                return err == RingBufferError.Closed;
            }
        }

        public bool IsTxClosed
        {
            get
            {
                if (this.writerDemand_.TryPickT0(out var _, out var err))
                    return false;
                return err == RingBufferError.Closed;
            }
        }

        async UniTask<OneOf<ReaderBuffSegm<T>, IBufferError>> IBuffer<T>.ReadAsync(uint length, CancellationToken token)
        {
            var r = await this.ReadAsync(length, token);
            return r.MapT1((e) => (IBufferError)e);
        }

        async UniTask<OneOf<WriterBuffSegm<T>, IBufferError>> IBuffer<T>.WriteAsync(uint length, CancellationToken token)
        {
            var r = await this.WriteAsync(length, token);
            return r.MapT1((e) => (IBufferError)e);
        }

        static void TrySetRxClosed(IBuffer<T> buffer)
        {
            if (buffer is not RingBuffer<T> ringBuff)
                return;
            ringBuff.readerSema_.Wait();
            ringBuff.readerDemand_ = RingBufferError.Closed;
        }

        static void TrySetTxClosed(IBuffer<T> buffer)
        {
            if (buffer is not RingBuffer<T> ringBuff)
                return; 
            ringBuff.writerSema_.Wait();
            ringBuff.writerDemand_ = RingBufferError.Closed;
        }
    }

    public sealed partial class RingBuffer<T>
    {
        private readonly struct Demand
        {
            public uint Amount { get; init; }

            public UniTaskCompletionSource Signal { get; init; }
        }

        private readonly struct ReclaimReaderBuffSegm : IReclaimReaderBuffSegm<T>
        {
            private readonly RingBuffer<T> buffer_;

            public ReclaimReaderBuffSegm(RingBuffer<T> buffer)
                => this.buffer_ = buffer;

            public void Reclaim(ReadOnlyMemory<T> mem, uint offset)
            {
                UniTaskCompletionSource? signal = null;
                lock (this.buffer_)
                {
                    var newPos = this.buffer_.readerOffset_ + offset;
#if DEBUG
                    if (newPos > this.buffer_.capacity_)
                        throw new ArgumentOutOfRangeException($"txPos({this.buffer_.writerOffset_}) + offset({offset}) > capacity({this.buffer_.capacity_})");
#endif
                    if (newPos == this.buffer_.capacity_)
                    {
                        if (!this.buffer_.inversed_)
                            throw new Exception("Illegal State");

                        this.buffer_.readerOffset_ = 0;
                        this.buffer_.inversed_ = false;
                    }
                    else
                    {
                        if (newPos == this.buffer_.writerOffset_ && !this.buffer_.inversed_)
                        {
                            this.buffer_.writerOffset_ = 0;
                            this.buffer_.readerOffset_ = 0;
                        }
                        else
                        {
                            this.buffer_.readerOffset_ = newPos;
                        }
                    }
                    if (this.buffer_.writerDemand_.TryPickT0(out var demand, out var _))
                    {
                        this.buffer_.writerDemand_ = RingBufferError.Idle;
                        signal = demand.Signal;
                    }
                }
                if (signal is UniTaskCompletionSource utcs)
                    utcs.TrySetResult();
                this.buffer_.readerSema_.Release();
            }
        }

        private readonly struct ReclaimWriterBuffSegm : IReclaimWriterBuffSegm<T>
        {
            private readonly RingBuffer<T> buffer_;

            public ReclaimWriterBuffSegm(RingBuffer<T> buffer)
                => this.buffer_ = buffer;

            public void Reclaim(Memory<T> mem, uint offset)
            {
                UniTaskCompletionSource? signal = null;
                lock (this.buffer_)
                {
                    var newPos = this.buffer_.writerOffset_ + offset;
#if DEBUG
                    if (newPos > this.buffer_.capacity_)
                        throw new ArgumentOutOfRangeException($"txPos({this.buffer_.writerOffset_}) + offset({offset}) > capacity({this.buffer_.capacity_})");
#endif
                    if (newPos == this.buffer_.capacity_)
                    {
                        this.buffer_.writerOffset_ = 0;
                        this.buffer_.inversed_ = true;
                    }
                    else
                    {
                        this.buffer_.writerOffset_ = newPos;
                    }
                    if (this.buffer_.readerDemand_.TryPickT0(out var demand, out var _))
                    {
                        this.buffer_.readerDemand_ = RingBufferError.Idle;
                        signal = demand.Signal;
                    }
                }
                if (signal is UniTaskCompletionSource utcs)
                    utcs.TrySetResult();
                this.buffer_.writerSema_.Release();
            }
        }
    }
}
