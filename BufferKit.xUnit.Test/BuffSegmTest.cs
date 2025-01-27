namespace BufferKit.xUnit.Test
{
    public sealed class BuffSegmTest
    {
        [Fact]
        public void BuffSegmReadWriteShouldChangeLength()
        {
            var data = new byte[128];
            using (var segmRef = new ReaderBuffSegm<byte>(new ReadOnlyMemory<byte>(data)))
            {
                Assert.Equal((NUsize)data.Length, segmRef.Length);
                var memory = segmRef.ReadAll();
                Assert.Equal((NUsize)0, segmRef.Length);
            }
            using (var segmMut = new WriterBuffSegm<byte>(new Memory<byte>(data)))
            {
                Assert.Equal((NUsize)data.Length, segmMut.Length);
                var memory = segmMut.WriteAll();
                Assert.Equal((NUsize)0, segmMut.Length);
            }
        }

        [Fact]
        public async Task BuffSegmRefBorrowSliceShouldChangeLength()
        {
            var source = new byte[256];
            for (var u = 0; u < source.Length; ++u)
                source[u] = (byte)u;

            var segm = new ReaderBuffSegm<byte>(new ReadOnlyMemory<byte>(source));
            byte c = 0;
            int i = 0;
            while (true)
            {
                if (segm.Length == 0)
                    break;
                var maybeSlice = await segm.SliceAsync(c);
                if (!maybeSlice.TryPickT0(out var slice, out var err))
                    throw err.CreateException();

                using (slice)
                {
                    Assert.True(slice.Length <= c);
                    foreach (var b in slice.ReadAll().ToArray())
                    {
                        Assert.Equal(i, b);
                        ++i;
                    }
                }
                c++;
            }
        }

        [Fact]
        public async Task BuffSegmMutBorrowSliceShouldChangeLength()
        {
            var source = new byte[256];
            for (var u = 0; u < source.Length; ++u)
                source[u] = (byte)u;

            var segm = new WriterBuffSegm<byte>(new Memory<byte>(source));
            byte c = 0;
            int i = 0;
            while (true)
            {
                if (segm.Length == 0)
                    break;
                var maybeSlice = await segm.SliceAsync(c);
                if (!maybeSlice.TryPickT0(out var slice, out var err))
                    throw err.CreateException();

                using (slice)
                {
                    Assert.True(slice.Length <= c);
                    foreach (var b in slice.WriteAll().ToArray())
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