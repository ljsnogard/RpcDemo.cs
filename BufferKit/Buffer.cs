namespace BufferKit
{
    using Cysharp.Threading.Tasks;

    using OneOf;

    public interface IBufferError
    {
        public Exception AsException();
    }

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

    public readonly struct BuffIoError : IBufferError
    {
        public OneOf<IBufferError, BuffSegmError> InnerError { get; init; }

        public Exception AsException()
        {
            if (this.InnerError.TryPickT0(out var buffErr, out var segmErr))
                return buffErr.AsException();
            else
                return segmErr.CreateException();
        }
    }

    /// <summary>
    /// 缓冲区的生产者端
    /// </summary>
    /// <typeparam name="T">缓冲区的数据单元类型</typeparam>
    /// <typeparam name="E">缓冲区的错误类型</typeparam>
    public sealed class BuffTx<T> : IDisposable
    {
        private IBufferInternal<T>? buff_;

        private readonly bool shouldCloseOnDispose_;

        internal BuffTx(IBufferInternal<T> buffer, bool shouldCloseOnDispose = true)
        {
            this.buff_ = buffer;
            this.shouldCloseOnDispose_ = shouldCloseOnDispose;
        }

        public bool IsRxClosed
        {
            get
            {
                if (this.buff_ is IBufferInternal<T> buff)
                    return buff.IsRxClosed;
                else
                    throw CreateObjectDisposedException();
            }
        }

        /// <summary>
        /// 从内部缓冲区中借出一段未填充缓存，该缓存长度不小于给定的长度；如果执行完成，则返回该未填充缓存
        /// </summary>
        /// <param name="length"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public UniTask<OneOf<BuffSegmMut<T>, IBufferError>> WriteAsync(uint length, CancellationToken token = default)
        {
            if (this.buff_ is IBufferInternal<T> buff)
                return buff.WriteAsync(length, token);
            else
                throw CreateObjectDisposedException();
        }

        /// <summary>
        /// 将参数所指定的外部缓存中的数据以复制的方式填充到内部缓存中，并返回已复制的长度
        /// </summary>
        /// <param name="source"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async UniTask<OneOf<uint, BuffIoError>> DumpAsync(ReadOnlyMemory<T> source, CancellationToken token = default)
        {
            if (this.buff_ is not IBufferInternal<T> buffer)
                throw CreateObjectDisposedException();

            uint filledCount = 0;
            uint sourceLength = (uint)source.Length;
            while (filledCount < sourceLength)
            {
                var unfilledLength = sourceLength - filledCount;
                var maybeBuff = await buffer.WriteAsync(unfilledLength, token);
                if (!maybeBuff.TryPickT0(out var txBuff, out var writerErr))
                    throw writerErr.CreateException();
                using (txBuff)
                {
                    var unfilledSource = source.Slice((int)filledCount);
                    filledCount += txBuff.CopyFrom(unfilledSource);
                }
            }
            return filledCount;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            if (!this.shouldCloseOnDispose_)
                return;
            if (this.buff_ is IBufferInternal<T> buff)
                buff.TrySetTxClosed();
        }

        ~BuffTx()
            => this.Dispose();

        static ObjectDisposedException CreateObjectDisposedException()
            => new ObjectDisposedException(typeof(BuffTx<T>).ToString());
    }

    /// <summary>
    /// 缓冲区的消费者端
    /// </summary>
    /// <typeparam name="T">缓冲区的数据单元类型</typeparam>
    public sealed class BuffRx<T> : IDisposable
    {
        private IBufferInternal<T>? buff_;

        private readonly bool shouldCloseOnDispose_;

        internal BuffRx(IBufferInternal<T> buffer, bool shouldCloseOnDispose = true)
        {
            this.buff_ = buffer;
            this.shouldCloseOnDispose_ = shouldCloseOnDispose;
        }

        public bool IsTxClosed
        {
            get
            {
                if (this.buff_ is IBufferInternal<T> buff)
                    return buff.IsTxClosed;
                else
                    throw CreateObjectDisposedException();
            }
        }

        /// <summary>
        /// 从内部缓冲区中借出一段可供消费者读取的已填充缓存，该缓存长度不小于给定的长度；如果执行完成，则返回该已填充缓存
        /// </summary>
        /// <param name="length"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public UniTask<OneOf<BuffSegmRef<T>, IBufferError>> ReadAsync(uint length, CancellationToken token = default)
        {
            if (this.buff_ is IBufferInternal<T> buff)
                return buff.ReadAsync(length, token);
            else
                throw CreateObjectDisposedException();
        }

        /// <summary>
        /// 将已缓存数据以复制的方式填充到参数指定的外部缓存，并返回已复制的长度
        /// </summary>
        /// <param name="target"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async UniTask<OneOf<uint, BuffIoError>> FillAsync(Memory<T> target, CancellationToken token = default)
        {
            if (this.buff_ is not IBufferInternal<T> buffer)
                throw CreateObjectDisposedException();

            uint filledCount = 0;
            uint targetLength = (uint)target.Length;
            while (filledCount < targetLength)
            {
                var unfilledLength = targetLength - filledCount;
                var maybeBuff = await buffer.ReadAsync(unfilledLength, token);
                if (!maybeBuff.TryPickT0(out var rxBuff, out var readerErr))
                    throw readerErr.CreateException();

                using (rxBuff)
                {
                    var unfilledTarget = target.Slice((int)filledCount, (int)rxBuff.Length);
                    filledCount += rxBuff.CopyTo(unfilledTarget);
                }
            }
            return filledCount;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            if (!this.shouldCloseOnDispose_)
                return;
            if (this.buff_ is IBufferInternal<T> buff)
                buff.TrySetRxClosed();
        }

        ~BuffRx()
            => this.Dispose();

        static ObjectDisposedException CreateObjectDisposedException()
            => new ObjectDisposedException(typeof(BuffTx<T>).ToString());
    }

    internal static class BuffErrorExt
    {
        public static Exception CreateException(this IBufferError error)
            => new Exception($"{error.GetType()}({error.ToString()})");
    }
}
