// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace ChatSample
{
    public class Chat : Hub
    {
        private static string lastConnectionId = null;

        public void BroadcastMessage(string name, string message)
        {
            Clients.All.SendAsync("broadcastMessage", name, message);
            Console.WriteLine("Broadcasting...");

            Clients.Client(lastConnectionId).SendAsync("echo", name, "echo");
        }

        public void Echo(string name, string message)
        {
            Clients.Client(Context.ConnectionId).SendAsync("echo", name, $"{message} (echo from server, Client IP: {Context.GetHttpContext().Connection.RemoteIpAddress})");
            Console.WriteLine("Echo...");
        }

        public override async Task OnConnectedAsync()
        {
            var headers = Context.GetHttpContext().Request.Headers;

            lastConnectionId = Context.ConnectionId;

            Console.WriteLine($"{Context.ConnectionId} connected.");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception e)
        {
            var headers = Context.GetHttpContext().Request.Headers;

            Console.WriteLine($"{Context.ConnectionId} disconnected.");
            await base.OnDisconnectedAsync(e);
        }
    }
}
