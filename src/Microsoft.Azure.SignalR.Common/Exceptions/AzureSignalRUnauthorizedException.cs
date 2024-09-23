// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace Microsoft.Azure.SignalR.Common;

#nullable enable

[Serializable]
public class AzureSignalRUnauthorizedException : AzureSignalRException
{
    private const string ErrorMessage = "Authorization failed. If you were using AccessKey, please check connection string and see if the AccessKey is correct. If you were using Azure Active Directory, please note that the role assignments will take up to 30 minutes to take effect if it was added recently.";

    private AzureSignalRUnauthorizedException(string message, Exception inner) : base(message, inner)
    {
    }

    internal AzureSignalRUnauthorizedException(Exception innerException) : base(ErrorMessage, innerException)
    {
    }

    internal async Task<AzureSignalRUnauthorizedException> BuildAsync(HttpRequestMessage request, Exception inner)
    {
        await Task.Delay(1);
        return new AzureSignalRUnauthorizedException(ErrorMessage, inner);
    }

#if NET8_0_OR_GREATER
    [Obsolete]
#endif
    protected AzureSignalRUnauthorizedException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}