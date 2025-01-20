namespace BufferKit.xUnit.Test
{
    public class BuffSegmTest
    {
        [Fact]
        public void BuffSegmRefBorrowSliceShouldChangeLength()
        {
            var source = new byte[256];
            for (var u = 0; u < source.Length; ++u)
                source[u] = (byte)u;

            var segm = new BuffSegmRef<byte>(new ReadOnlyMemory<byte>(source));
            byte c = 0;
            int i = 0;
            while (true)
            {
                if (segm.Length == 0)
                    break;
                var maybeSlice = segm.BorrowSlice(c);
                if (!maybeSlice.TryPickT0(out var slice, out var err))
                    throw err.CreateException();

                using (slice)
                {
                    Assert.True(slice.Length <= c);
                    foreach (var b in slice.Memory.ToArray())
                    {
                        Assert.Equal(i, b);
                        ++i;
                    }
                }
                c++;
            }
        }

        [Fact]
        public void BuffSegmMutBorrowSliceShouldChangeLength()
        {
            var source = new byte[256];
            for (var u = 0; u < source.Length; ++u)
                source[u] = (byte)u;

            var segm = new BuffSegmMut<byte>(new Memory<byte>(source));
            byte c = 0;
            int i = 0;
            while (true)
            {
                if (segm.Length == 0)
                    break;
                var maybeSlice = segm.BorrowSlice(c);
                if (!maybeSlice.TryPickT0(out var slice, out var err))
                    throw err.CreateException();

                using (slice)
                {
                    Assert.True(slice.Length <= c);
                    foreach (var b in slice.Memory.ToArray())
                    {
                        Assert.Equal(i, b);
                        ++i;
                    }
                }
                c++;
            }
        }
    }
}