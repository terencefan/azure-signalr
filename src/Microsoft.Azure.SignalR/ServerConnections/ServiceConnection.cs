// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Azure.SignalR.Common;
using Microsoft.Azure.SignalR.Protocol;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.SignalR;

internal class ServiceConnection : ServiceConnectionBase
{
    private const int DefaultCloseTimeoutMilliseconds = 30000;

    private const string ClientConnectionCountInHub = "#clientInHub";

    private const string ClientConnectionCountInServiceConnection = "#client";

    // Fix issue: https://github.com/Azure/azure-signalr/issues/198
    // .NET Framework has restriction about reserved string as the header name like "User-Agent"
    private static readonly Dictionary<string, string> CustomHeader = new Dictionary<string, string> { { Constants.AsrsUserAgent, ProductInfo.GetProductInfo() } };

    private readonly IConnectionFactory _connectionFactory;

    private readonly IClientConnectionFactory _clientConnectionFactory;

    private readonly int _closeTimeOutMilliseconds;

    private readonly IClientConnectionManager _clientConnectionManager;

    private readonly ConcurrentDictionary<string, string> _connectionIds =
        new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

    private readonly string[] _pingMessages =
        new string[4] { ClientConnectionCountInHub, null, ClientConnectionCountInServiceConnection, null };

    private readonly ConnectionDelegate _connectionDelegate;

    private readonly IClientInvocationManager _clientInvocationManager;

    private readonly IHubProtocolResolver _hubProtocolResolver;

    // Performance: Do not use ConcurrentDictionary. There is no multi-threading scenario here, all operations are in the same logical thread.
    private readonly Dictionary<string, List<IMemoryOwner<byte>>> _bufferingMessages =
        new Dictionary<string, List<IMemoryOwner<byte>>>(StringComparer.Ordinal);

    public Action<HttpContext> ConfigureContext { get; set; }

    public ServiceConnection(IServiceProtocol serviceProtocol,
                             IClientConnectionManager clientConnectionManager,
                             IConnectionFactory connectionFactory,
                             ILoggerFactory loggerFactory,
                             ConnectionDelegate connectionDelegate,
                             IClientConnectionFactory clientConnectionFactory,
                             string serverId,
                             string connectionId,
                             HubServiceEndpoint endpoint,
                             IServiceMessageHandler serviceMessageHandler,
                             IServiceEventHandler serviceEventHandler,
                             IClientInvocationManager clientInvocationManager,
                             IHubProtocolResolver hubProtocolResolver,
                             ServiceConnectionType connectionType = ServiceConnectionType.Default,
                             GracefulShutdownMode mode = GracefulShutdownMode.Off,
                             int closeTimeOutMilliseconds = DefaultCloseTimeoutMilliseconds,
                             bool allowStatefulReconnects = false
        ) : base(serviceProtocol, serverId, connectionId, endpoint, serviceMessageHandler, serviceEventHandler, connectionType, loggerFactory?.CreateLogger<ServiceConnection>(), mode, allowStatefulReconnects)
    {
        _clientConnectionManager = clientConnectionManager;
        _connectionFactory = connectionFactory;
        _connectionDelegate = connectionDelegate;
        _clientConnectionFactory = clientConnectionFactory;
        _closeTimeOutMilliseconds = closeTimeOutMilliseconds;
        _clientInvocationManager = clientInvocationManager;
        _hubProtocolResolver = hubProtocolResolver;
    }

    public void AddClientConnection(ClientConnectionContext connection)
    {
        _clientConnectionManager.TryAddClientConnection(connection);
        _connectionIds.TryAdd(connection.ConnectionId, connection.InstanceId);
    }

    public ClientConnectionContext RemoveClientConnection(string connectionId)
    {
        _connectionIds.TryRemove(connectionId, out _);
        _clientConnectionManager.TryRemoveClientConnection(connectionId, out var connection);
#if NET7_0_OR_GREATER
        _clientInvocationManager.CleanupInvocationsByConnection(connectionId);
#endif
        return connection;
    }

    protected override Task<ConnectionContext> CreateConnection(string target = null)
    {
        return _connectionFactory.ConnectAsync(HubEndpoint, TransferFormat.Binary, ConnectionId, target, headers: CustomHeader);
    }

    protected override Task DisposeConnection(ConnectionContext connection)
    {
        return _connectionFactory.DisposeAsync(connection);
    }

    protected override Task CleanupClientConnections(string fromInstanceId = null)
    {
        // To gracefully complete client connections, let the client itself owns the connection lifetime

        foreach (var connection in _connectionIds)
        {
            if (!string.IsNullOrEmpty(fromInstanceId) && connection.Value != fromInstanceId)
            {
                continue;
            }

            // make sure there is no await operation before _bufferingMessages.
            _bufferingMessages.Remove(connection.Key);
            // We should not wait until all the clients' lifetime ends to restart another service connection
            _ = PerformDisconnectAsyncCore(connection.Key);
        }

        return Task.CompletedTask;
    }

    protected override ReadOnlyMemory<byte> GetPingMessage()
    {
        _pingMessages[1] = _clientConnectionManager.ClientConnections.Count.ToString();
        _pingMessages[3] = _connectionIds.Count.ToString();

        return ServiceProtocol.GetMessageBytes(
            new PingMessage
            {
                Messages = _pingMessages
            });
    }

    protected override Task OnClientConnectedAsync(OpenConnectionMessage message)
    {
        var connection = _clientConnectionFactory.CreateConnection(message, ConfigureContext);
        if (connection.Context.IsMigrated)
        {
            Log.MigrationStarting(Logger, connection.Context.ConnectionId);
        }
        else
        {
            Log.ConnectedStarting(Logger, connection.Context.ConnectionId);
        }
        return connection.OnConnectedAsync(this, message, _hubProtocolResolver.GetProtocol(message.Protocol, null), _connectionDelegate, _closeTimeOutMilliseconds);
    }

    protected override Task OnClientDisconnectedAsync(CloseConnectionMessage closeConnectionMessage)
    {
        var connectionId = closeConnectionMessage.ConnectionId;
        // make sure there is no await operation before _bufferingMessages.
        _bufferingMessages.Remove(connectionId);
        if (_clientConnectionManager.ClientConnections.TryGetValue(connectionId, out var context))
        {
            if (closeConnectionMessage.Headers.TryGetValue(Constants.AsrsMigrateTo, out var to))
            {
                context.AbortOnClose = false;
                context.Features.Set<IConnectionMigrationFeature>(new ConnectionMigrationFeature(ServerId, to));

                // We have to prevent SignalR `{type: 7}` (close message) from reaching our client while doing migration.
                // Since all data messages will be sent to `ServiceConnection` directly.
                // We can simply ignore all messages came from the application.
                context.CancelOutgoing();

                // The close connection message must be the last message, so we could complete the pipe.
                context.CompleteIncoming();
            }
        }
        return PerformDisconnectAsyncCore(connectionId);
    }

    protected override async Task OnClientMessageAsync(ConnectionDataMessage connectionDataMessage)
    {
        if (connectionDataMessage.TracingId != null)
        {
            MessageLog.ReceiveMessageFromService(Logger, connectionDataMessage);
        }
        if (_clientConnectionManager.ClientConnections.TryGetValue(connectionDataMessage.ConnectionId, out var connection))
        {
            try
            {
#if !NET8_0_OR_GREATER
                // do NOT write close message until net 8 or later.
                if (connectionDataMessage.Type == DataMessageType.Close)
                {
                    return;
                }
#endif
                if (connectionDataMessage.IsPartial)
                {
                    var owner = ExactSizeMemoryPool.Shared.Rent((int)connectionDataMessage.Payload.Length);
                    connectionDataMessage.Payload.CopyTo(owner.Memory.Span);
                    // make sure there is no await operation before _bufferingMessages.
                    if (!_bufferingMessages.TryGetValue(connectionDataMessage.ConnectionId, out var list))
                    {
                        list = new List<IMemoryOwner<byte>>();
                        _bufferingMessages[connectionDataMessage.ConnectionId] = list;
                    }
                    list.Add(owner);
                }
                else
                {
                    // make sure there is no await operation before _bufferingMessages.
                    if (_bufferingMessages.TryGetValue(connectionDataMessage.ConnectionId, out var list))
                    {
                        _bufferingMessages.Remove(connectionDataMessage.ConnectionId);
                        long length = 0;
                        foreach (var owner in list)
                        {
                            using (owner)
                            {
                                await connection.WriteMessageAsync(new ReadOnlySequence<byte>(owner.Memory));
                                length += owner.Memory.Length;
                            }
                        }
                        var payload = connectionDataMessage.Payload;
                        length += payload.Length;
                        Log.WriteMessageToApplication(Logger, length, connectionDataMessage.ConnectionId);
                        await connection.WriteMessageAsync(payload);
                    }
                    else
                    {
                        var payload = connectionDataMessage.Payload;
                        Log.WriteMessageToApplication(Logger, payload.Length, connectionDataMessage.ConnectionId);
                        await connection.WriteMessageAsync(payload);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.FailToWriteMessageToApplication(Logger, connectionDataMessage, ex);
            }
        }
        else
        {
            // Unexpected error
            Log.ReceivedMessageForNonExistentConnection(Logger, connectionDataMessage);
        }
    }

    protected override Task DispatchMessageAsync(ServiceMessage message)
    {
        return message switch
        {
            PingMessage pingMessage => OnPingMessageAsync(pingMessage),
            ClientInvocationMessage clientInvocationMessage => OnClientInvocationAsync(clientInvocationMessage),
            ServiceMappingMessage serviceMappingMessage => OnServiceMappingAsync(serviceMappingMessage),
            ClientCompletionMessage clientCompletionMessage => OnClientCompletionAsync(clientCompletionMessage),
            ErrorCompletionMessage errorCompletionMessage => OnErrorCompletionAsync(errorCompletionMessage),
            ConnectionReconnectMessage connectionReconnectMessage => OnConnectionReconnectAsync(connectionReconnectMessage),
            _ => base.DispatchMessageAsync(message)
        };
    }

    protected override Task OnPingMessageAsync(PingMessage pingMessage)
    {
#if NET7_0_OR_GREATER
        if (RuntimeServicePingMessage.TryGetOffline(pingMessage, out var instanceId))
        {
            _clientInvocationManager.Caller.CleanupInvocationsByInstance(instanceId);

            // Router invocations will be cleanup by its `CleanupInvocationsByConnection`, which is called by `RemoveClientConnection`.
            // In `base.OnPingMessageAsync`, `CleanupClientConnections(instanceId)` will finally execute `RemoveClientConnection` for each ConnectionId.
        }
#endif
        return base.OnPingMessageAsync(pingMessage);
    }

    private async Task PerformDisconnectAsyncCore(string connectionId)
    {
        var connection = RemoveClientConnection(connectionId);
        if (connection != null)
        {
            // In normal close, service already knows the client is closed, no need to be informed.
            connection.AbortOnClose = false;

            // We're done writing to the application output
            // Let the connection complete incoming
            connection.CompleteIncoming();

            // wait for the connection's lifetime task to end
            var lifetime = connection.LifetimeTask;

            // Wait on the application task to complete
            // We wait gracefully here to be consistent with self-host SignalR
            await Task.WhenAny(lifetime, Task.Delay(_closeTimeOutMilliseconds));

            if (!lifetime.IsCompleted)
            {
                Log.DetectedLongRunningApplicationTask(Logger, connectionId);
            }

            await lifetime;
        }
    }

    private Task OnClientInvocationAsync(ClientInvocationMessage message)
    {
        _clientInvocationManager.Router.AddInvocation(message.ConnectionId, message.InvocationId, message.CallerServerId, default);
        return Task.CompletedTask;
    }

    private Task OnServiceMappingAsync(ServiceMappingMessage message)
    {
        _clientInvocationManager.Caller.AddServiceMapping(message);
        return Task.CompletedTask;
    }

    private Task OnClientCompletionAsync(ClientCompletionMessage clientCompletionMessage)
    {
        _clientInvocationManager.Caller.TryCompleteResult(clientCompletionMessage.ConnectionId, clientCompletionMessage);
        return Task.CompletedTask;
    }

    private Task OnErrorCompletionAsync(ErrorCompletionMessage errorCompletionMessage)
    {
        _clientInvocationManager.Caller.TryCompleteResult(errorCompletionMessage.ConnectionId, errorCompletionMessage);
        return Task.CompletedTask;
    }

    private Task OnConnectionReconnectAsync(ConnectionReconnectMessage connectionReconnectMessage)
    {
        // make sure there is no await operation before _bufferingMessages.
        _bufferingMessages.Remove(connectionReconnectMessage.ConnectionId);
        return Task.CompletedTask;
    }
}
