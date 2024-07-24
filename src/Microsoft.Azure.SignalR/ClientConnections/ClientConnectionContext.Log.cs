// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Microsoft.Azure.SignalR.Protocol;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.SignalR;

internal partial class ClientConnectionContext
{
    private static class Log
    {
        private static readonly Action<ILogger, Exception> _waitingForTransport =
            LoggerMessage.Define(LogLevel.Debug, new EventId(2, "WaitingForTransport"), "Waiting for the transport layer to end.");

        private static readonly Action<ILogger, Exception> _waitingForApplication =
            LoggerMessage.Define(LogLevel.Debug, new EventId(4, "WaitingForApplication"), "Waiting for the application to end.");

        private static readonly Action<ILogger, string, Exception> _errorSendingMessage =
            LoggerMessage.Define<string>(LogLevel.Error, new EventId(6, "ErrorSendingMessage"), "Error while sending message to the service, the connection carrying the traffic is dropped. Error detail: {message}");

        private static readonly Action<ILogger, string, Exception> _sendLoopStopped =
            LoggerMessage.Define<string>(LogLevel.Error, new EventId(7, "SendLoopStopped"), "Error while processing messages from {TransportConnectionId}.");

        private static readonly Action<ILogger, Exception> _applicationTaskFailed =
            LoggerMessage.Define(LogLevel.Error, new EventId(8, "ApplicationTaskFailed"), "Application task failed.");

        private static readonly Action<ILogger, ulong?, string, Exception> _failToWriteMessageToApplication =
            LoggerMessage.Define<ulong?, string>(LogLevel.Error, new EventId(9, "FailToWriteMessageToApplication"), "Failed to write message {tracingId} to {TransportConnectionId}.");

        private static readonly Action<ILogger, string, Exception> _connectedEnding =
            LoggerMessage.Define<string>(LogLevel.Information, new EventId(12, "ConnectedEnding"), "Connection {TransportConnectionId} ended.");

        private static readonly Action<ILogger, string, Exception> _closeConnection =
            LoggerMessage.Define<string>(LogLevel.Debug, new EventId(13, "CloseConnection"), "Sending close connection message to the service for {TransportConnectionId}.");

        private static readonly Action<ILogger, long, string, Exception> _writeMessageToApplication =
            LoggerMessage.Define<long, string>(LogLevel.Trace, new EventId(19, "WriteMessageToApplication"), "Writing {ReceivedBytes} to connection {TransportConnectionId}.");

        private static readonly Action<ILogger, string, Exception> _processConnectionFailed =
            LoggerMessage.Define<string>(LogLevel.Error, new EventId(24, "ProcessConnectionFailed"), "Error processing the connection {TransportConnectionId}.");

        public static void WriteMessageToApplication(ILogger<ServiceConnection> logger, long count, string connectionId)
        {
            _writeMessageToApplication(logger, count, connectionId, null);
        }

        public static void FailToWriteMessageToApplication(ILogger<ServiceConnection> logger, ConnectionDataMessage message, Exception exception)
        {
            _failToWriteMessageToApplication(logger, message.TracingId, message.ConnectionId, exception);
        }

        public static void WaitingForTransport(ILogger logger)
        {
            _waitingForTransport(logger, null);
        }

        public static void WaitingForApplication(ILogger logger)
        {
            _waitingForApplication(logger, null);
        }

        public static void ApplicationTaskFailed(ILogger logger, Exception exception)
        {
            _applicationTaskFailed(logger, exception);
        }

        public static void CloseConnection(ILogger logger, string connectionId)
        {
            _closeConnection(logger, connectionId, null);
        }

        public static void SendLoopStopped(ILogger logger, string connectionId, Exception exception)
        {
            _sendLoopStopped(logger, connectionId, exception);
        }

        public static void ErrorSendingMessage(ILogger logger, Exception exception)
        {
            _errorSendingMessage(logger, exception.Message, exception);
        }

        public static void ProcessConnectionFailed(ILogger logger, string connectionId, Exception exception)
        {
            _processConnectionFailed(logger, connectionId, exception);
        }

        public static void ConnectedEnding(ILogger logger, string connectionId)
        {
            _connectedEnding(logger, connectionId, null);
        }
    }
}
