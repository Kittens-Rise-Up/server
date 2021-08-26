﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Common.Networking.Packet;
using Common.Networking.IO;
using ENet;
using GameServer.Database;
using GameServer.Logging;
using GameServer.Utilities;

namespace GameServer.Server
{
    public class ServerInstructionBanPlayer : ServerInstruction
    {
        public override ServerInstructionOpcode Opcode { get; set; }

        public ServerInstructionBanPlayer() 
        {
            Opcode = ServerInstructionOpcode.BanPlayer;
        }

        public override void Handle(List<object> value)
        {
            var username = value[0].ToString();

            var bannedOnline = Utils.BanOnlinePlayer(username);

            if (!bannedOnline)
                Utils.BanOfflinePlayer(username);
        }
    }
}
