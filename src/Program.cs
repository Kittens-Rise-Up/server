﻿using System;
using System.IO;
using Common.Networking.Packet;
using Common.Networking.IO;
using ENet;
using GameServer.Packets;

namespace GameServer
{
    class Program
    {
        static void Main(string[] args)
        {
			Library.Initialize();

			var maxClients = 100;
			ushort port = 8888;

			using (var server = new Host())
			{
                var address = new Address
                {
                    Port = port
                };

                server.Create(address, maxClients);

                while (!Console.KeyAvailable)
                {
                    var polled = false;

                    while (!polled)
                    {
                        if (server.CheckEvents(out Event netEvent) <= 0)
                        {
                            if (server.Service(15, out netEvent) <= 0)
                                break;

                            polled = true;
                        }

                        switch (netEvent.Type)
                        {
                            case EventType.None:
                                break;

                            case EventType.Connect:
                                Console.WriteLine("Client connected - ID: " + netEvent.Peer.ID + ", IP: " + netEvent.Peer.IP);
                                break;

                            case EventType.Disconnect:
                                Console.WriteLine("Client disconnected - ID: " + netEvent.Peer.ID + ", IP: " + netEvent.Peer.IP);
                                break;

                            case EventType.Timeout:
                                Console.WriteLine("Client timeout - ID: " + netEvent.Peer.ID + ", IP: " + netEvent.Peer.IP);
                                break;

                            case EventType.Receive:
                                Console.WriteLine("Packet received from - ID: " + netEvent.Peer.ID + ", IP: " + netEvent.Peer.IP + ", Channel ID: " + netEvent.ChannelID + ", Data length: " + netEvent.Packet.Length);

                                var packet = netEvent.Packet;

                                var readBuffer = new byte[1024];
                                var readStream = new MemoryStream(readBuffer);
                                var reader = new BinaryReader(readStream);

                                readStream.Position = 0;
                                netEvent.Packet.CopyTo(readBuffer);
                                //var packetID = (ClientPacketType)reader.ReadByte();

                                //Console.WriteLine(packetID);

                                var data = new PacketPurchaseItem();
                                var packetReader = new PacketReader(readBuffer);
                                data.Read(packetReader);

                                Console.WriteLine(data.m_ID);
                                Console.WriteLine(data.m_ItemID);

                                packet.Dispose();
                                break;
                        }
                    }
                }

                server.Flush();
			}

			Library.Deinitialize();
		}
    }
}
