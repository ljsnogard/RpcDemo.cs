namespace BufferKit
{
    using Cysharp.Threading.Tasks;

    using OneOf;

    public interface IBufferError
    { }

    /// <summary>
    /// 可支持一对且仅支持一对生产者和消费者的缓冲
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IBuffer<T>
    {
        /// <summary>
        /// 从内部缓冲区中借出一段可供消费者读取的已填充缓存，该缓存长度不大于给定的长度；如果执行完成，则返回该已填充缓存
        /// </summary>
        /// <param name="length"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public UniTask<OneOf<BuffSegmRef<T>, IBufferError>> ReadAsync(uint length, CancellationToken token = default);

        /// <summary>
        /// 从内部缓冲区中借出一段未填充缓存，该缓存长度不大于给定的长度；如果执行完成，则返回该未填充缓存
        /// </summary>
        /// <param name="length"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public UniTask<OneOf<BuffSegmMut<T>, IBufferError>> WriteAsync(uint length, CancellationToken token = default);

        public bool IsRxClosed { get; }

        public bool IsTxClosed { get; }
    }

    internal interface IBufferInternal<T> : IBuffer<T>
    {
        public void TrySetRxClosed();

        public void TrySetTxClosed();
    }

    public readonly struct BuffTxError
    {
        public OneOf<IBufferError, BuffSegmError> InnerError { get; init; }
    }

    /// <summary>
    /// 缓冲区的生产者端
    /// </summary>
    /// <typeparam name="T">缓冲区的数据单元类型</typeparam>
    /// <typeparam name="E">缓冲区的错误类型</typeparam>
    public struct BuffTx<T> : IDisposable
    {
        private readonly IBufferInternal<T> buff_;

        private readonly bool shouldCloseOnDispose;

        internal BuffTx(IBufferInternal<T> buffer, bool shouldCloseOnDispose = true)
        {
            this.buff_ = buffer;
            this.shouldCloseOnDispose = shouldCloseOnDispose;
        }

        /// <summary>
        /// 从内部缓冲区中借出一段未填充缓存，该缓存长度不小于给定的长度；如果执行完成，则返回该未填充缓存
        /// </summary>
        /// <param name="length"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public UniTask<OneOf<BuffSegmMut<T>, IBufferError>> WriteAsync(uint length, CancellationToken token = default)
            => this.buff_.WriteAsync(length, token);

        /// <summary>
        /// 将外部缓存中的数据以复制的方式填充到内部缓存中，并返回已复制的长度
        /// </summary>
        /// <param name="source"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async UniTask<OneOf<uint, BuffTxError>> WriteAsync(ReadOnlyMemory<T> source, CancellationToken token = default)
        {
            uint filledCounut = 0;
            uint sourceLength = (uint)source.Length;
            while (filledCounut < sourceLength)
            {
                var unfilledLength = sourceLength - filledCounut;
                var maybeBuff = await this.buff_.WriteAsync(unfilledLength, token);
                if (!maybeBuff.TryPickT0(out var buff, out var writerErr))
                    throw writerErr.CreateException();

                var unfilledSource = source.Slice((int)filledCounut, (int)buff.Length);
                var maybeSlice = buff.BorrowSlice(unfilledLength);
                if (!maybeSlice.TryPickT0(out var slice, out var segmErr))                
                    return new BuffTxError { InnerError = segmErr };
                
                using (slice)
                {
                    for (var i = 0; i < unfilledSource.Length; ++i)
                        slice.Memory.Span[i] = unfilledSource.Span[i];
                    filledCounut += unfilledLength;
                }
            }
            return filledCounut;
        }

        void IDisposable.Dispose()
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 缓冲区的消费者端
    /// </summary>
    /// <typeparam name="T">缓冲区的数据单元类型</typeparam>
    public struct BuffRx<T> : IDisposable
    {
        private readonly IBufferInternal<T> buff_;

        private readonly bool shouldCloseOnDispose_;

        internal BuffRx(IBufferInternal<T> buffer, bool shouldCloseOnDispose = true)
        {
            this.buff_ = buffer;
            this.shouldCloseOnDispose_ = shouldCloseOnDispose;
        }

        /// <summary>
        /// 从内部缓冲区中借出一段可供消费者读取的已填充缓存，该缓存长度不小于给定的长度；如果执行完成，则返回该已填充缓存
        /// </summary>
        /// <param name="length"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public UniTask<OneOf<BuffSegmRef<T>, IBufferError>> ReadAsync(uint length, CancellationToken token = default)
            => this.buff_.ReadAsync(length, token);

        /// <summary>
        /// 将已缓存数据以复制的方式填充到指定的外部缓存，并返回已复制的长度
        /// </summary>
        /// <param name="target"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async UniTask<OneOf<uint, BuffTxError>> ReadAsync(Memory<T> target, CancellationToken token = default)
        {
            uint filledCounut = 0;
            uint targetLength = (uint)target.Length;
            while (filledCounut < targetLength)
            {
                var unfilledLength = targetLength - filledCounut;
                var maybeBuff = await this.buff_.ReadAsync(unfilledLength, token);
                if (!maybeBuff.TryPickT0(out var buff, out var readerErr))
                    throw readerErr.CreateException();

                var unfilledTarget = target.Slice((int)filledCounut, (int)buff.Length);
                var maybeSlice = buff.BorrowSlice(unfilledLength);
                if (!maybeSlice.TryPickT0(out var slice, out var segmErr))
                    return new BuffTxError { InnerError = segmErr };

                using (slice)
                {
                    for (var i = 0; i < unfilledTarget.Length; ++i)
                        unfilledTarget.Span[i] = slice.ReadOnlyMemory.Span[i];

                    filledCounut += unfilledLength;
                }
            }
            return filledCounut;
        }

        void IDisposable.Dispose()
            => throw new NotImplementedException();
    }

    internal static class BuffErrorExt
    {
        public static Exception CreateException(this IBufferError error)
            => new Exception($"{error.GetType()}({error.ToString()})");
    }
}
