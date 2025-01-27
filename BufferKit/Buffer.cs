namespace BufferKit
{
    using Cysharp.Threading.Tasks;

    using OneOf;

    using System.Diagnostics;

    public interface IBufferError
    {
        public Exception AsException();
    }

    /// <summary>
    /// 可支持且仅支持一对生产者和消费者的缓冲
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
        public UniTask<OneOf<ReadOnlyMemory<ReaderBuffSegm<T>>, IBufferError>> ReadAsync(NUsize length, CancellationToken token = default);

        /// <summary>
        /// 预览缓冲区中, 位于指定的偏移量以后的所有已填充的数据, 而不消费这些数据
        /// </summary>
        /// <param name="offset">要跳过的数据偏移量</param>
        /// <param name="token"></param>
        /// <returns></returns>
        public UniTask<OneOf<ReadOnlyMemory<PeekerBuffSegm<T>>, IBufferError>> PeekAsync(NUsize offset, CancellationToken token = default);

        /// <summary>
        /// 忽略内容，直接消耗不大于指定长度的已填充数据，通常与 PeekAsync 搭配使用。
        /// </summary>
        /// <param name="length"></param>
        /// <param name=""></param>
        /// <returns></returns>
        public UniTask<OneOf<NUsize, IBufferError>> ReaderSkipAsync(NUsize length, CancellationToken token = default);

        /// <summary>
        /// 从内部缓冲区中借出一段未填充缓存，该缓存长度不大于给定的长度；如果执行完成，则返回该未填充缓存
        /// </summary>
        /// <param name="length"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public UniTask<OneOf<ReadOnlyMemory<WriterBuffSegm<T>>, IBufferError>> WriteAsync(NUsize length, CancellationToken token = default);

        public bool IsRxClosed { get; }

        public bool IsTxClosed { get; }
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
        private IBuffer<T>? buff_;

        private readonly Action<IBuffer<T>>? closeOnDispose_;

        public BuffTx(IBuffer<T> buffer, Action<IBuffer<T>>? closeOnDispose = null)
        {
            this.buff_ = buffer;
            this.closeOnDispose_ = closeOnDispose;
        }

        public bool IsRxClosed
        {
            get
            {
                if (this.buff_ is IBuffer<T> buff)
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
        public UniTask<OneOf<ReadOnlyMemory<WriterBuffSegm<T>>, IBufferError>> WriteAsync(NUsize length, CancellationToken token = default)
        {
            if (this.buff_ is IBuffer<T> buff)
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
        public async UniTask<OneOf<NUsize, BuffIoError>> WriteAsync(ReadOnlyMemory<T> source, CancellationToken token = default)
        {
            if (this.buff_ is not IBuffer<T> buffer)
                throw CreateObjectDisposedException();

            NUsize filledCount = 0;
            NUsize sourceLength = (NUsize)source.Length;
            while (filledCount < sourceLength)
            {
                var unfilledLength = sourceLength - filledCount;
                var maybeArr = await buffer.WriteAsync(unfilledLength, token);
                if (!maybeArr.TryPickT0(out var txArr, out var writerErr))
                    return new BuffIoError { InnerError = OneOf<IBufferError, BuffSegmError>.FromT0(writerErr) };

                for (var i = 0; i < txArr.Length; i++)
                {
                    using var txBuff = txArr.Span[i];
                    Debug.Assert((uint)source.Length - filledCount >= txBuff.Length);
                    var unfilledSource = source.Slice(filledCount, txBuff.Length);
                    filledCount += txBuff.CopyFrom(unfilledSource); ;
                }
            }
            return filledCount;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            if (this.closeOnDispose_ is not Action<IBuffer<T>> closeTx)
                return;
            if (this.buff_ is IBuffer<T> buff)
            {
                closeTx(buff);
                this.buff_ = null;
            }
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
        private IBuffer<T>? buff_;

        private readonly Action<IBuffer<T>>? closeOnDispose_;

        public BuffRx(IBuffer<T> buffer, Action<IBuffer<T>>? closeOnDispose = null)
        {
            this.buff_ = buffer;
            this.closeOnDispose_ = closeOnDispose;
        }

        public bool IsTxClosed
        {
            get
            {
                if (this.buff_ is IBuffer<T> buff)
                    return buff.IsTxClosed;
                else
                    throw this.CreateObjectDisposedException();
            }
        }

        /// <summary>
        /// 从内部缓冲区中借出一段可供消费者读取的已填充缓存，该缓存长度不小于给定的长度；如果执行完成，则返回该已填充缓存
        /// </summary>
        /// <param name="length"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public UniTask<OneOf<ReadOnlyMemory<ReaderBuffSegm<T>>, IBufferError>> ReadAsync(NUsize length, CancellationToken token = default)
        {
            if (this.buff_ is IBuffer<T> buff)
                return buff.ReadAsync(length, token);
            else
                throw this.CreateObjectDisposedException();
        }

        /// <summary>
        /// 预览缓冲区中所有已填充的数据而不消费这些数据
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public UniTask<OneOf<ReadOnlyMemory<PeekerBuffSegm<T>>, IBufferError>> PeekAsync(NUsize offset, CancellationToken token = default)
        {
            if (this.buff_ is IBuffer<T> buff)
                return buff.PeekAsync(offset, token);
            else
                throw this.CreateObjectDisposedException();
        }

        /// <summary>
        /// 将已缓存数据以复制的方式填充到参数指定的外部缓存，返回已复制的数据量并消耗相应长度。
        /// </summary>
        /// <param name="target"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async UniTask<OneOf<NUsize, BuffIoError>> ReadAsync(Memory<T> target, CancellationToken token = default)
        {
            if (this.buff_ is not IBuffer<T> buffer)
                throw this.CreateObjectDisposedException();

            NUsize filledCount = 0;
            NUsize targetLength = (NUsize)target.Length;
            while (filledCount < targetLength)
            {
                var unfilledLength = targetLength - filledCount;
                var maybeArr = await buffer.ReadAsync(unfilledLength, token);
                if (!maybeArr.TryPickT0(out var rxArr, out var readerErr))
                    return new BuffIoError { InnerError = OneOf<IBufferError, BuffSegmError>.FromT0(readerErr) };

                for (var i = 0; i < rxArr.Length; i++)
                {
                    using var rxBuff = rxArr.Span[i];
                    Debug.Assert((uint)target.Length - filledCount >= rxBuff.Length);
                    var unfilledTarget = target.Slice(filledCount, rxBuff.Length);
                    filledCount += rxBuff.CopyTo(unfilledTarget);
                }
            }
            return filledCount;
        }

        public async UniTask<OneOf<NUsize, BuffIoError>> PeekAsync(NUsize offset, Memory<T> target, CancellationToken token = default)
        {
            if (this.buff_ is not IBuffer<T> buffer)
                throw this.CreateObjectDisposedException();

            NUsize filledCount = 0;
            NUsize targetLength = (NUsize)target.Length;
            while (filledCount < targetLength)
            {
                var maybeSlices = await buffer.PeekAsync(offset + filledCount, token);
                if (!maybeSlices.TryPickT0(out var slices, out var readerErr))
                    return new BuffIoError { InnerError = OneOf<IBufferError, BuffSegmError>.FromT0(readerErr) };

                for(var i = 0; i < slices.Length; ++i)
                {
                    var source = slices.Span[i];
                    var unfilledLength = targetLength - filledCount;
                    if (unfilledLength == 0)
                        break;
                    var copyLen = Math.Min(unfilledLength, source.Length);
                    var dst = target.Slice(filledCount, copyLen);
                    filledCount += source.CopyTo(dst);
                }
            }
            return filledCount;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            if (this.closeOnDispose_ is not Action<IBuffer<T>> closeRx)
                return;
            if (this.buff_ is IBuffer<T> buff)
            {
                closeRx(buff);
                this.buff_ = null;
            }
        }

        ~BuffRx()
            => this.Dispose();
    }

    internal static class CreateObjectDisposedExceptionExt
    {
        public static ObjectDisposedException CreateObjectDisposedException(this IDisposable obj)
            => new ObjectDisposedException(obj.GetType().ToString());
    }
}
