namespace BufferKit
{
    using OneOf;

    using System.Diagnostics.CodeAnalysis;

    public interface IReclaim<T>
    { }

    public interface IReclaimRef<T>: IReclaim<T>
    {
        public void Reclaim(ReadOnlyMemory<T> mem, uint offset);
    }

    public interface IReclaimMut<T>: IReclaim<T>
    {
        public void Reclaim(Memory<T> mem, uint offset);
    }

    #region IBuffSegm variants

    internal interface IBuffSegm<T>
    { }

    internal interface IBuffSegmRef<T>: IBuffSegm<T>
    {
        public void Forward(ReadOnlyMemory<T> memory, uint length);
    }

    internal interface IBuffSegmMut<T>: IBuffSegm<T>
    {
        public void Forward(Memory<T> memory, uint length);
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
        private OneOf<ReadOnlyMemory<T>, BuffSegmError> memory_;

        private IReclaimRef<T>? reclaim_;

        private uint offset_;

        private readonly uint length_;

        public BuffSegmRef(ReadOnlyMemory<T> memory) : this(new NoReclaim<T>(), memory)
        { }

        public BuffSegmRef(IReclaimRef<T> reclaim, ReadOnlyMemory<T> memory) : this(reclaim, memory, 0, (uint)memory.Length)
        { }

        private BuffSegmRef(IReclaimRef<T> reclaim, ReadOnlyMemory<T> memory, uint offset, uint length)
        {
            this.reclaim_ = reclaim;
            this.memory_ = memory;
            this.offset_ = offset;
            this.length_ = length;
        }

        public uint Length
            => this.length_;

        public ReadOnlyMemory<T> ReadOnlyMemory
        {
            get
            {
                if (!this.memory_.TryPickT0(out var memory, out var error))
                    throw error.CreateException();
                else
                    return memory.Slice((int)this.offset_, (int)this.length_);
            }
        }

        public OneOf<BuffSegmRef<T>, BuffSegmError> BorrowSlice(uint length)
        {
            if (!this.memory_.TryPickT0(out var memory, out var _))
                return BuffSegmError.Borrowed;

            uint borrowLength;
            if (length > this.length_)
                return BuffSegmError.Insufficient;
            else
                borrowLength = length;

            var reclaim = new ReclaimBuffSegm<T>(this);
            this.memory_ = BuffSegmError.Borrowed;

            return new BuffSegmRef<T>(reclaim, memory, this.offset_, borrowLength);
        }

        void IBuffSegmRef<T>.Forward(ReadOnlyMemory<T> memory, uint length)
        {
            this.memory_ = memory;
            this.offset_ += length;
        }

        void IDisposable.Dispose()
        {
            if (!this.memory_.TryPickT0(out var memory, out var error))
                throw error.CreateException();

            if (this.reclaim_ is IReclaimRef<T> reclaim)
                reclaim.Reclaim(memory, this.offset_);

            this.reclaim_ = null;
        }
    }

    public sealed class BuffSegmMut<T>: IBuffSegmMut<T>, IDisposable
    {
        private OneOf<Memory<T>, BuffSegmError> memory_;

        private IReclaimMut<T>? reclaim_;

        private uint offset_;

        private readonly uint length_;

        public BuffSegmMut(Memory<T> memory) : this(new NoReclaim<T>(), memory)
        { }

        public BuffSegmMut(IReclaimMut<T> reclaim, Memory<T> memory) : this(reclaim, memory, 0, (uint)memory.Length)
        { }

        private BuffSegmMut(IReclaimMut<T> reclaim, Memory<T> memory, uint offset, uint length)
        {
            this.reclaim_ = reclaim;
            this.memory_ = memory;
            this.offset_ = offset;
            this.length_ = length;
        }

        public uint Length
            => this.length_;

        public Memory<T> Memory
        {
            get
            {
                if (!this.memory_.TryPickT0(out var memory, out var error))
                    throw error.CreateException();
                else
                    return memory.Slice((int)this.offset_, (int)this.length_);
            }
        }

        public OneOf<BuffSegmMut<T>, BuffSegmError> BorrowSlice(uint length)
        {
            if (!this.memory_.TryPickT0(out var memory, out var _))
                return BuffSegmError.Borrowed;

            uint borrowLength;
            if (length > this.length_)
                return BuffSegmError.Insufficient;
            else
                borrowLength = length;

            var reclaim = new ReclaimBuffSegm<T>(this);
            this.memory_ = BuffSegmError.Borrowed;

            return new BuffSegmMut<T>(reclaim, memory, this.offset_, borrowLength);
        }

        void IBuffSegmMut<T>.Forward(Memory<T> memory, uint length)
        {
            this.memory_ = memory;
            this.offset_ += length;
        }

        void IDisposable.Dispose()
        {
            if (!this.memory_.TryPickT0(out var memory, out var error))
                throw error.CreateException();

            if (this.reclaim_ is IReclaimMut<T> reclaim)
                reclaim.Reclaim(memory, this.offset_);

            this.reclaim_ = null;
        }
    }

    internal readonly struct NoReclaim<T>: IReclaimRef<T>, IReclaimMut<T>
    {
        public void Reclaim(ReadOnlyMemory<T> mem, uint offset)
        { }

        public void Reclaim(Memory<T> mem, uint offset)
        { }
    }

    internal readonly struct ReclaimBuffSegm<T>: IReclaimRef<T>, IReclaimMut<T>
    {
        private readonly IBuffSegm<T> source_;

        public ReclaimBuffSegm(IBuffSegm<T> source)
            => this.source_ = source;

        public void Reclaim(ReadOnlyMemory<T> mem, uint offset)
        {
            if (this.source_ is IBuffSegmRef<T> source)
                source.Forward(mem, offset);
            else
                throw this.source_.UnexpectedTypeException();
        }

        public void Reclaim(Memory<T> mem, uint offset)
        { 
            if (this.source_ is IBuffSegmMut<T> source)
                source.Forward(mem, offset);
            else
                throw this.source_.UnexpectedTypeException();
        }
    }

    internal static class BuffSegmThrowExtension
    {
        public static Exception CreateException(in this BuffSegmError error)
            => new Exception($"{error}");

        public static Exception UnexpectedTypeException<T>(this IBuffSegm<T> segm)
            => new Exception($"Unexpected source type({segm.GetType()})");
    }
}
