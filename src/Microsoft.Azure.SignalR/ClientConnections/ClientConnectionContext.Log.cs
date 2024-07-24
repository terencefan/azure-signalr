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
        private static readonly Action<ILogger, ulong?, string, Exception> _failToWriteMessageToApplication =
            LoggerMessage.Define<ulong?, string>(LogLevel.Error, new EventId(9, "FailToWriteMessageToApplication"), "Failed to write message {tracingId} to {TransportConnectionId}.");

        private static readonly Action<ILogger, long, string, Exception> _writeMessageToApplication =
            LoggerMessage.Define<long, string>(LogLevel.Trace, new EventId(19, "WriteMessageToApplication"), "Writing {ReceivedBytes} to connection {TransportConnectionId}.");

        private static readonly Action<ILogger, string, Exception> _detectedLongRunningApplicationTask =
            LoggerMessage.Define<string>(LogLevel.Warning, new EventId(26, "DetectedLongRunningApplicationTask"), "The connection {TransportConnectionId} has a long running application logic that prevents the connection from complete.");

        public static void WriteMessageToApplication(ILogger<ServiceConnection> logger, long count, string connectionId)
        {
            _writeMessageToApplication(logger, count, connectionId, null);
        }

        public static void FailToWriteMessageToApplication(ILogger<ServiceConnection> logger, ConnectionDataMessage message, Exception exception)
        {
            _failToWriteMessageToApplication(logger, message.TracingId, message.ConnectionId, exception);
        }

        public static void DetectedLongRunningApplicationTask(ILogger logger, string connectionId)
        {
            _detectedLongRunningApplicationTask(logger, connectionId, null);
        }
    }
}
