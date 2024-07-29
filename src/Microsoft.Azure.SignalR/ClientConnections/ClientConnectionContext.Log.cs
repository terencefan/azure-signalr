﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Microsoft.Azure.SignalR.Protocol;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.SignalR;

internal partial class ClientConnectionContext
{
    private static class Log
    {
        private static readonly Action<ILogger, long, string, Exception> _writeMessageToApplication =
            LoggerMessage.Define<long, string>(LogLevel.Trace, new EventId(1, "WriteMessageToApplication"), "Writing {ReceivedBytes} to connection {TransportConnectionId}.");

        private static readonly Action<ILogger, ulong?, string, Exception> _failToWriteMessageToApplication =
            LoggerMessage.Define<ulong?, string>(LogLevel.Error, new EventId(2, "FailToWriteMessageToApplication"), "Failed to write message {tracingId} to {TransportConnectionId}.");

        private static readonly Action<ILogger, string, Exception> _sendLoopStopped =
            LoggerMessage.Define<string>(LogLevel.Error, new EventId(3, "SendLoopStopped"), "Error while processing messages from {TransportConnectionId}.");

        private static readonly Action<ILogger, string, Exception> _errorSendingMessage =
            LoggerMessage.Define<string>(LogLevel.Error, new EventId(4, "ErrorSendingMessage"), "Error while sending message to the service, the connection carrying the traffic is dropped. Error detail: {message}");

        private static readonly Action<ILogger, string, Exception> _migrationStarting =
            LoggerMessage.Define<string>(LogLevel.Debug, new EventId(5, "MigrationStarting"), "Connection {TransportConnectionId} migrated from another server.");

        private static readonly Action<ILogger, string, Exception> _connectedStarting =
            LoggerMessage.Define<string>(LogLevel.Information, new EventId(11, "ConnectedStarting"), "Connection {TransportConnectionId} started.");

        public static void WriteMessageToApplication(ILogger<ServiceConnection> logger, long count, string connectionId)
        {
            _writeMessageToApplication(logger, count, connectionId, null);
        }

        public static void FailToWriteMessageToApplication(ILogger<ServiceConnection> logger, ConnectionDataMessage message, Exception exception)
        {
            _failToWriteMessageToApplication(logger, message.TracingId, message.ConnectionId, exception);
        }

        public static void SendLoopStopped(ILogger logger, string connectionId, Exception exception)
        {
            _sendLoopStopped(logger, connectionId, exception);
        }

        public static void ErrorSendingMessage(ILogger logger, Exception exception)
        {
            _errorSendingMessage(logger, exception.Message, exception);
        }

        public static void MigrationStarting(ILogger logger, string connectionId)
        {
            _migrationStarting(logger, connectionId, null);
        }

        public static void ConnectedStarting(ILogger logger, string connectionId)
        {
            _connectedStarting(logger, connectionId, null);
        }
    }
}
