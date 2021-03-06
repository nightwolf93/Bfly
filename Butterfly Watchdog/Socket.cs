﻿using System;
using System.Net;
using System.Net.Sockets;
using Butterfly_Watchdog.ServerManager;

namespace Butterfly_Emulator.ServerManager
{
    class DataSocket
    {
        private static Socket mListener;
        private static AsyncCallback mConnectionReqCallback;

        internal static void SetupListener(int Port)
        {
            SessionManagement.Init();
            mListener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint Endpoint = new IPEndPoint(IPAddress.Any, Port);
            mListener.Bind(Endpoint);

            mListener.Listen(1000);
            mConnectionReqCallback = new AsyncCallback(ConnectionRequest);
        }

        internal static void Start()
        {
            WaitForNextConnection();
        }

        private static void ConnectionRequest(IAsyncResult iAr)
        {
            Socket ClientSock = ((Socket)iAr.AsyncState).EndAccept(iAr);
            new Session(ClientSock);
            WaitForNextConnection();
        }

        private static void WaitForNextConnection()
        {
            try
            {
                mListener.BeginAccept(mConnectionReqCallback, mListener);
            }
            catch { }
        }
    }
}
