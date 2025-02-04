﻿namespace RpcClientSdk
{
    using Cysharp.Threading.Tasks;

    using OneOf;

    using RpcMuxSdk;

    using BufferKit;

    public readonly struct StreamStatus(byte code)
    {
        public readonly byte Code = code;
    }

    /// <summary>
    /// 从服务端拉取的数据包
    /// </summary>
    public readonly struct StreamItem
    {
        public StreamStatus Status { get; init; }

        /// <summary>
        /// 推送数据包的头部
        /// </summary>
        public IAsyncEnumerable<Header> Headers { get; init; }

        /// <summary>
        /// 推送数据包的主体数据
        /// </summary>
        public BuffTx<byte> Payload { get; init; }
    }

    public readonly struct PullError
    { }

    public interface IPullAgent
    {
        public Uri Location { get; }

        public UniTask<OneOf<StreamItem, PullError>> ReceiveAsync(CancellationToken token = default);
    }
}
