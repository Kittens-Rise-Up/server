﻿using Version = Common.Netcode.Version;

using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GameServer.Console;
using GameServer.Server.Packets;
using Common.Netcode;
using ENet;
using GameServer.Server.Game;
using Common.Game;
using GameServer.Server.MongoDb;
using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GameServer.Server
{
    public class TestModel 
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }
        public string Name { get; set; }
    }
    public class ENetServer
    {
        public static Version Version { get; private set; }
        public static ConcurrentQueue<ENetCommand> ENetCmds { get; private set; }
        public static ConcurrentQueue<ServerPacket> Outgoing { get; private set; }

        public static Dictionary<uint, ServerPlayer> Players { get; private set; }
        public static Dictionary<uint, Channel> Channels { get; private set; }
        public static uint ChannelId = 0;

        private static ConcurrentBag<Event> Incoming { get; set; }
        private static Dictionary<ENetOpcode, ENetCmd> ENetCmd { get; set; }
        private static Dictionary<ClientPacketOpcode, HandlePacket> HandlePacket { get; set; }

        public static async void ENetThreadWorker(ushort port, int maxClients) 
        {
            Thread.CurrentThread.Name = "SERVER";

            if (!Database.Connect()) 
            {
                Logger.Log("Failed to connect to database");
                Logger.LogRaw("\nExiting application in 3 seconds...");
                await Task.Delay(3000);
                Environment.Exit(0);
                return;
            }

            var db = Database.DbClient.GetDatabase("database");
            var collection = db.GetCollection<TestModel>("players");
            await collection.InsertOneAsync(new TestModel {
                Name = "Tester"
            });

            var results = await collection.FindAsync(_ => true);

            foreach (var result in results.ToList()) 
            {
                Logger.Log(result.Name);
            }
            Logger.Log(collection);

            Version = new() { Major = 0, Minor = 1, Patch = 0 };
            ENetCmds = new();
            Outgoing = new();
            Players = new();
            Channels = new() { 
                { ChannelId++, new Channel { Name = "Global" } },
                { ChannelId++, new Channel { Name = "Game"   } }
            };
            Incoming = new();
            ENetCmd = typeof(ENetCmd).Assembly.GetTypes().Where(x => typeof(ENetCmd).IsAssignableFrom(x) && !x.IsAbstract).Select(Activator.CreateInstance).Cast<ENetCmd>().ToDictionary(x => x.Opcode, x => x);
            HandlePacket = typeof(HandlePacket).Assembly.GetTypes().Where(x => typeof(HandlePacket).IsAssignableFrom(x) && !x.IsAbstract).Select(Activator.CreateInstance).Cast<HandlePacket>().ToDictionary(x => x.Opcode, x => x);

            Library.Initialize();

            using Host server = new();
            Address address = new();

            address.Port = port;
            server.Create(address, maxClients);

            Logger.Log($"Server listening on port {port}");

            while (!System.Console.KeyAvailable)
            {
                bool polled = false;

                // ENet Cmds
                while (ENetCmds.TryDequeue(out ENetCommand cmd)) 
                    ENetCmd[cmd.Opcode].Handle(cmd.Data);

                // Incoming
                while (Incoming.TryTake(out Event netEvent)) 
                {
                    var packet = netEvent.Packet;
                    var packetReader = new PacketReader(packet);
                    var opcode = (ClientPacketOpcode)packetReader.ReadByte();

                    Logger.Log($"Received New Client Packet: {opcode}");

                    HandlePacket[opcode].Handle(netEvent.Peer, packetReader); // is ref needed for the packetReader param?
                    packetReader.Dispose(); // is this right?
                }

                // Outgoing
                while (Outgoing.TryDequeue(out ServerPacket packet)) 
                {
                    foreach (var peer in packet.Peers)
                        Send(packet, peer);
                }

                while (!polled)
                {
                    if (server.CheckEvents(out Event netEvent) <= 0)
                    {
                        if (server.Service(15, out netEvent) <= 0)
                            break;

                        polled = true;
                    }

                    var peer = netEvent.Peer;
                    var eventType = netEvent.Type;

                    if (eventType == EventType.Receive) 
                    {
                        // Receive
                        var packet = netEvent.Packet;
                        if (packet.Length > GamePacket.MaxSize)
                        {
                            Logger.LogWarning($"Tried to read packet from server of size {packet.Length} when max packet size is {GamePacket.MaxSize}");
                            packet.Dispose();
                            continue;
                        }

                        Incoming.Add(netEvent);
                    }
                    else if (eventType == EventType.Connect) 
                    {
                        // Connect
                    }
                    else if (eventType == EventType.Disconnect) 
                    {
                        // Disconnect
                        Logger.Log($"Player '{ENetServer.Players[peer.ID].Username}' disconnected");
                        HandlePlayerLeftServerCleanup(peer.ID);
                    }
                    else if (eventType == EventType.Timeout) 
                    {
                        // Timeout
                        Logger.Log($"Player '{ENetServer.Players[peer.ID].Username}' timed out");
                        HandlePlayerLeftServerCleanup(peer.ID);
                    }
                }
            }

            server.Flush();
        }

        private static void HandlePlayerLeftServerCleanup(uint peerId) 
        {
            Players[peerId].SaveConfig();
            Players.Remove(peerId);
            //Channels[(uint)SpecialChannel.Global].Users.Remove(peerId);
            /*Outgoing.Enqueue(new ServerPacket((byte)ServerPacketOpcode.PlayerJoinLeave, new WPacketPlayerJoinLeave
            {
                JoinLeaveOpcode = JoinLeaveOpcode.Leave,
                PlayerId = peerId
            }, GetOtherPeers(peerId)));*/
        }

        private static Peer[] GetOtherPeers(uint peerId)
        {
            var peers = new List<Peer>();
            foreach (var pair in ENetServer.Players) 
                if (pair.Key != peerId)
                    peers.Add(pair.Value.Peer);

            return peers.ToArray();
        }

        private static void Send(ServerPacket gamePacket, Peer peer) 
        {
            var packet = default(Packet);
            packet.Create(gamePacket.Data, gamePacket.PacketFlags);
            byte channelID = 0;
            peer.Send(channelID, ref packet);
        }
    }

    public class ENetCommand
    {
        public ENetOpcode Opcode { get; set; }
        public List<object> Data { get; set; }
    }

    public enum ENetOpcode 
    {
        GetOnlinePlayers,
        GetPlayerStats,
        KickPlayer,
        BanPlayer,
        PardonPlayer,
        ClearPlayerStats,
        SendPlayerData
    }
}
