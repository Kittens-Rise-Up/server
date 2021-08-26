﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Common.Networking.Packet;
using Common.Networking.IO;
using ENet;
using GameServer.Database;
using GameServer.Logging;

namespace GameServer.Server
{
    public abstract class ServerInstruction
    {
        public abstract ServerInstructionOpcode Opcode { get; set; }

        public abstract void Handle(List<object> value);
    }
}
