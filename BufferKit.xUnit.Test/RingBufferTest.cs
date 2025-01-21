namespace BufferKit.xUnit.Test
{
    using Microsoft.VisualStudio.TestPlatform.Utilities;

    using Xunit.Abstractions;

    public sealed class RingBufferTest
    {
        private readonly ITestOutputHelper output_;

        public RingBufferTest(ITestOutputHelper output)
            => this.output_ = output;

        [Fact]
        public async Task RingBufferReadShouldReturnNoGreaterLengthThanDemanded()
        {
            var ringBuff = new RingBuffer<byte>(16);
            for (uint i = 0; i < ringBuff.Capacity; i++)
            {
                var maybeTxBuff = await ringBuff.WriteAsync(i);
                if (!maybeTxBuff.TryPickT0(out var txBuff, out var error))
                    throw error.AsException();
                Assert.True(txBuff.Length <= i);
                var txMem = txBuff.Memory;
                txBuff.Dispose();

                var maybeRxBuff = await ringBuff.ReadAsync(i);
                if (!maybeRxBuff.TryPickT0(out var rxBuff, out error))
                    throw error.AsException();
                Assert.True(rxBuff.Length <= i);
                var rxMem = rxBuff.Memory;
                rxBuff.Dispose();
            }
        }
        [Fact]

        public async Task RingBufferShouldWorkForSingleUnit()
        {
            RingBuffer<byte>? ringBuffer = new RingBuffer<byte>(1);
            var maybe = RingBuffer<byte>.TrySplit(ref ringBuffer);
            if (!maybe.TryPickT0(out var pair, out var _))
                throw new Exception();

            (var tx, var rx) = pair;
            var count = 255;
            var txCount = 0;
            var rxCount = 0;

            var txTask = async () =>
            {
                while (txCount < count)
                {
                    var tryWrite = await tx.DumpAsync(new ReadOnlyMemory<byte>([(byte)txCount]));
                    if (!tryWrite.TryPickT0(out var writeCount, out var error))
                        throw error.AsException();
                    Assert.Equal((uint)1, writeCount);
                    txCount += 1;
                }
            };
            var rxTask = async () =>
            {
                while (rxCount < count)
                {
                    var mem = new Memory<byte>([0]);
                    var tryRead = await rx.FillAsync(mem);
                    if (!tryRead.TryPickT0(out var readCount, out var error))
                        throw error.AsException();
                    Assert.Equal((uint)1, readCount);
                    Assert.Equal((byte)rxCount, mem.Span[0]);
                    rxCount += 1;
                }
            };
            await Task.WhenAll(txTask(), rxTask());
        }
    }
}
