// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Azure.SignalR.AspNet;

internal class ClientConnectionManager : IClientConnectionManager
{
    private readonly ConcurrentDictionary<string, IClientConnection> _clientConnections = new ConcurrentDictionary<string, IClientConnection>();

    public ClientConnectionTransportFactory TransportFactory { get; init; }

    public IEnumerable<IClientConnection> ClientConnections
    {
        get
        {
            foreach (var entity in _clientConnections)
            {
                yield return entity.Value;
            }
        }
    }

    public int Count => _clientConnections.Count;

    public ClientConnectionManager(HubConfiguration configuration, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<ClientConnectionManager>() ?? NullLogger<ClientConnectionManager>.Instance;
        TransportFactory = new ClientConnectionTransportFactory(configuration, logger);
    }

    public bool TryAddClientConnection(IClientConnection connection)
    {
        return _clientConnections.TryAdd(connection.ConnectionId, connection);
    }

    public bool TryRemoveClientConnection(string connectionId, out IClientConnection connection)
    {
        return _clientConnections.TryRemove(connectionId, out connection);
    }

    public bool TryGetClientConnection(string connectionId, out IClientConnection connection)
    {
        return _clientConnections.TryGetValue(connectionId, out connection);
    }

    public Task WhenAllCompleted() => Task.CompletedTask;
}
