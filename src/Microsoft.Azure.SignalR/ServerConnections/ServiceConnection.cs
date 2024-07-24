﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Azure.SignalR.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Azure.SignalR;

internal partial class ServiceConnection : ServiceConnectionBase
{
    private const string ClientConnectionCountInHub = "#clientInHub";

    private const string ClientConnectionCountInServiceConnection = "#client";

    // Fix issue: https://github.com/Azure/azure-signalr/issues/198
    // .NET Framework has restriction about reserved string as the header name like "User-Agent"
    private static readonly Dictionary<string, string> CustomHeader = new Dictionary<string, string> { { Constants.AsrsUserAgent, ProductInfo.GetProductInfo() } };

    private readonly IConnectionFactory _connectionFactory;

    private readonly IClientConnectionFactory _clientConnectionFactory;

    private readonly IClientConnectionManager _clientConnectionManager;

    private readonly ConcurrentDictionary<string, string> _connectionIds =
        new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

    private readonly string[] _pingMessages =
        new string[4] { ClientConnectionCountInHub, null, ClientConnectionCountInServiceConnection, null };

    private readonly ConnectionDelegate _connectionDelegate;

    private readonly IClientInvocationManager _clientInvocationManager;

    private readonly IHubProtocolResolver _hubProtocolResolver;

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
                             bool allowStatefulReconnects = false
        ) : base(serviceProtocol, serverId, connectionId, endpoint, serviceMessageHandler, serviceEventHandler, connectionType, loggerFactory?.CreateLogger<ServiceConnection>(), mode, allowStatefulReconnects)
    {
        _clientConnectionManager = clientConnectionManager;
        _connectionFactory = connectionFactory;
        _connectionDelegate = connectionDelegate;
        _clientConnectionFactory = clientConnectionFactory;
        _clientInvocationManager = clientInvocationManager;
        _hubProtocolResolver = hubProtocolResolver;
    }

    internal bool TryRemoveClientConnection(string connectionId, out IClientConnection connection)
    {
        _connectionIds.TryRemove(connectionId, out _);
        var r = _clientConnectionManager.TryRemoveClientConnection(connectionId, out connection);
#if NET7_0_OR_GREATER
        _clientInvocationManager.CleanupInvocationsByConnection(connectionId);
#endif
        return r;
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

        foreach (var entity in _connectionIds)
        {
            if (!string.IsNullOrEmpty(fromInstanceId) && entity.Value != fromInstanceId)
            {
                continue;
            }

            if (_clientConnectionManager.TryRemoveClientConnection(entity.Key, out var c) && c is ClientConnectionContext connection)
            {
                // We should not wait until all the clients' lifetime ends to restart another service connection
                _ = PerformDisconnectAsyncCore(connection);
            }
        }

        return Task.CompletedTask;
    }

    protected override ReadOnlyMemory<byte> GetPingMessage()
    {
        _pingMessages[1] = _clientConnectionManager.Count.ToString();
        _pingMessages[3] = _connectionIds.Count.ToString();

        return ServiceProtocol.GetMessageBytes(
            new PingMessage
            {
                Messages = _pingMessages
            });
    }

    protected override Task OnClientConnectedAsync(OpenConnectionMessage message)
    {
        var connection = _clientConnectionFactory.CreateConnection(message, ConfigureContext) as ClientConnectionContext;
        connection.ServiceConnection = this;

        if (message.Headers.TryGetValue(Constants.AsrsMigrateFrom, out var from))
        {
            connection.Features.Set<IConnectionMigrationFeature>(new ConnectionMigrationFeature(from, ServerId));
        }

        AddClientConnection(connection);

        var isDiagnosticClient = false;
        message.Headers.TryGetValue(Constants.AsrsIsDiagnosticClient, out var isDiagnosticClientValue);
        if (!StringValues.IsNullOrEmpty(isDiagnosticClientValue))
        {
            isDiagnosticClient = Convert.ToBoolean(isDiagnosticClientValue.FirstOrDefault());
        }

        var hubProtocol = _hubProtocolResolver.GetProtocol(message.Protocol, null);
        using (new ClientConnectionScope(endpoint: HubEndpoint, outboundConnection: this, isDiagnosticClient: isDiagnosticClient))
        {
            _ = connection.ProcessClientConnectionAsync(_connectionDelegate, hubProtocol);
        }

        if (connection.IsMigrated)
        {
            Log.MigrationStarting(Logger, connection.ConnectionId);
        }
        else
        {
            Log.ConnectedStarting(Logger, connection.ConnectionId);
        }

        return Task.CompletedTask;
    }

    protected override Task OnClientDisconnectedAsync(CloseConnectionMessage message)
    {
        if (_clientConnectionManager.TryRemoveClientConnection(message.ConnectionId, out var c) && c is ClientConnectionContext connection)
        {
            if (message.Headers.TryGetValue(Constants.AsrsMigrateTo, out var to))
            {
                connection.AbortOnClose = false;
                connection.Features.Set<IConnectionMigrationFeature>(new ConnectionMigrationFeature(ServerId, to));

                // We have to prevent SignalR `{type: 7}` (close message) from reaching our client while doing migration.
                // Since all data messages will be sent to `ServiceConnection` directly.
                // We can simply ignore all messages came from the application.
                connection.CancelOutgoing();

                // The close connection message must be the last message, so we could complete the pipe.
                connection.CompleteIncoming();
            }

            return PerformDisconnectAsyncCore(connection);
        }
        return Task.CompletedTask;
    }

    protected override async Task OnClientMessageAsync(ConnectionDataMessage connectionDataMessage)
    {
        if (connectionDataMessage.TracingId != null)
        {
            MessageLog.ReceiveMessageFromService(Logger, connectionDataMessage);
        }

        if (_clientConnectionManager.TryGetClientConnection(connectionDataMessage.ConnectionId, out var connection))
        {
            await (connection as ClientConnectionContext).ProcessConnectionDataMessageAsync(connectionDataMessage);
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

    private void AddClientConnection(ClientConnectionContext connection)
    {
        _clientConnectionManager.TryAddClientConnection(connection);
        _connectionIds.TryAdd(connection.ConnectionId, connection.InstanceId);
    }

    private async Task PerformDisconnectAsyncCore(ClientConnectionContext connection)
    {
        connection.ClearBufferedMessages();

        // In normal close, service already knows the client is closed, no need to be informed.
        connection.AbortOnClose = false;

        // We're done writing to the application output
        // Let the connection complete incoming
        connection.CompleteIncoming();

        // wait for the connection's lifetime task to end
        var lifetime = connection.LifetimeTask;

        // Wait on the application task to complete
        // We wait gracefully here to be consistent with self-host SignalR
        await Task.WhenAny(lifetime, connection.DelayTask);

        if (!lifetime.IsCompleted)
        {
            Log.DetectedLongRunningApplicationTask(Logger, connection.ConnectionId);
        }

        await lifetime;
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

    private Task OnConnectionReconnectAsync(ConnectionReconnectMessage message)
    {
        if (_clientConnectionManager.TryGetClientConnection(message.ConnectionId, out var connection))
        {
            (connection as ClientConnectionContext)?.ClearBufferedMessages();
        }
        return Task.CompletedTask;
    }
}
