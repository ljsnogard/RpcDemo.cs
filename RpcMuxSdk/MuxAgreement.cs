namespace RpcMuxSdk
{
    public readonly struct MuxAgreement
    {
        /// <summary>
        /// The max packet size that the multiplexer can send to the remote
        /// </summary>
        public UInt32 MaxTxPacketSize { get; init; }

        /// <summary>
        /// The max packet size that the multiplexer can receive from the remote
        /// </summary>
        public UInt32 MaxRxPacketSize { get; init; }

        /// <summary>
        /// The max time interval (in seconds) that a channel will live without any traffic sent or received.
        /// </summary>
        public UInt32 ChannelTimeout { get; init; }
    }
}
