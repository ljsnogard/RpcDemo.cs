﻿namespace BufferKit.xUnit.Test
{
    using Xunit.Abstractions;

    public sealed class RingBufferTest
    {
        private class ConsoleWriter : StringWriter
        {
            private ITestOutputHelper output;

            public ConsoleWriter(ITestOutputHelper output)
                => this.output = output;

            public override void WriteLine(string? m)
                => output.WriteLine(m);
            
        }

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
                    var mem = txBuff.WriteAll();
                    txLen += (uint)mem.Length;
                }
                Assert.True(txLen <= i);

                var maybeRxBuffArr = await ringBuff.ReadAsync(i);
                if (!maybeRxBuffArr.TryPickT0(out var rxBuffArr, out error))
                    throw error.AsException();
                uint rxLen = 0;
                for (var iRx = 0; iRx < txBuffArr.Length; iRx++)
                {
                    using var rxBuff = rxBuffArr.Span[iRx];
                    var mem = rxBuff.ReadAll();
                    rxLen += (uint)mem.Length;
                }
                Assert.True(rxLen <= i);
            }
        }

        [Fact]
        public async Task RingBufferShouldWorkWithLeastCapacity()
        {
            Console.SetOut(new ConsoleWriter(this.output_));

            var ringBuffer = new RingBuffer<byte>(1);
            var maybe = ringBuffer.TrySplit();
            if (!maybe.TryPickT0(out var pair, out var _))
                throw new Exception();

            (var tx, var rx) = pair;
            NUsize count = 8;
            NUsize txCount = 0;
            NUsize rxCount = 0;

            var rxWork = async () =>
            {
                using (rx)
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
                    this.output_.WriteLine("rx loop exits.");
                }
            };
            var rxTask = rxWork();
            using (tx)
            {
                while (txCount < count)
                {
                    Assert.True(txCount.TryInto(out byte txCountU8));
                    var tryWrite = await tx.WriteAsync(new ReadOnlyMemory<byte>([txCountU8]));
                    if (!tryWrite.TryPickT0(out var writeCount, out var error))
                        throw error.AsException();
                    Assert.Equal((uint)1, writeCount);
                    txCount += 1;
                }
                this.output_.WriteLine("tx loop exits.");
            }
            await rxTask;
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
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    var r = await rx.ReadAsync(buff, cts.Token);
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
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                var d = await tx.WriteAsync(slice, cts.Token);
                Assert.True(d.IsT0);
                Assert.Equal((uint)slice.Length, d.AsT0);
            }
            await rxTask;
        }
    }
}
