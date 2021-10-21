﻿using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.IO;
using System.Net.Http;
using Common.Networking.Packet;
using Common.Networking.IO;
using ENet;
using GameServer.Server.Packets;
using GameServer.Logging;
using GameServer.Utilities;
using Common.Game;

namespace GameServer.Server
{
    public class ENetServer
    {
        public static ConcurrentBag<Event> Incoming { get; private set; }
        public static ConcurrentQueue<ENetCmds> ENetCmds { get; private set; }
        public static Dictionary<uint, Player> Players { get; private set; }
        public static Dictionary<ServerOpcode, ENetCmd> ENetCmd { get; private set; }
        public static Dictionary<ClientOpcode, HandlePacket> HandlePacket { get; private set; }
        public static HttpClient WebClient { get; private set; }
        public static ServerVersion ServerVersion { get; private set; }
        public static Dictionary<ResourceType, ResourceInfo> ResourceInfoData { get; private set; }
        public static Dictionary<StructureType, StructureInfo> StructureInfoData { get; private set; }

        #region WorkerThread
        public static void WorkerThread() 
        {
            Thread.CurrentThread.Name = "SERVER";

            ResourceInfoData = typeof(ResourceInfo).Assembly.GetTypes().Where(x => typeof(ResourceInfo).IsAssignableFrom(x) && !x.IsAbstract).Select(Activator.CreateInstance).Cast<ResourceInfo>()
                .ToDictionary(x => (ResourceType)Enum.Parse(typeof(ResourceType), x.GetType().Name.Replace(typeof(ResourceInfo).Name, "")), x => x);
            StructureInfoData = typeof(StructureInfo).Assembly.GetTypes().Where(x => typeof(StructureInfo).IsAssignableFrom(x) && !x.IsAbstract).Select(Activator.CreateInstance).Cast<StructureInfo>()
                .ToDictionary(x => (StructureType)Enum.Parse(typeof(StructureType), x.GetType().Name.Replace(typeof(StructureInfo).Name, "")), x => x);

            FileManager.SetupDirectories();
            FileManager.CreateConfig("banned_players", FileManager.ConfigType.Array);

            ServerVersion = new()
            {
                Major = 0,
                Minor = 1,
                Patch = 0
            };

            Incoming = new();
            ENetCmds = new();
            Players = new();
            WebClient = new();

            HandlePacket = typeof(HandlePacket).Assembly.GetTypes().Where(x => typeof(HandlePacket).IsAssignableFrom(x) && !x.IsAbstract).Select(Activator.CreateInstance).Cast<HandlePacket>()
                .ToDictionary(x => x.Opcode, x => x);

            ENetCmd = typeof(ENetCmd).Assembly.GetTypes().Where(x => typeof(ENetCmd).IsAssignableFrom(x) && !x.IsAbstract).Select(Activator.CreateInstance).Cast<ENetCmd>()
                .ToDictionary(x => x.Opcode, x => x);

            Library.Initialize();

            var maxClients = 100;
            ushort port = 25565;

            using (var server = new Host())
            {
                var address = new Address
                {
                    Port = port
                };

                server.Create(address, maxClients);

                Logger.Log($"Listening on port {port}");

                while (!Console.KeyAvailable)
                {
                    var polled = false;

                    // Server Instructions
                    while (ENetCmds.TryDequeue(out ENetCmds result))
                    {
                        foreach (var cmd in result.Instructions)
                        {
                            var opcode = cmd.Key;

                            ENetCmd[opcode].Handle(cmd.Value);
                        }
                    }

                    // Incoming
                    while (Incoming.TryTake(out Event netEvent))
                    {
                        var peer = netEvent.Peer;
                        var packetSizeMax = 2048;
                        var readBuffer = new byte[packetSizeMax];
                        var packetReader = new PacketReader(readBuffer);
                        packetReader.BaseStream.Position = 0;

                        netEvent.Packet.CopyTo(readBuffer);

                        var opcode = (ClientOpcode)packetReader.ReadByte();

                        HandlePacket[opcode].Handle(netEvent, ref packetReader);

                        packetReader.Dispose();
                        netEvent.Packet.Dispose();
                    }

                    while (!polled)
                    {
                        if (server.CheckEvents(out Event netEvent) <= 0)
                        {
                            if (server.Service(15, out netEvent) <= 0)
                                break;

                            polled = true;
                        }

                        var eventType = netEvent.Type;

                        if (eventType == EventType.None) 
                        {
                            Logger.LogWarning("Received EventType.None");
                        }

                        if (eventType == EventType.Connect) 
                        {
                            var bannedPlayers = FileManager.ReadConfig<List<BannedPlayer>>("banned_players");
                            var bannedPlayer = bannedPlayers.Find(x => x.Ip == netEvent.Peer.IP);

                            if (bannedPlayer == null)
                            {
                                // Player is not banned, set timeout delays for player timeout
                                netEvent.Peer.Timeout(32, 1000, 4000);
                            }
                            else 
                            {
                                // Player is banned, disconnect them immediately 
                                netEvent.Peer.DisconnectNow((uint)DisconnectOpcode.Banned);
                                Logger.Log($"Player '{bannedPlayer.Name}' tried to join but is banned");
                            }
                        }

                        if (eventType == EventType.Disconnect) 
                        {
                            var player = Players[netEvent.Peer.ID];

                            PlayerManager.UpdatePlayerConfig(player);

                            // Remove player from player list
                            Players.Remove(netEvent.Peer.ID);

                            Logger.Log($"Player '{(player == null ? netEvent.Peer.ID : player.Username)}' disconnected");
                        }

                        if (eventType == EventType.Timeout) 
                        {
                            var player = Players[netEvent.Peer.ID];

                            PlayerManager.UpdatePlayerConfig(player);

                            // Remove player from player list
                            Players.Remove(netEvent.Peer.ID);

                            Logger.Log($"Player '{(player == null ? netEvent.Peer.ID : player.Username)}' timed out");
                        }

                        if (eventType == EventType.Receive) 
                        {
                            //Logger.Log("Packet received from - ID: " + netEvent.Peer.ID + ", IP: " + netEvent.Peer.IP + ", Channel ID: " + netEvent.ChannelID + ", Data length: " + netEvent.Packet.Length);

                            Incoming.Add(netEvent);
                        }
                    }
                }

                server.Flush();
            }

            Library.Deinitialize();
        }

        public static void Send(GamePacket gamePacket, Peer peer, PacketFlags packetFlags)
        {
            // Send data to a specific client (peer)
            var packet = default(Packet);
            packet.Create(gamePacket.Data, packetFlags);
            byte channelID = 0;
            peer.Send(channelID, ref packet);
        }

        public static void SaveAllPlayersToDatabase()
        {
            /*if (Players.Count == 0)
                return;

            Logger.Log($"Saving {Players.Count} players to the database");

            var playersThatAreNotInDatabase = new List<Player>();

            foreach (var player in Players)
            {
                foreach (var dbPlayer in db.Players.ToList())
                {
                    if (player.Username == dbPlayer.Username)
                    {
                        player.AddResourcesGeneratedFromStructures();
                        UpdatePlayerValuesInDatabase(dbPlayer, player);
                        break;
                    }

                    playersThatAreNotInDatabase.Add(player);
                }
            }

            foreach (var player in playersThatAreNotInDatabase)
            {
                player.AddResourcesGeneratedFromStructures();
                db.Add((ModelPlayer)player);
            }

            db.SaveChanges();*/
        }
        #endregion
    }

    public struct ServerVersion
    {
        public byte Major { get; set; }
        public byte Minor { get; set; }
        public byte Patch { get; set; }
    }

    public class ENetCmds 
    {
        public Dictionary<ServerOpcode, List<object>> Instructions { get; set; }

        public ENetCmds()
        {
            Instructions = new Dictionary<ServerOpcode, List<object>>();
        }

        public ENetCmds(ServerOpcode opcode)
        {
            Instructions = new Dictionary<ServerOpcode, List<object>>
            {
                [opcode] = null
            };
        }

        public void Set(ServerOpcode opcode, params object[] data)
        {
            Instructions[opcode] = new List<object>(data);
        }
    }

    public enum ServerOpcode 
    {
        GetOnlinePlayers,
        GetPlayerStats,
        KickPlayer,
        BanPlayer,
        PardonPlayer,
        ClearPlayerStats
    }
}
