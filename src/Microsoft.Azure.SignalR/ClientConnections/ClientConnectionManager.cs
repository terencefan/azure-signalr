// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Azure.SignalR;

internal class ClientConnectionManager : IClientConnectionManager
{
    private readonly ConcurrentDictionary<string, IClientConnection> _clientConnections = new();

    public IReadOnlyDictionary<string, IClientConnection> ClientConnections => _clientConnections;

    public bool TryAddClientConnection(IClientConnection connection) => _clientConnections.TryAdd(connection.ConnectionId, connection);

    public bool TryRemoveClientConnection(string connectionId, out IClientConnection connection) => _clientConnections.TryRemove(connectionId, out connection);

    public bool TryGetClientConnection(string connectionId, out IClientConnection connection) => _clientConnections.TryGetValue(connectionId, out connection);

    public Task WhenAllCompleted() => Task.WhenAll(_clientConnections.Select(c => (c.Value as ClientConnectionContext).LifetimeTask));
}
