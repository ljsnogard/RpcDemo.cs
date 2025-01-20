namespace RpcNetSdk
{
    using BufferKit;

    public readonly struct HeaderRx
    {
        public string Name { get; init; }

        public BuffRx<byte> Content { get; init; }
    }

    public readonly struct NoHeaders: IAsyncEnumerable<HeaderRx>
    {
        private static async IAsyncEnumerable<HeaderRx> YieldNothing()
        {
            await Task.Yield();
            yield break;
        }

        public static readonly NoHeaders Instance = new NoHeaders();

        public IAsyncEnumerator<HeaderRx> GetAsyncEnumerator(CancellationToken token = default)
            => YieldNothing().GetAsyncEnumerator(token);
    }
}
