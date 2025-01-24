namespace BufferKit
{
    using System.Diagnostics.CodeAnalysis; // to use [NotNullWhen()] for override object.Equals

    using Cysharp.Threading.Tasks;

    using OneOf;

    public interface IReclaim<T>
    { }

    public interface IReclaimReaderBuffSegm<T>: IReclaim<T>
    {
        public void Reclaim(ReadOnlyMemory<T> mem, uint offset);
    }

    public interface IReclaimWriterBuffSegm<T>: IReclaim<T>
    {
        public void Reclaim(Memory<T> mem, uint offset);
    }

    #region IBuffSegm variants

    internal interface IBuffSegm<T>
    { }

    internal interface IReaderBuffSegm<T>: IBuffSegm<T>
    {
        public void Forward(uint length);
    }

    internal interface IWriterBuffSegm<T>: IBuffSegm<T>
    {
        public void Forward(uint length);
    }

    #endregion

    public readonly struct BuffSegmError
    {
        private readonly uint code_;

        private BuffSegmError(uint code)
            => this.code_ = code;

        public static readonly ReadOnlyMemory<string> NAMES = new ReadOnlyMemory<string>(
            ["Borrowed", "Disposed", "Insufficient"]);

        public static readonly BuffSegmError Borrowed = new BuffSegmError(0);

        public static readonly BuffSegmError Disposed = new BuffSegmError(1);

        public static readonly BuffSegmError Insufficient = new BuffSegmError(2);

        public override string ToString()
            => $"BuffSegmError.{NAMES.Span[(int)this.code_]}";

        public static bool operator ==(BuffSegmError lhs, BuffSegmError rhs)
            => lhs.code_ == rhs.code_;

        public static bool operator !=(BuffSegmError rhs, BuffSegmError lhs)
            => lhs.code_ != rhs.code_;

        public override int GetHashCode()
            => HashCode.Combine(typeof(BuffSegmError), this.code_);

        public override bool Equals([NotNullWhen(true)] object? obj)
            => obj is BuffSegmError err && this.code_ == err.code_;
    }

    public sealed class ReaderBuffSegm<T>: IReaderBuffSegm<T>, IDisposable
    {
        private readonly ReadOnlyMemory<T> memory_;

        private readonly SemaphoreSlim semaphore_;

        private IReclaimReaderBuffSegm<T>? reclaim_;

        private uint offset_;

        public ReaderBuffSegm(ReadOnlyMemory<T> memory) : this(new NoReclaim<T>(), memory, 0)
        { }

        public ReaderBuffSegm(IReclaimReaderBuffSegm<T> reclaim, ReadOnlyMemory<T> memory) : this(reclaim, memory, 0)
        { }

        private ReaderBuffSegm(IReclaimReaderBuffSegm<T> reclaim, ReadOnlyMemory<T> memory, uint offset)
        {
            this.reclaim_ = reclaim;
            this.semaphore_ = new SemaphoreSlim(1, 1);
            this.memory_ = memory;
            this.offset_ = offset;
        }

        public uint Length
            => (uint)this.memory_.Length - this.offset_;

        /// <summary>
        /// 获取并消耗所有未读取缓冲区，操作完成后此对象 Length 属性将变为0.
        /// </summary>
        public ReadOnlyMemory<T> Memory
        {
            get
            {
                var m = this.memory_.Slice((int)this.offset_);
                this.offset_ += (uint)m.Length;
                return m;
            }
        }

        public uint CopyTo(Memory<T> target)
        {
            if (this.Length == 0 || target.Length == 0)
                return 0;

            var copyLen = Math.Min(this.Length, (uint)target.Length);
            var srcSlice = this.memory_.Slice((int)this.offset_, (int)copyLen);
            srcSlice.CopyTo(target);
            this.offset_ += copyLen;
            return copyLen;
        }

        public async UniTask<OneOf<ReaderBuffSegm<T>, BuffSegmError>> SliceAsync(uint length, CancellationToken token = default)
        {
            var succ = false;
            try
            {
                // 限制借用者的数量，只有当一个借用者结束后才能允许下一个借用
                await this.semaphore_.WaitAsync(token);

                uint borrowLength = Math.Min(this.Length, length);
                var reclaim = ReclaimBuffSegm<T>.Create(this);
                var memory = this.memory_.Slice((int)this.offset_, (int)borrowLength);

                succ = true;
                return new ReaderBuffSegm<T>(reclaim, memory, 0);
            }
            finally
            {
                if (!succ && this.semaphore_.CurrentCount == 0)
                    this.semaphore_.Release();
            }
        }

        void IReaderBuffSegm<T>.Forward(uint length)
        {
            this.offset_ += length;
            this.semaphore_.Release();
        }

        public void Dispose()
        {
            if (this.reclaim_ is IReclaimReaderBuffSegm<T> reclaim)
                reclaim.Reclaim(this.memory_, this.offset_);

            this.reclaim_ = null;
            GC.SuppressFinalize(this);
        }

        ~ReaderBuffSegm()
            => this.Dispose();
    }

    public sealed class WriterBuffSegm<T>: IWriterBuffSegm<T>, IDisposable
    {
        private readonly Memory<T> memory_;

        private readonly SemaphoreSlim semaphore_;

        private IReclaimWriterBuffSegm<T>? reclaim_;

        private uint offset_;

        public WriterBuffSegm(Memory<T> memory) : this(new NoReclaim<T>(), memory)
        { }

        public WriterBuffSegm(IReclaimWriterBuffSegm<T> reclaim, Memory<T> memory) : this(reclaim, memory, 0)
        { }

        private WriterBuffSegm(IReclaimWriterBuffSegm<T> reclaim, Memory<T> memory, uint offset)
        {
            this.reclaim_ = reclaim;
            this.semaphore_ = new SemaphoreSlim(1, 1);
            this.memory_ = memory;
            this.offset_ = offset;
        }

        /// <summary>
        /// 查询未填充的缓冲区长度
        /// </summary>
        public uint Length
            => (uint)this.memory_.Length - this.offset_;

        /// <summary>
        /// 获取并消耗所有未填充缓冲区，操作完成后此对象 Length 属性将变为0.
        /// </summary>
        public Memory<T> Memory
        {
            get
            {
                var m = this.memory_.Slice((int)this.offset_);
                this.offset_ += (uint)m.Length;
                return m;
            }
        }

        public async UniTask<OneOf<uint, BuffSegmError>> CopyFromAsync(ReaderBuffSegm<T> source)
        {
            var copyLen = Math.Min(this.Length, source.Length);
            var maybeSlice = await source.SliceAsync(copyLen);
            if (!maybeSlice.TryPickT0(out var srcSlice, out var error))
                return error;
            using (srcSlice)
            {
                var srcMem = srcSlice.Memory;
                var dstMem = this.memory_.Slice((int)this.offset_, (int)copyLen);
                srcMem.CopyTo(dstMem);
                return copyLen;
            }
        }

        public uint CopyFrom(ReadOnlyMemory<T> source)
        {
            if (this.Length == 0 || source.Length == 0)
                return 0;
            var copyLen = Math.Min(this.Length, (uint)source.Length);
            var dst = this.memory_.Slice((int)this.offset_);
            var src = source.Slice(0, (int)copyLen);
            src.CopyTo(dst);
            this.offset_ += copyLen;
            return copyLen;
        }

        /// <summary>
        /// 从头部借出不大于指定长度的一段用于填充数据
        /// </summary>
        /// <param name="length"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async UniTask<OneOf<WriterBuffSegm<T>, BuffSegmError>> SliceAsync(uint length, CancellationToken token = default)
        {
            var succ = false;
            try
            {
                await this.semaphore_.WaitAsync(token);

                uint borrowLength = Math.Min(this.Length, length);
                var reclaim = ReclaimBuffSegm<T>.Create(this);
                var memory = this.memory_.Slice((int)this.offset_, (int)borrowLength);

                succ = true;
                return new WriterBuffSegm<T>(reclaim, memory, 0);
            }
            finally
            {
                if (!succ && this.semaphore_.CurrentCount == 0)
                    this.semaphore_.Release();
            }
        }

        void IWriterBuffSegm<T>.Forward(uint length)
        {
            this.offset_ += length;
            this.semaphore_.Release();
        }

        public void Dispose()
        {
            if (this.reclaim_ is IReclaimWriterBuffSegm<T> reclaim)
                reclaim.Reclaim(this.memory_, this.offset_);

            this.reclaim_ = null;
            GC.SuppressFinalize(this);
        }

        ~WriterBuffSegm()
            => this.Dispose();
    }

    internal readonly struct NoReclaim<T>: IReclaimReaderBuffSegm<T>, IReclaimWriterBuffSegm<T>
    {
        public void Reclaim(ReadOnlyMemory<T> mem, uint offset)
            => DoNothing();

        public void Reclaim(Memory<T> mem, uint offset)
            => DoNothing();

        private static void DoNothing() { }
    }

    internal readonly struct ReclaimBuffSegm<T>: IReclaimReaderBuffSegm<T>, IReclaimWriterBuffSegm<T>
    {
        private readonly IBuffSegm<T> source_;

        private ReclaimBuffSegm(IBuffSegm<T> source)
            => this.source_ = source;

        public static ReclaimBuffSegm<T> Create<S>(in S source) where S: class, IBuffSegm<T>
            => new ReclaimBuffSegm<T>(source);

        public void Reclaim(ReadOnlyMemory<T> mem, uint offset)
        {
            if (this.source_ is IReaderBuffSegm<T> source)
                source.Forward(offset);
            else
                throw this.source_.UnexpectedTypeException();
        }

        public void Reclaim(Memory<T> mem, uint offset)
        {
            if (this.source_ is IWriterBuffSegm<T> source)
                source.Forward(offset);
            else
                throw this.source_.UnexpectedTypeException();
        }
    }

    public static class BuffSegmThrowExtension
    {
        public static Exception CreateException(in this BuffSegmError error)
            => new Exception($"{error}");

        internal static Exception UnexpectedTypeException<T>(this IBuffSegm<T> segm)
            => new Exception($"Unexpected source type({segm.GetType()})");
    }
}
