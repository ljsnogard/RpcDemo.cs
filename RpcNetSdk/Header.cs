namespace RpcNetSdk
{
    public readonly struct Header
    {
        public string Name { get; init; }

        public ReadOnlyMemory<byte> Content { get; init; }
    }

    public readonly struct NoHeaders: IAsyncEnumerable<Header>
    {
        private static async IAsyncEnumerable<Header> YieldNothing()
        {
            await Task.Yield();
            yield break;
        }

        public static readonly NoHeaders Instance = new NoHeaders();

        public IAsyncEnumerator<Header> GetAsyncEnumerator(CancellationToken token = default)
            => YieldNothing().GetAsyncEnumerator(token);
    }
}
