﻿using System;
using System.Collections.Generic;
using Butterfly.Core;
using Butterfly.HabboHotel.Misc;
using Butterfly.HabboHotel.Pathfinding;
using Butterfly.HabboHotel.Users;
using Butterfly.HabboHotel.Users.UserDataManagement;
using Butterfly.Messages;
using Butterfly.Net;
using Butterfly.Util;
using ConnectionManager;
using System.Drawing;

namespace Butterfly.HabboHotel.GameClients
{
    class GameClient
    {
        private uint Id;

        private ConnectionInformation Connection;
        private GameClientMessageHandler MessageHandler;

        private Habbo Habbo;

        internal DateTime TimePingedReceived;
        //internal DateTime TimePingSent;
        //internal DateTime TimePingLastControl;

        internal bool SetDoorPos;
        internal Point newDoorPos;
        private GamePacketParser packetParser;

        internal uint ConnectionID
        {
            get
            {
                return Id;
            }
        }

        internal int CurrentRoomUserID;

        internal GameClient(uint ClientId, ConnectionInformation pConnection)
        {
            Id = ClientId;
            Connection = pConnection;
            SetDoorPos = false;
            CurrentRoomUserID = -1;
            packetParser = new GamePacketParser();
        }

        void SwitchParserRequest()
        {
            if (MessageHandler == null)
            {
                InitHandler();
            }
            packetParser.SetConnection(Connection);
            packetParser.onNewPacket += new GamePacketParser.HandlePacket(parser_onNewPacket);
            byte[] data = (Connection.parser as InitialPacketParser).currentData;
            Connection.parser.Dispose();
            Connection.parser = packetParser;
            Connection.parser.handlePacketData(data);
        }

        void parser_onNewPacket(ClientMessage Message)
        {
            try
            {
                MessageHandler.HandleRequest(Message);
            }
            catch (Exception e) { Logging.LogPacketException(Message.ToString(), e.ToString()); }
        }

        void PolicyRequest()
        {
            Connection.SendData(ButterflyEnvironment.GetDefaultEncoding().GetBytes(CrossdomainPolicy.GetXmlPolicy()));
        }

        internal ConnectionInformation GetConnection()
        {
            return Connection;
        }

        internal GameClientMessageHandler GetMessageHandler()
        {
            return MessageHandler;
        }

        internal bool gotTheThing = false;

        internal Habbo GetHabbo()
        {
            return Habbo;
        }

        internal void StartConnection()
        {
            if (Connection == null)
            {
                return;
            }

            TimePingedReceived = DateTime.Now;
            
            (Connection.parser as InitialPacketParser).PolicyRequest += new InitialPacketParser.NoParamDelegate(PolicyRequest);
            (Connection.parser as InitialPacketParser).SwitchParserRequest += new InitialPacketParser.NoParamDelegate(SwitchParserRequest);

            Connection.startPacketProcessing();
        }

        internal void InitHandler()
        {
            MessageHandler = new GameClientMessageHandler(this);
        }

        internal bool tryLogin(string AuthTicket)
        {
            try
            {
                string ip = GetConnection().getIp();
                byte errorCode = 0;
                UserData userData = UserDataFactory.GetUserData(AuthTicket, ip, out errorCode);
                if (errorCode == 1)
                {
                    SendNotifWithScroll(LanguageLocale.GetValue("login.invalidsso"));
                    return false;
                }
                else if (errorCode == 2)
                {
                    SendNotifWithScroll(LanguageLocale.GetValue("login.loggedin"));
                    return false;
                }

                
                if (Program.LicHandeler != null && ButterflyEnvironment.GetGame().GetClientManager().ClientCount > Program.LicHandeler.AmountOfSlots)
                {
                    Program.LicHandeler.ReportFullServer();
                    SendBanMessage(LanguageLocale.GetValue("server.full"));
                    return false;
                }
                ButterflyEnvironment.GetGame().GetClientManager().RegisterClient(this, userData.userID, userData.user.Username);
                this.Habbo = userData.user;
                userData.user.LoadData(userData);

                if (userData.user.Username == null)
                {
                    SendBanMessage("You have no username.");
                    return false;
                }
                string banReason = ButterflyEnvironment.GetGame().GetBanManager().GetBanReason(userData.user.Username, ip);
                if (!string.IsNullOrEmpty(banReason))
                {
                    SendBanMessage(banReason);
                    return false;
                }

                userData.user.Init(this, userData);

                QueuedServerMessage response = new QueuedServerMessage(Connection);

                userData.user.SerializeQuests(ref response);

                List<string> Rights = ButterflyEnvironment.GetGame().GetRoleManager().GetRightsForHabbo(userData.user);

                ServerMessage appendingResponse = new ServerMessage(2);
                appendingResponse.Init(2);
                appendingResponse.AppendInt32(Rights.Count);

                foreach (string Right in Rights)
                {
                    appendingResponse.AppendStringWithBreak(Right);
                }

                response.appendResponse(appendingResponse);

                if (userData.user.HasFuse("fuse_mod"))
                {
                    response.appendResponse(ButterflyEnvironment.GetGame().GetModerationTool().SerializeTool());
                    ButterflyEnvironment.GetGame().GetModerationTool().SerializeOpenTickets(ref response, userData.userID);
                }

                response.appendResponse(userData.user.GetAvatarEffectsInventoryComponent().Serialize());

                appendingResponse.Init(290);
                appendingResponse.AppendBoolean(true);
                appendingResponse.AppendBoolean(false);
                response.appendResponse(appendingResponse);

                appendingResponse.Init(3);
                response.appendResponse(appendingResponse);

                appendingResponse.Init(517);
                appendingResponse.AppendBoolean(true);
                response.appendResponse(appendingResponse);

                //if (PixelManager.NeedsUpdate(this))
                //    PixelManager.GivePixels(this);

                if (ButterflyEnvironment.GetGame().GetClientManager().pixelsOnLogin > 0)
                {
                    PixelManager.GivePixels(this, ButterflyEnvironment.GetGame().GetClientManager().pixelsOnLogin);
                }

                if (ButterflyEnvironment.GetGame().GetClientManager().creditsOnLogin > 0)
                {
                    userData.user.Credits += ButterflyEnvironment.GetGame().GetClientManager().creditsOnLogin;
                    userData.user.UpdateCreditsBalance();
                }

                if (userData.user.HomeRoom > 0)
                {
                    appendingResponse.Init(455);
                    appendingResponse.AppendUInt(userData.user.HomeRoom);
                    response.appendResponse(appendingResponse);
                }

                appendingResponse.Init(458);
                appendingResponse.AppendInt32(30);
                appendingResponse.AppendInt32(userData.user.FavoriteRooms.Count);

                foreach (uint Id in userData.user.FavoriteRooms.ToArray())
                {
                    appendingResponse.AppendUInt(Id);
                }

                response.appendResponse(appendingResponse);

                if (userData.user.HasFuse("fuse_use_club_badge") && !userData.user.GetBadgeComponent().HasBadge("ACH_BasicClub1"))
                {
                    userData.user.GetBadgeComponent().GiveBadge("ACH_BasicClub1", true);
                }
                else if (!userData.user.HasFuse("fuse_use_club_badge") && userData.user.GetBadgeComponent().HasBadge("ACH_BasicClub1"))
                {
                    userData.user.GetBadgeComponent().RemoveBadge("ACH_BasicClub1");
                }


                if (!userData.user.GetBadgeComponent().HasBadge("Z63"))
                    userData.user.GetBadgeComponent().GiveBadge("Z63", true);

                appendingResponse.Init(2);
                appendingResponse.AppendInt32(0);

                if (userData.user.HasFuse("fuse_use_vip_outfits")) // VIP 
                    appendingResponse.AppendInt32(2);
                else if (userData.user.HasFuse("fuse_furni_chooser")) // HC
                    appendingResponse.AppendInt32(1);
                else
                    appendingResponse.AppendInt32(0);

                appendingResponse.AppendInt32(0);
                response.appendResponse(appendingResponse);

                appendingResponse.Init(2);
                appendingResponse.AppendInt32(Rights.Count);

                foreach (string Right in Rights)
                {
                    appendingResponse.AppendStringWithBreak(Right);
                }

                response.appendResponse(appendingResponse);

                if (LanguageLocale.welcomeAlertEnabled)
                {
                    ServerMessage alert = new ServerMessage(810);
                    alert.AppendUInt(1);
                    alert.AppendStringWithBreak(LanguageLocale.welcomeAlert);
                    response.appendResponse(alert);
                }

                response.sendResponse();
                Logging.WriteLine("[" + Habbo.Username + "] logged in");

                return true;
            }
            catch (UserDataNotFoundException e)
            {
                SendNotifWithScroll(LanguageLocale.GetValue("login.invalidsso") + "extra data: " + e.ToString());
            }
            catch (Exception e)
            {
                //Logging.LogCriticalException("Invalid Dario bug duing user login: " + e.ToString());
                //SendNotif("Login error: " + e.ToString());
                SendNotifWithScroll("Login error: " + e.ToString());
            }
            return false;
        }

        internal void SendNotifWithScroll(string message)
        {
            ServerMessage notification = new ServerMessage(810);
            notification.AppendUInt(1);
            notification.AppendStringWithBreak(message);

            SendMessage(notification);
        }

        internal void SendBanMessage(string Message)
        {
            ServerMessage BanMessage = new ServerMessage(35);
            BanMessage.AppendStringWithBreak(LanguageLocale.GetValue("moderation.banmessage"), 13);
            BanMessage.AppendStringWithBreak(Message);
            GetConnection().SendData(BanMessage.GetBytes());
        }

        internal void SendNotif(string Message)
        {
            SendNotif(Message, false);
        }

        internal void SendNotif(string Message, Boolean FromHotelManager)
        {
            ServerMessage nMessage = new ServerMessage();

            if (FromHotelManager)
            {
                nMessage.Init(139);
            }
            else
            {
                nMessage.Init(161);
            }

            nMessage.AppendStringWithBreak(Message);
            GetConnection().SendData(nMessage.GetBytes());
        }

        internal void Stop()
        {
            if (GetMessageHandler() != null)
                MessageHandler.Destroy();

            if (GetHabbo() != null)
                Habbo.OnDisconnect();
            CurrentRoomUserID = -1;

            this.MessageHandler = null;
            this.Habbo = null;
            this.Connection = null;
        }

        private bool Disconnected = false;

        internal void Disconnect()
        {
            if (GetHabbo() != null && GetHabbo().GetInventoryComponent() != null)
                GetHabbo().GetInventoryComponent().RunDBUpdate();
            if (!Disconnected)
            {
                if (Connection != null)
                    Connection.Dispose();
                Disconnected = true;
            }
        }

        internal void HandleConnectionData(ref byte[] data)
        {
            if (data[0] == 64)
            {
                int pos = 0;

                while (pos < data.Length)
                {
                    try
                    {
                        int MessageLength = Base64Encoding.DecodeInt32(new byte[] { data[pos++], data[pos++], data[pos++] });
                        int MessageId = Base64Encoding.DecodeInt32(new byte[] { data[pos++], data[pos++] });

                        byte[] Content = new byte[MessageLength - 2];

                        for (int i = 0; i < Content.Length; i++)
                        {
                            Content[i] = data[pos++];
                        }

                        ClientMessage Message = new ClientMessage(MessageId, Content);

                        if (MessageHandler == null)
                        {
                            InitHandler(); //Never ever register the packets BEFORE you receive any data.
                        }

                        //DateTime PacketMsgStart = DateTime.Now;
                    }
                    catch (Exception e)
                    {
                        Logging.HandleException(e, "packet handling");
                        Disconnect();
                    }
                }
            }
            else
            {
                Connection.SendData(ButterflyEnvironment.GetDefaultEncoding().GetBytes(CrossdomainPolicy.GetXmlPolicy()));
            }
        }

        internal void SendMessage(ServerMessage Message)
        {
            if (Message == null)
                return;
            if (GetConnection() == null)
                return;
            GetConnection().SendData(Message.GetBytes());
        }

        internal void UnsafeSendMessage(ServerMessage Message)
        {
            if (Message == null)
                return;
            if (GetConnection() == null)
                return;
            GetConnection().SendUnsafeData(Message.GetBytes());
        }
    }
}
