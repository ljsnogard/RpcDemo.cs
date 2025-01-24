namespace RpcMuxSdk
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public sealed class Listener
    {
        private readonly PortBinder bindPort_;

        public ushort LocalPort
            => this.bindPort_.LocalPort;
    }
}
