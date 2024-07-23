// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Azure.SignalR.Tests.Common;

internal class TestClientConnectionManager : IClientConnectionManager
{
    private readonly ConcurrentDictionary<string, IClientConnection> _connections = new();

    public int Count => _connections.Count;

    public IEnumerable<IClientConnection> ClientConnections => _connections.Values;

    public bool TryAddClientConnection(IClientConnection connection)
    {
        return _connections.TryAdd(connection.ConnectionId, connection);
    }

    public bool TryGetClientConnection(string connectionId, out IClientConnection connection)
    {
        return _connections.TryGetValue(connectionId, out connection);
    }

    public bool TryRemoveClientConnection(string connectionId, out IClientConnection connection)
    {
        return _connections.TryRemove(connectionId, out connection);
    }

    public Task WhenAllCompleted()
    {
        return Task.CompletedTask;
    }
}
