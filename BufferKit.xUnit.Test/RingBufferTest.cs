namespace BufferKit.xUnit.Test
{
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
                var maybeTxBuffArr = await ringBuff.WriteAsync(i);
                if (!maybeTxBuffArr.TryPickT0(out var txBuffArr, out var error))
                    throw error.AsException();
                uint txLen = 0;
                for (var iTx = 0; iTx < txBuffArr.Length; iTx++)
                {
                    using var txBuff = txBuffArr.Span[iTx];
                    txLen += txBuff.Length;
                }
                Assert.True(txLen < i);

                var maybeRxBuffArr = await ringBuff.ReadAsync(i);
                if (!maybeRxBuffArr.TryPickT0(out var rxBuffArr, out error))
                    throw error.AsException();
                uint rxLen = 0;
                for (var iRx = 0; iRx < txBuffArr.Length; iRx++)
                {
                    using var rxBuff = rxBuffArr.Span[iRx];
                    rxLen += rxBuff.Length;
                }
                Assert.True(rxLen < i);
            }
        }

        [Fact]
        public async Task RingBufferShouldWorkForSingleUnit()
        {
            var ringBuffer = new RingBuffer<byte>(1);
            var maybe = ringBuffer.TrySplit();
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
                    var tryRead = await rx.ReadAsync(mem);
                    if (!tryRead.TryPickT0(out var readCount, out var error))
                        throw error.AsException();
                    Assert.Equal((uint)1, readCount);
                    Assert.Equal((byte)rxCount, mem.Span[0]);
                    rxCount += 1;
                }
            };
            await Task.WhenAll(txTask(), rxTask());
        }

        [Theory]
        [InlineData(1, 16)]
        [InlineData(4, 64)]
        [InlineData(64, 256)]
        public async Task ReadDataOrderShouldBeTheSameAsWriteOrder(int capacity, int maxNum)
        {
            var ringBuffer = new RingBuffer<int>((uint)capacity);

            var testDataSlices = new List<int[]>();
            var currNum = 0;
            while (currNum < maxNum)
            {
                var restLen = maxNum - currNum;
                int randLen;
                if (restLen == 1)
                    randLen = 1;
                else
                    randLen = Random.Shared.Next(1, restLen);
                var slice = new int[randLen];
                for (var i = 0; i < randLen; i++)
                    slice[i] = currNum++;
                testDataSlices.Add(slice);
            }
            var rxWorker = async (BuffRx<int> rx) =>
            {
                var readNum = 0;
                var buff = (new int[1]).AsMemory();
                while (readNum < maxNum)
                {
                    var r = await rx.ReadAsync(buff);
                    Assert.True(r.IsT0);
                    Assert.Equal((uint)buff.Length, r.AsT0);
                    Assert.Equal(readNum++, buff.Span[0]);
                }
            };

            using var rx = ringBuffer.CreateRx();
            var rxTask = rxWorker(rx);

            using var tx = ringBuffer.CreateTx();
            foreach (var slice in testDataSlices)
            {
                var d = await tx.DumpAsync(slice);
                Assert.True(d.IsT0);
                Assert.Equal((uint)slice.Length, d.AsT0);
            }
            await rxTask;
        }
    }
}
