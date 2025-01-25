namespace BufferKit
{
    using System;
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
            => obj is RingBufferError err && this.code_ == err.code_;

        public override int GetHashCode()
            => HashCode.Combine(typeof(RingBufferError), this.code_);
    }

    public readonly struct IoPosition
    {
        public uint Offset { get; init; }
        public uint Length { get; init; }
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
            => this.GetWriterPositions().SumLength();

        /// <summary>
        /// 可供读者消费的数据量
        /// </summary>
        public uint ReaderSize
            => this.GetReaderPositions().SumLength();

        public ReadOnlyMemory<IoPosition> GetReaderPositions()
        {
            lock (this)
            {
                if (this.inversed_)
                {
                    var p0 = new IoPosition
                    {
                        Offset = this.readerOffset_,
                        Length = this.capacity_ - this.readerOffset_,
                    };
                    var p1 = new IoPosition
                    {
                        Offset = 0,
                        Length = this.writerOffset_,
                    };
                    if (p0.Length == 0)
                        return ReadOnlyMemory<IoPosition>.Empty;
                    if (p1.Length == 0)
                        return new IoPosition[] { p0 };
                    else
                        return new IoPosition[] { p0, p1 };
                }
                else
                {
                    var p0 = new IoPosition
                    {
                        Offset = this.readerOffset_,
                        Length = this.writerOffset_ - this.readerOffset_,
                    };
                    if (p0.Length == 0)
                        return ReadOnlyMemory<IoPosition>.Empty;
                    else
                        return new IoPosition[] { p0 };
                }
            }
        }

        public ReadOnlyMemory<IoPosition> GetWriterPositions()
        {
            lock (this)
            {
                if (this.inversed_)
                { 
                    if (this.readerOffset_ == this.writerOffset_)
                        return ReadOnlyMemory<IoPosition>.Empty;
                    
                    var p0 = new IoPosition
                    {
                        Offset = this.writerOffset_,
                        Length = this.readerOffset_ - this.writerOffset_,
                    };
                    return new IoPosition[] { p0 };
                }
                else
                {
                    var p0 = new IoPosition
                    {
                        Offset = this.writerOffset_,
                        Length = this.capacity_ - this.writerOffset_,
                    };
                    var p1 = new IoPosition
                    {
                        Offset = 0,
                        Length = this.readerOffset_,
                    };
                    if (p0.Length == 0)
                        return ReadOnlyMemory<IoPosition>.Empty;
                    if (p1.Length == 0)
                        return new IoPosition[] { p0 };
                    else
                        return new IoPosition[] { p0, p1 };
                }
            }
        }

        /// <summary>
        /// 从内部缓冲区中借出一段可供消费者读取的已填充缓存，该缓存长度不大于给定的长度；如果执行完成，则返回该已填充缓存
        /// </summary>
        /// <param name="length"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public UniTask<OneOf<ReadOnlyMemory<ReaderBuffSegm<T>>, RingBufferError>> ReadAsync(uint length, CancellationToken token = default)
            => this.RxAsync_(offset: 0, length, this.CreateReaderSegm, token);
        

        public UniTask<OneOf<ReadOnlyMemory<PeekerBuffSegm<T>>, RingBufferError>> PeekAsync(uint offset, CancellationToken token = default)
            => this.RxAsync_(offset, this.capacity_, this.CreatePeekerSegm, token);

        public UniTask<OneOf<uint, RingBufferError>> ReaderSkipAsync(uint length, CancellationToken token = default)
            => throw new NotImplementedException();

        private ReaderBuffSegm<T> CreateReaderSegm(ReadOnlyMemory<T> m)
            => new ReaderBuffSegm<T>(new ReclaimReaderBuffSegm(this), m);

        private PeekerBuffSegm<T> CreatePeekerSegm(ReadOnlyMemory<T> m)
            => new PeekerBuffSegm<T>(new ReclaimPeekerBuffSegm(this), m);

        private async UniTask<OneOf<ReadOnlyMemory<S>, RingBufferError>> RxAsync_<S>
            ( uint offset
            , uint length
            , Func<ReadOnlyMemory<T>, S> createSlice
            , CancellationToken token)
            where S: IBuffSegm<T>
        {
            if (this.readerDemand_.TryPickT1(out var error, out var _) && error == RingBufferError.Closed)
                return error;
            if (offset >= this.capacity_)
                return RingBufferError.Incapable;
            if (length == 0)
                return ReadOnlyMemory<S>.Empty;
            var succ = false;
            try
            {
                await this.readerSema_.WaitAsync(token);                
                while (true)
                {
                    var positions = this.GetReaderPositions();
                    if (positions.IsEmpty && this.IsTxClosed)
                        return RingBufferError.Closed;

                    var rxSize = positions.SumLength();
                    var available = rxSize - offset;
                    if (available > 0)
                    {
                        var l = Math.Min(available, length);
                        var s0 = createSlice(this.memory_.Slice(in positions.Span[0], l));
                        var rest = length - s0.Length;
                        succ = true;
                        if (positions.Length > 1 && rest > 0)
                        {
                            var s1 = createSlice(this.memory_.Slice(in positions.Span[1], rest));
                            return new ReadOnlyMemory<S>([s0, s1]);
                        }
                        else
                        {
                            return new ReadOnlyMemory<S>([s0]);
                        }
                    }
                    Demand demand;
                    lock (this)
                    {
                        while (true)
                        {
                            if (this.readerDemand_.TryPickT0(out demand, out error))
                                break;
                            if (error != RingBufferError.Idle)
                                return error;
                            this.readerDemand_ = new Demand { Amount = length, Signal = new UniTaskCompletionSource() };
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
        /// 从内部缓冲区中借出一段未填充缓存，该缓存长度不大于给定的长度；如果成功则返回该未填充缓存
        /// </summary>
        /// <param name="length"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async UniTask<OneOf<ReadOnlyMemory<WriterBuffSegm<T>>, RingBufferError>> WriteAsync(uint length, CancellationToken token = default)
        {
            if (this.writerDemand_.TryPickT1(out var error, out var _) && error == RingBufferError.Closed)
                return error;
            if (length == 0)            
                return ReadOnlyMemory<WriterBuffSegm<T>>.Empty;
            var succ = false;
            try
            {
                await this.writerSema_.WaitAsync(token);
                while (true)
                {
                    var positions = this.GetWriterPositions();
                    if (positions.IsEmpty && this.IsRxClosed)
                        return RingBufferError.Closed;

                    if (positions.SumLength() > 0)
                    {
                        var m0 = this.memory_.Slice(in positions.Span[0], length);
                        var s0 = new WriterBuffSegm<T>(new ReclaimWriterBuffSegm(this), m0);
                        var rest = length - s0.Length;
                        succ = true;
                        if (positions.Length > 1 && rest > 0)
                        {
                            var m1 = this.memory_.Slice(in positions.Span[1], rest);
                            var s1 = new WriterBuffSegm<T>(new ReclaimWriterBuffSegm(this), m1);
                            return new ReadOnlyMemory<WriterBuffSegm<T>>([s0, s1]);
                        }
                        else
                        {
                            if (s0.Length == 0)
                                throw new Exception("Fuck you");
                            return new ReadOnlyMemory<WriterBuffSegm<T>>([s0]);
                        }
                    }
                    Demand demand;
                    lock (this)
                    {
                        while (true)
                        {
                            if (this.writerDemand_.TryPickT0(out demand, out error))
                                break;
                            if (error != RingBufferError.Idle)
                                return error;
                            this.writerDemand_ = new Demand { Amount = length, Signal = new UniTaskCompletionSource() };
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

        async UniTask<OneOf<ReadOnlyMemory<ReaderBuffSegm<T>>, IBufferError>> IBuffer<T>.ReadAsync(uint length, CancellationToken token)
        {
            var r = await this.ReadAsync(length, token);
            return r.MapT1((e) => e as IBufferError);
        }

        async UniTask<OneOf<ReadOnlyMemory<PeekerBuffSegm<T>>, IBufferError>> IBuffer<T>.PeekAsync(uint offset, System.Threading.CancellationToken token)
        {
            var r = await this.PeekAsync(offset, token);
            return r.MapT1((e) => e as IBufferError);
        }

        async UniTask<OneOf<uint, IBufferError>> IBuffer<T>.ReaderSkipAsync(uint length, System.Threading.CancellationToken token)
        {
            var r = await this.ReaderSkipAsync(length, token);
            return r.MapT1((e) => e as IBufferError);
        }

        async UniTask<OneOf<ReadOnlyMemory<WriterBuffSegm<T>>, IBufferError>> IBuffer<T>.WriteAsync(uint length, CancellationToken token)
        {
            var r = await this.WriteAsync(length, token);
            return r.MapT1((e) => e as IBufferError);
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

        /// <summary>
        /// 调整 readerOffset, 唤醒 writerDemand, 释放 readerSema
        /// </summary>
        private readonly struct ReclaimReaderBuffSegm : IReclaimReadOnlyMemory<T>
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

        /// <summary>
        /// 只释放 readerSema
        /// </summary>
        private readonly struct ReclaimPeekerBuffSegm : IReclaimReadOnlyMemory<T>
        {
            private readonly RingBuffer<T> buffer_;

            public ReclaimPeekerBuffSegm(RingBuffer<T> buffer)
                => this.buffer_ = buffer;

            public void Reclaim(ReadOnlyMemory<T> mem, uint offset)
                => this.buffer_.readerSema_.Release();
        }

        private readonly struct ReclaimWriterBuffSegm : IReclaimMutableMemory<T>
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

    internal static class IoPositionsArrayExtensions
    {
        public static uint SumLength(in this ReadOnlyMemory<IoPosition> a)
        {
            uint s = 0;
            for (var i = 0; i < a.Length; ++i)
                s += a.Span[i].Length;
            return s;
        }

        public static Memory<T> Slice<T>(in this Memory<T> m, in IoPosition p, in uint demand)
        {
            var l = Math.Min(p.Length, demand);
            return m.Slice((int)p.Offset, (int)l);
        }
    }
}
