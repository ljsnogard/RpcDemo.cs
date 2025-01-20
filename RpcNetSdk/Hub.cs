namespace RpcNetSdk
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    internal sealed class Hub
    {
        /// <summary>
        /// 以外部 port 为 Key 的已建立的 channel
        /// </summary>
        private readonly SortedDictionary<ushort, Channel> activeChannels_;

        /// <summary>
        /// 以外部 port 为 key 的半建立的 channel
        /// </summary>
        private readonly SortedDictionary<ushort, Channel> pendingChannels_;
    }
}
