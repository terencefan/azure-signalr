// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Microsoft.Azure.SignalR.Protocol;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.SignalR;

internal partial class ServiceConnection
{
    private static class Log
    {
        // Category: ServiceConnection
        private static readonly Action<ILogger, Exception> _transportComplete =
            LoggerMessage.Define(LogLevel.Debug, new EventId(2, "TransportComplete"), "Transport completed.");

        private static readonly Action<ILogger, Exception> _closeTimedOut =
            LoggerMessage.Define(LogLevel.Debug, new EventId(3, "CloseTimedOut"), "Timed out waiting for close message sending to client, aborting the connection.");

        private static readonly Action<ILogger, Exception> _applicationComplete =
            LoggerMessage.Define(LogLevel.Debug, new EventId(4, "ApplicationComplete"), "Application task completes.");

        private static readonly Action<ILogger, Exception> _failedToCleanupConnections =
            LoggerMessage.Define(LogLevel.Error, new EventId(5, "FailedToCleanupConnection"), "Failed to clean up client connections.");

        private static readonly Action<ILogger, ulong?, string, Exception> _receivedMessageForNonExistentConnection =
            LoggerMessage.Define<ulong?, string>(LogLevel.Warning, new EventId(10, "ReceivedMessageForNonExistentConnection"), "Received message {tracingId} for connection {TransportConnectionId} which does not exist.");

        private static readonly Action<ILogger, string, Exception> _connectedStarting =
            LoggerMessage.Define<string>(LogLevel.Information, new EventId(11, "ConnectedStarting"), "Connection {TransportConnectionId} started.");

        private static readonly Action<ILogger, string, Exception> _serviceConnectionConnected =
            LoggerMessage.Define<string>(LogLevel.Debug, new EventId(20, "ServiceConnectionConnected"), "Service connection {ServiceConnectionId} connected.");

        private static readonly Action<ILogger, Exception> _applicationTaskCancelled =
            LoggerMessage.Define(LogLevel.Error, new EventId(21, "ApplicationTaskCancelled"), "Cancelled running application code, probably caused by time out.");

        private static readonly Action<ILogger, string, Exception> _migrationStarting =
            LoggerMessage.Define<string>(LogLevel.Debug, new EventId(22, "MigrationStarting"), "Connection {TransportConnectionId} migrated from another server.");

        private static readonly Action<ILogger, string, Exception> _errorSkippingHandshakeResponse =
            LoggerMessage.Define<string>(LogLevel.Error, new EventId(23, "ErrorSkippingHandshakeResponse"), "Error while skipping handshake response during migration, the connection will be dropped on the client-side. Error detail: {message}");

        private static readonly Action<ILogger, int, string, Exception> _closingClientConnections =
            LoggerMessage.Define<int, string>(LogLevel.Information, new EventId(25, "ClosingClientConnections"), "Closing {ClientCount} client connection(s) for server connection {ServerConnectionId}.");

        private static readonly Action<ILogger, string, Exception> _detectedLongRunningApplicationTask =
            LoggerMessage.Define<string>(LogLevel.Warning, new EventId(26, "DetectedLongRunningApplicationTask"), "The connection {TransportConnectionId} has a long running application logic that prevents the connection from complete.");

        public static void DetectedLongRunningApplicationTask(ILogger logger, string connectionId)
        {
            _detectedLongRunningApplicationTask(logger, connectionId, null);
        }

        public static void TransportComplete(ILogger logger)
        {
            _transportComplete(logger, null);
        }

        public static void CloseTimedOut(ILogger logger)
        {
            _closeTimedOut(logger, null);
        }

        public static void ApplicationComplete(ILogger logger)
        {
            _applicationComplete(logger, null);
        }

        public static void ClosingClientConnections(ILogger logger, int clientCount, string serverConnectionId)
        {
            _closingClientConnections(logger, clientCount, serverConnectionId, null);
        }

        public static void FailedToCleanupConnections(ILogger logger, Exception exception)
        {
            _failedToCleanupConnections(logger, exception);
        }

        public static void ReceivedMessageForNonExistentConnection(ILogger logger, ConnectionDataMessage message)
        {
            _receivedMessageForNonExistentConnection(logger, message.TracingId, message.ConnectionId, null);
        }

        public static void ConnectedStarting(ILogger logger, string connectionId)
        {
            _connectedStarting(logger, connectionId, null);
        }

        public static void MigrationStarting(ILogger logger, string connectionId)
        {
            _migrationStarting(logger, connectionId, null);
        }

        public static void ApplicationTaskCancelled(ILogger logger)
        {
            _applicationTaskCancelled(logger, null);
        }

        public static void ErrorSkippingHandshakeResponse(ILogger logger, Exception ex)
        {
            _errorSkippingHandshakeResponse(logger, ex.Message, ex);
        }
    }
}