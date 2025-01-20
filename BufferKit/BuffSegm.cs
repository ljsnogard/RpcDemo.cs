namespace BufferKit
{
    using System.Diagnostics.CodeAnalysis; // to use [NotNullWhen()] for override object.Equals

    using Cysharp.Threading.Tasks;

    using OneOf;

    public interface IReclaim<T>
    { }

    public interface IReclaimRef<T>: IReclaim<T>
    {
        public void Reclaim(ReadOnlyMemory<T> mem);
    }

    public interface IReclaimMut<T>: IReclaim<T>
    {
        public void Reclaim(Memory<T> mem);
    }

    #region IBuffSegm variants

    internal interface IBuffSegm<T>
    { }

    internal interface IBuffSegmRef<T>: IBuffSegm<T>
    {
        public void Forward(uint length);
    }

    internal interface IBuffSegmMut<T>: IBuffSegm<T>
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
        {
            if (obj is BuffSegmError other)
                return this.code_ == other.code_;
            else
                return false;
        }
    }

    public sealed class BuffSegmRef<T>: IBuffSegmRef<T>, IDisposable
    {
        private readonly ReadOnlyMemory<T> memory_;

        private IReclaimRef<T>? reclaim_;

        private uint offset_;

        public BuffSegmRef(ReadOnlyMemory<T> memory) : this(new NoReclaim<T>(), memory, 0)
        { }

        private BuffSegmRef(IReclaimRef<T> reclaim, ReadOnlyMemory<T> memory, uint offset)
        {
            this.reclaim_ = reclaim;
            this.memory_ = memory;
            this.offset_ = offset;
        }

        public uint Length
            => (uint)this.Memory.Length;

        public ReadOnlyMemory<T> Memory
            => this.memory_.Slice((int)this.offset_);

        public OneOf<BuffSegmRef<T>, BuffSegmError> BorrowSlice(uint length)
        {
            uint borrowLength = Math.Min(this.Length, length);
            var reclaim = new ReclaimBuffSegm<T>(this);
            var memory = this.memory_.Slice((int)this.offset_, (int)borrowLength);
            return new BuffSegmRef<T>(reclaim, memory, 0);
        }

        void IBuffSegmRef<T>.Forward(uint length)
            => this.offset_ += length;
        

        void IDisposable.Dispose()
        {
            if (this.reclaim_ is IReclaimRef<T> reclaim)
                reclaim.Reclaim(this.memory_);

            this.reclaim_ = null;
        }
    }

    public sealed class BuffSegmMut<T>: IBuffSegmMut<T>, IDisposable
    {
        private readonly Memory<T> memory_;

        private IReclaimMut<T>? reclaim_;

        private uint offset_;

        public BuffSegmMut(Memory<T> memory) : this(new NoReclaim<T>(), memory)
        { }

        public BuffSegmMut(IReclaimMut<T> reclaim, Memory<T> memory) : this(reclaim, memory, 0)
        { }

        private BuffSegmMut(IReclaimMut<T> reclaim, Memory<T> memory, uint offset)
        {
            this.reclaim_ = reclaim;
            this.memory_ = memory;
            this.offset_ = offset;
        }

        public uint Length
            => (uint)this.Memory.Length;

        public Memory<T> Memory
            => this.memory_.Slice((int)this.offset_);

        public OneOf<BuffSegmMut<T>, BuffSegmError> BorrowSlice(uint length)
        {
            uint borrowLength = Math.Min(this.Length, length);
            var reclaim = new ReclaimBuffSegm<T>(this);
            var memory = this.memory_.Slice((int)this.offset_, (int)borrowLength);
            return new BuffSegmMut<T>(reclaim, memory, 0);
        }

        void IBuffSegmMut<T>.Forward(uint length)
            => this.offset_ += length;

        void IDisposable.Dispose()
        {
            if (this.reclaim_ is IReclaimMut<T> reclaim)
                reclaim.Reclaim(this.memory_);

            this.reclaim_ = null;
        }
    }

    internal readonly struct NoReclaim<T>: IReclaimRef<T>, IReclaimMut<T>
    {
        public void Reclaim(ReadOnlyMemory<T> mem)
            => DoNothing();

        public void Reclaim(Memory<T> mem)
            => DoNothing();

        private static void DoNothing() { }
    }

    /// <summary>
    /// 用于为子串提供
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal readonly struct ReclaimBuffSegm<T>: IReclaimRef<T>, IReclaimMut<T>
    {
        private readonly IBuffSegm<T> source_;

        public ReclaimBuffSegm(IBuffSegm<T> source)
            => this.source_ = source;

        public void Reclaim(ReadOnlyMemory<T> mem)
        {
            if (this.source_ is IBuffSegmRef<T> source)
                source.Forward((uint)mem.Length);
            else
                throw this.source_.UnexpectedTypeException();
        }

        public void Reclaim(Memory<T> mem)
        {
            if (this.source_ is IBuffSegmMut<T> source)
                source.Forward((uint)mem.Length);
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
