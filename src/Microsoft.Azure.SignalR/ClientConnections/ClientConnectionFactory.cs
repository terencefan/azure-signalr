// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.SignalR.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Azure.SignalR;

internal class ClientConnectionFactory : IClientConnectionFactory
{
    private readonly ILogger<ServiceConnection> _logger;

    public ClientConnectionFactory(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<ServiceConnection>() ?? NullLogger<ServiceConnection>.Instance;
    }

    public IClientConnection CreateConnection(OpenConnectionMessage message, Action<HttpContext> configureContext = null)
    {
        return new ClientConnectionContext(message, configureContext)
        {
            Logger = _logger
        };
    }
}
