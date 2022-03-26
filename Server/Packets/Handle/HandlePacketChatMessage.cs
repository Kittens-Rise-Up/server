﻿using Common.Netcode;
using ENet;
using GameServer.Console;
using GameServer.Utils;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Runtime;
using GameServer.Server.Game;
using GameServer.Server.Packets;
using Common.Game;

namespace GameServer.Server.Packets
{
    public class HandlePacketChatMessage : HandlePacket
    {
        public override ClientPacketOpcode Opcode { get; set; }

        public HandlePacketChatMessage() => Opcode = ClientPacketOpcode.ChatMessage;

        public override void Handle(Peer peer, PacketReader packetReader)
        {
            var data = new RPacketChatMessage();
            data.Read(packetReader);

            Logger.Log(data.ChannelId);
            Logger.Log(data.Message);

            //ENetServer.Outgoing.Enqueue(new ServerPacket((byte)ServerPacketOpcode.ChatMessage, packetData, peer));
        }
    }
}
