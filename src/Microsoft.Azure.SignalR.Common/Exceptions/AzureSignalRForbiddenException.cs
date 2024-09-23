// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Microsoft.Azure.SignalR.Common;

#nullable enable

internal class AzureSignalRForbiddenException : AzureSignalRException
{
    private AzureSignalRForbiddenException(string message, Exception ex) : base(message, ex)
    {
    }

    public static async Task<AzureSignalRForbiddenException> BuildAsync(HttpResponseMessage response, Exception inner)
    {
        var content = await response.Content.ReadAsStringAsync();
        return content.Contains("nginx")
            ? new AzureSignalRForbiddenException("Ingress returns 403 response, please check your Networking settings.", inner)
            : new AzureSignalRForbiddenException("SignalR returns 403 response, please check your Access control (IAM) settings.", inner);
    }
}
