// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Azure.SignalR.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Microsoft.Azure.SignalR.Common;

using SignalRProtocol = Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.AspNetCore.Connections;

using Microsoft.AspNetCore.SignalR.Protocol;

namespace Microsoft.Azure.SignalR;

#nullable enable

internal class ClientConnection : ClientConnectionBase
{
    private volatile ServiceConnection? _serviceConnection;

    public ServiceConnection ServiceConnection
    {
        get => _serviceConnection ?? throw new ArgumentNullException(nameof(ServiceConnection));
        private set
        {
            lock (value)
            {
                _serviceConnection?.RemoveClientConnection(Context.ConnectionId);
                _serviceConnection = value;
                _serviceConnection.AddClientConnection(Context);

                Logger = _serviceConnection?.Logger ?? NullLogger.Instance;
            }
        }
    }

    public ClientConnectionContext Context { get; }

    private ILogger Logger { get; set; } = NullLogger.Instance;

    public ClientConnection(ClientConnectionContext clientConnectionContext)
    {
        Context = clientConnectionContext ?? throw new ArgumentNullException(nameof(clientConnectionContext));
    }

    private enum ForwardMessageResult
    {
        Success,

        Error,

        Fatal,
    }

    public Task OnConnectedAsync(ServiceConnection serviceConnection,
                                 OpenConnectionMessage message,
                                 IHubProtocol protocol,
                                 ConnectionDelegate connectionDelegate,
                                 int closeTimeoutMilliseconds)
    {
        ServiceConnection = serviceConnection;

        if (message.Headers.TryGetValue(Constants.AsrsMigrateFrom, out var from))
        {
            Context.Features.Set<IConnectionMigrationFeature>(new ConnectionMigrationFeature(from, ServiceConnection.ServerId));
        }

        var isDiagnosticClient = false;
        message.Headers.TryGetValue(Constants.AsrsIsDiagnosticClient, out var isDiagnosticClientValue);
        if (!StringValues.IsNullOrEmpty(isDiagnosticClientValue))
        {
            isDiagnosticClient = Convert.ToBoolean(isDiagnosticClientValue.FirstOrDefault());
        }

        using (new ClientConnectionScope(endpoint: ServiceConnection.HubEndpoint, outboundConnection: serviceConnection, isDiagnosticClient: isDiagnosticClient))
        {
            _ = ProcessClientConnectionAsync(connectionDelegate, protocol, closeTimeoutMilliseconds);
        }
        return Task.CompletedTask;
    }

    private async Task ProcessClientConnectionAsync(ConnectionDelegate connectionDelegate,
                                                    IHubProtocol protocol,
                                                    int closeTimeoutMilliseconds)
    {
        try
        {
            // Writing from the application to the service
            var transport = ProcessOutgoingMessagesAsync(protocol, Context.OutgoingAborted);

            // Waiting for the application to shutdown so we can clean up the connection
            var app = ProcessApplicationTaskAsyncCore(connectionDelegate);

            var task = await Task.WhenAny(app, transport);

            // remove it from the connection list
            ServiceConnection.RemoveClientConnection(Context.ConnectionId);

            // This is the exception from application
            Exception? exception = null;
            if (task == app)
            {
                exception = app.Exception?.GetBaseException();

                // there is no need to write to the transport as application is no longer running
                Log.WaitingForTransport(Logger);

                // app task completes connection.Transport.Output, which will completes connection.Application.Input and ends the transport
                // Transports are written by us and are well behaved, wait for them to drain
                Context.CancelOutgoing(closeTimeoutMilliseconds);

                // transport never throws
                await transport;
            }
            else
            {
                // transport task ends first, no data will be dispatched out
                Log.WaitingForApplication(Logger);

                try
                {
                    // always wait for the application to complete
                    await app;
                }
                catch (Exception e)
                {
                    exception = e;
                }
            }

            if (exception != null)
            {
                Log.ApplicationTaskFailed(Logger, exception);
            }

            // If we aren't already aborted, we send the abort message to the service
            if (Context.AbortOnClose)
            {
                // Inform the Service that we will remove the client because SignalR told us it is disconnected.
                var serviceMessage =
                    new CloseConnectionMessage(Context.ConnectionId, errorMessage: exception?.Message);

                // when it fails, it means the underlying connection is dropped
                // service is responsible for closing the client connections in this case and there is no need to throw
                await ServiceConnection.WriteAsync(serviceMessage);
                Log.CloseConnection(Logger, Context.ConnectionId);
            }

            Log.ConnectedEnding(Logger, Context.ConnectionId);
        }
        catch (Exception e)
        {
            // When it throws, there must be something wrong
            Log.ProcessConnectionFailed(Logger, Context.ConnectionId, e);
        }
        finally
        {
            Context.OnCompleted();
        }
    }

    private async Task ProcessOutgoingMessagesAsync(SignalRProtocol.IHubProtocol protocol, CancellationToken token)
    {
        try
        {
            var isHandshakeResponseParsed = false;
            var shouldSkipHandshakeResponse = Context.IsMigrated;

            while (true)
            {
                var result = await Context.Application.Input.ReadAsync(token);

                if (result.IsCanceled)
                {
                    break;
                }

                var buffer = result.Buffer;

                if (!buffer.IsEmpty)
                {
                    if (!isHandshakeResponseParsed)
                    {
                        var next = buffer;
                        if (SignalRProtocol.HandshakeProtocol.TryParseResponseMessage(ref next, out var message))
                        {
                            isHandshakeResponseParsed = true;
                            if (!shouldSkipHandshakeResponse)
                            {
                                var forwardResult = await ForwardMessage(new ConnectionDataMessage(Context.ConnectionId, buffer.Slice(0, next.Start))
                                {
                                    Type = DataMessageType.Handshake
                                });
                                switch (forwardResult)
                                {
                                    case ForwardMessageResult.Success:
                                        break;
                                    default:
                                        return;
                                }
                            }
                            buffer = buffer.Slice(next.Start);
                        }
                        else
                        {
                            // waiting for handshake response.
                        }
                    }
                    if (isHandshakeResponseParsed)
                    {
                        var next = buffer;
                        while (!buffer.IsEmpty && protocol.TryParseMessage(ref next, FakeInvocationBinder.Instance, out var message))
                        {
                            var messageType = message is SignalRProtocol.HubInvocationMessage ? DataMessageType.Invocation :
                                message is SignalRProtocol.CloseMessage ? DataMessageType.Close : DataMessageType.Other;
                            var forwardResult = await ForwardMessage(new ConnectionDataMessage(Context.ConnectionId, buffer.Slice(0, next.Start)) { Type = messageType });
                            switch (forwardResult)
                            {
                                case ForwardMessageResult.Fatal:
                                    return;
                                default:
                                    buffer = next;
                                    break;
                            }
                        }
                    }
                }

                if (result.IsCompleted)
                {
                    // This connection ended (the application itself shut down) we should remove it from the list of connections
                    break;
                }

                Context.Application.Input.AdvanceTo(buffer.Start, buffer.End);
            }
        }
        catch (Exception ex)
        {
            // The exception means application fail to process input anymore
            // Cancel any pending flush so that we can quit and perform disconnect
            // Here is abort close and WaitOnApplicationTask will send close message to notify client to disconnect
            Log.SendLoopStopped(Logger, Context.ConnectionId, ex);
            Context.Application.Output.CancelPendingFlush();
        }
        finally
        {
            Context.Application.Input.Complete();
        }
    }

    private async Task ProcessApplicationTaskAsyncCore(ConnectionDelegate connectionDelegate)
    {
        Exception? exception = null;

        try
        {
            // Wait for the application task to complete
            // application task can end when exception, or Context.Abort() from hub
            await connectionDelegate(Context);
        }
        catch (ObjectDisposedException)
        {
            // When the application shuts down and disposes IServiceProvider, HubConnectionHandler.RunHubAsync is still running and runs into _dispatcher.OnDisconnectedAsync
            // no need to throw the error out
        }
        catch (Exception ex)
        {
            // Capture the exception to communicate it to the transport (this isn't strictly required)
            exception = ex;
            throw;
        }
        finally
        {
            // Close the transport side since the application is no longer running
            Context.Transport.Output.Complete(exception);
            Context.Transport.Input.Complete();
        }
    }

    /// <summary>
    /// Forward message to service
    /// </summary>
    private async Task<ForwardMessageResult> ForwardMessage(ConnectionDataMessage data)
    {
        try
        {
            // Forward the message to the service
            await ServiceConnection.WriteAsync(data);
            return ForwardMessageResult.Success;
        }
        catch (ServiceConnectionNotActiveException)
        {
            // Service connection not active means the transport layer for this connection is closed, no need to continue processing
            return ForwardMessageResult.Fatal;
        }
        catch (Exception ex)
        {
            Log.ErrorSendingMessage(Logger, ex);
            return ForwardMessageResult.Error;
        }
    }

    private sealed class FakeInvocationBinder : IInvocationBinder
    {
        public static readonly FakeInvocationBinder Instance = new FakeInvocationBinder();

        public IReadOnlyList<Type> GetParameterTypes(string methodName) => Type.EmptyTypes;

        public Type GetReturnType(string invocationId) => typeof(object);

        public Type GetStreamItemType(string streamId) => typeof(object);
    }
}
