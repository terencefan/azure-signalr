// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Azure.SignalR;

internal class ClientConnectionManager : IClientConnectionManager
{
    private readonly ConcurrentDictionary<string, ClientConnectionContext> _clientConnections =
        new ConcurrentDictionary<string, ClientConnectionContext>();

    public IEnumerable<ClientConnectionContext> ClientConnections
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

    public bool TryAddClientConnection(ClientConnectionContext connection)
    {
        return _clientConnections.TryAdd(connection.ConnectionId, connection);
    }

    public bool TryRemoveClientConnection(string connectionId, out ClientConnectionContext connection)
    {
        return _clientConnections.TryRemove(connectionId, out connection);
    }

    public bool TryGetClientConnection(string connectionId, out ClientConnectionContext connection)
    {
        return _clientConnections.TryGetValue(connectionId, out connection);
    }

    public Task WhenAllCompleted() => Task.WhenAll(_clientConnections.Select(c => c.Value.LifetimeTask));
}
