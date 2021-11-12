﻿using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Packets;
using Archipelago.RiskOfRain2.Enums;
using Archipelago.RiskOfRain2.Extensions;
using Archipelago.RiskOfRain2.Handlers;
using R2API.Utils;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Archipelago.RiskOfRain2
{
    internal class ArchipelagoClient
    {
        public delegate void ClientDisconnected(ushort code, string reason, bool wasClean);
        public event ClientDisconnected OnClientDisconnect;

        public ArchipelagoSession Session { get; private set; }
        public ReceivedItemsHandler Items { get; private set; }
        public LocationChecksHandler Locations { get; private set; }
        public UIModuleHandler UI { get; private set; }
        public Color AccentColor { get; private set; }
        public bool ClientSideMode { get; private set; }

        private bool enableDeathLink;
        private DeathLinkDifficulty deathlinkDifficulty;
        private DeathLinkService deathLinkService;

        public ArchipelagoClient()
        {
            UI = new UIModuleHandler(this);
        }

        public void InitializeForClientsidePlayer()
        {
            ClientSideMode = true;
            Session = null;
            Items = null;
            Locations = null;

            UI.Hook();
        }

        public bool Connect(string hostname, int port, string slotName, string password = null, List<string> tags = null)
        {
            Log.LogDebug($"Attempting connection to new session. Host: {hostname}:{port} Slot: {slotName}");
            Session = ArchipelagoSessionFactory.CreateSession(hostname, port);
            Items = new ReceivedItemsHandler(Session.Items);
            Locations = new LocationChecksHandler(Session.Locations);
            Session.Socket.SocketClosed += Socket_SocketClosed;
            Session.Socket.PacketReceived += Socket_PacketReceived;

            if (enableDeathLink)
            {
                tags.Add("DeathLink");
            }

            if (!Session.TryConnectAndLogin("Risk of Rain 2", slotName, new Version(0, 2, 0), tags, Guid.NewGuid().ToString(), password))
            {
                ChatMessage.SendColored($"Failed to connect to Archipelago at {hostname}:{port} for slot {slotName}. Restart your run to try again. (Sorry)", Color.red);
                return false;
            }

            if (enableDeathLink)
            {
                deathLinkService = Session.CreateDeathLinkServiceAndEnable();
            }

            Items.Hook();
            Locations.Hook();
            UI.Hook();

            ChatMessage.SendColored($"Succesfully connected to Archipelago at {hostname}:{port} for slot {slotName}.", Color.green);
            return true;
        }

        public void Disconnect()
        {
            Session.Socket.DisconnectAsync();
            RunDisconnectProcedure();
            Session.Socket.SocketClosed -= Socket_SocketClosed;
            Session.Socket.PacketReceived -= Socket_PacketReceived;
        }

        public void EnableDeathLink(DeathLinkDifficulty difficulty)
        {
            enableDeathLink = true;
            deathlinkDifficulty = difficulty;
        }

        public void SetAccentColor(Color accentColor)
        {
            AccentColor = accentColor;

            Log.LogDebug($"Accent Color set to: {accentColor.r} {accentColor.g} {accentColor.b} {accentColor.a}");
        }

        private void Socket_SocketClosed(WebSocketSharp.CloseEventArgs e)
        {
            Log.LogDebug($"Socket was disconnected. ({e.Code}) {e.Reason} (Clean? {e.WasClean})");

            if (OnClientDisconnect != null)
            {
                OnClientDisconnect(e.Code, e.Reason, e.WasClean);
            }

            RunDisconnectProcedure();
        }

        private void RunDisconnectProcedure()
        {
            Items.Unhook();
            Locations.Unhook();
            UI.Unhook();
        }

        private void Socket_PacketReceived(ArchipelagoPacketBase packet)
        {
            Log.LogDebug($"Received a packet of type: {packet.PacketType}");
            switch (packet)
            {
                case ConnectedPacket connectedPacket:
                {
                    var itemPickupStep = Convert.ToInt32(connectedPacket.SlotData["itemPickupStep"]) + 1;
                    var totalChecks = connectedPacket.LocationsChecked.Count + connectedPacket.MissingChecks.Count;
                    var currentChecks = connectedPacket.LocationsChecked.Count;

                    Locations.SetCheckCounts(totalChecks, itemPickupStep, currentChecks);
                    break;
                }
                case PrintPacket printPacket:
                {
                    ChatMessage.Send(printPacket.Text);
                    break;
                }
                case PrintJsonPacket printJsonPacket:
                {
                    string text = "";
                    foreach (var part in printJsonPacket.Data)
                    {
                        switch (part.Type)
                        {
                            case JsonMessagePartType.PlayerId:
                            {
                                int player_id = int.Parse(part.Text);
                                //todo: yeah...
                                text += "Steven";
                                break;
                            }
                            case JsonMessagePartType.ItemId:
                            {
                                int item_id = int.Parse(part.Text);
                                text += Items.GetItemNameFromId(item_id);
                                break;
                            }
                            case JsonMessagePartType.LocationId:
                            {
                                int location_id = int.Parse(part.Text);
                                text += Locations.GetLocationNameFromId(location_id);
                                break;
                            }
                            default:
                            {
                                text += part.Text;
                                break;
                            }
                        }
                    }
                    ChatMessage.Send(text);
                    break;
                }
            }
        }
    }
}