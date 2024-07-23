using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Azure.SignalR;

internal interface IClientConnectionManager
{
    IEnumerable<IClientConnection> ClientConnections { get; }

    int Count { get; }

    Task WhenAllCompleted();

    bool TryAddClientConnection(IClientConnection connection);

    bool TryRemoveClientConnection(string connectionId, out IClientConnection connection);

    bool TryGetClientConnection(string connectionId, out IClientConnection connection);
}
