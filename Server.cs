// Server.cs
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MMC;

public static class Server
{
    private static TcpListener listener;
    private static bool running = true;

    public static void Start()
    {
        listener = new TcpListener(IPAddress.Any, 25565);
        listener.Start();
        Logger.Log("[Server] Listening on port 25565...");

        while (running)
        {
            if (listener.Pending())
            {
                TcpClient client = listener.AcceptTcpClient();
                Logger.Log("[Server] Client connected: " + client.Client.RemoteEndPoint);
                new Thread(() => ClientHandler.Handle(client)).Start();
            }
            Thread.Sleep(10);
        }
    }
}