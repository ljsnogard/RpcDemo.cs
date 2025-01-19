namespace RpcClientSdk
{
    using BufferKit;

    using RpcNetSdk;

    public readonly partial struct ResponseStatus(ushort code)
    {
        public static readonly ushort Ok = 200;

        public static readonly ushort BadRequest = 400;
        public static readonly ushort Unauthorized = 401;
        public static readonly ushort NotFound = 404;

        public static readonly ushort ServerError = 500;

        public readonly ushort Code = code;
    }

    public readonly struct Response
    {
        public ResponseStatus Status { get; init; }

        public IAsyncEnumerable<HeaderRx> Headers { get; init; }

        public BuffRx<byte> Payload { get; init; }
    }
}
