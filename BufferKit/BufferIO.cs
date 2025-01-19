namespace BufferKit
{
    using Cysharp.Threading.Tasks;

    using OneOf;

    public readonly struct BuffTxError
    { }

    public struct BuffTx<T>
    {
        public UniTask<OneOf<BuffSegmMut<T>, BuffTxError>> WriteAsync(uint length, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }
    }

    public readonly struct BuffRxError
    { }

    public struct BuffRx<T>
    {
        /// <summary>
        /// 从缓冲区中取出不大于指定长度的，且已填充数据的，用于读取的 buffer
        /// </summary>
        /// <param name="length">要取出的最大长度</param>
        /// <param name="token">取消任务的令牌</param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public UniTask<OneOf<BuffSegmRef<T>, BuffRxError>> ReadAsync(uint length, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }
    }
}
