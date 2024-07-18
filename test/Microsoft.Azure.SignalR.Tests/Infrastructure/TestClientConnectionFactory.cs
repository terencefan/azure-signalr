using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Azure.SignalR.Protocol;

namespace Microsoft.Azure.SignalR.Tests
{
    internal class TestClientConnectionFactory : IClientConnectionFactory
    {
        public IList<ClientConnectionContext> Connections = new List<ClientConnectionContext>();

        public IHubProtocol HubProtocol { get; } = new JsonHubProtocol();

        public ClientConnection CreateConnection(OpenConnectionMessage message, Action<HttpContext> configureContext)
        {
            var context = new ClientConnectionContext(message, configureContext);
            Connections.Add(context);
            return new ClientConnection(context);
        }
    }
}
