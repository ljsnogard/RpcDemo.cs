namespace BufferKit
{
    using System.Diagnostics.CodeAnalysis;

    using OneOf;

    /// <summary>
    /// Unsigned integer types wrapping around nuint
    /// </summary>
    public readonly struct NUsize : IComparable<NUsize>, IEquatable<NUsize>
    {
        public readonly nuint Val;

        public NUsize(nuint val)
            => this.Val = val;

        #region Convert

        public static OneOf<NUsize, UInt128> TryFrom(UInt128 u)
        {
            if (u > nuint.MaxValue)
                return u;
            else
                return new NUsize((nuint)u);
        }

        public static OneOf<NUsize, UInt64> TryFrom(UInt64 u)
        {
            if (u > nuint.MaxValue)
                return u;
            else
                return new NUsize((nuint)u);
        }

        public static implicit operator NUsize(nuint u)
            => new NUsize(u);

        public static implicit operator NUsize(UInt32 u)
            => new NUsize(u);

        public static implicit operator NUsize(UInt16 u)
            => new NUsize(u);

        public static implicit operator NUsize(Byte u)
            => new NUsize(u);

        public static implicit operator nuint(NUsize u)
            => u.Val;

        public static OneOf<NUsize, Int128> TryFrom(Int128 u)
        {
            if (u < 0 || u > nuint.MaxValue)
                return u;
            else
                return new NUsize((nuint)u);
        }

        public static OneOf<NUsize, Int64> TryFrom(Int64 u)
        {
            if (u < 0 || (Int128)u > nuint.MaxValue)
                return u;
            else
                return new NUsize((nuint)u);
        }

        public static OneOf<NUsize, Int32> TryFrom(Int32 u)
        {
            if (u < 0)
                return u;
            else
                return new NUsize((nuint)u);
        }

        public static OneOf<NUsize, Int16> TryFrom(Int16 u)
        {
            if (u < 0)
                return u;
            else
                return new NUsize((nuint)u);
        }

        public static OneOf<NUsize, SByte> TryFrom(SByte u)
        {
            if (u < 0)
                return u;
            else
                return new NUsize((nuint)u);
        }

        #endregion

        #region Add, Sub, Mul, Div, Mod

        public static NUsize operator +(NUsize a, NUsize b)
            => new NUsize(a.Val + b.Val);

        public static NUsize operator -(NUsize a, NUsize b)
            => new NUsize(a.Val - b.Val);

        public static NUsize operator *(NUsize a, NUsize b)    
            => new NUsize(a.Val * b.Val);

        public static NUsize operator /(NUsize a, NUsize b)
            => new NUsize(a.Val / b.Val);

        public static NUsize operator %(NUsize a, NUsize b)
            => new NUsize(a.Val % b.Val);

        public static NUsize operator <<(NUsize a, int b)
            => new NUsize(a.Val << b);

        public static NUsize operator >>(NUsize a, int b)
            => new NUsize(a.Val >> b);

        #endregion

        #region Compare with Int128

        public static bool operator >(NUsize a, Int128 b)
            => a.Val > b;

        public static bool operator <(NUsize a, Int128 b)
            => a.Val < b;

        public static bool operator >=(NUsize a, Int128 b)
            => a.Val >= b;

        public static bool operator <=(NUsize a, Int128 b)
            => a.Val <= b;

        public static bool operator ==(NUsize a, Int128 b)
            => a.Val == b;

        public static bool operator !=(NUsize a, Int128 b)
            => a.Val != b;

        #endregion

        #region Compare with Int64

        public static bool operator >(NUsize a, Int64 b)
            => (Int128)a.Val > (Int128)b;

        public static bool operator <(NUsize a, Int64 b)
            => (Int128)a.Val < (Int128)b;

        public static bool operator >=(NUsize a, Int64 b)
            => (Int128)a.Val >= (Int128)b;

        public static bool operator <=(NUsize a, Int64 b)
            => (Int128)a.Val <= (Int128)b;

        public static bool operator ==(NUsize a, Int64 b)
            => (Int128)a.Val == (Int128)b;

        public static bool operator !=(NUsize a, Int64 b)
            => (Int128)a.Val != (Int128)b;

        #endregion

        #region Compare with Int32

        public static bool operator >(NUsize a, Int32 b)
            => a.Val > (Int128)b;

        public static bool operator <(NUsize a, Int32 b)
            => a.Val < (Int128)b;

        public static bool operator >=(NUsize a, Int32 b)
            => a.Val >= (Int128)b;

        public static bool operator <=(NUsize a, Int32 b)
            => a.Val <= (Int128)b;

        public static bool operator ==(NUsize a, Int32 b)
            => a == (Int128)b;

        public static bool operator !=(NUsize a, Int32 b)
            => a != (Int128)b;

        #endregion

        #region Comparable

        public int CompareTo(NUsize other)
            => this.Val.CompareTo(other.Val);

        public static bool operator >(NUsize a, NUsize b)
            => a.Val > b.Val;

        public static bool operator <(NUsize a, NUsize b)
            => a.Val < b.Val;

        public static bool operator >=(NUsize a, NUsize b)
            => a.Val >= b.Val;

        public static bool operator <=(NUsize a, NUsize b)
            => a.Val <= b.Val;

        #endregion

        #region Equatable

        public static bool operator ==(NUsize a, NUsize b)
            => a.Val == b.Val;

        public static bool operator !=(NUsize a, NUsize b)   
            => a.Val != b.Val;

        public bool Equals(NUsize other)
            => this.Val == other.Val;

        public override bool Equals([NotNullWhen(true)] object? obj)
            => obj is NUsize other && this.Equals(other);

        public override int GetHashCode()
            => this.Val.GetHashCode();

        public override string ToString()
            => this.Val.ToString();

        #endregion
    }
}
