namespace BufferKit
{
    public sealed class RingBuffer<T>
    {
        private readonly T[] memory_;

        public RingBuffer(T[] memory)
        {
            this.memory_ = memory;
        }
    }
}
