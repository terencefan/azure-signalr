// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Azure.Core;

namespace Microsoft.Azure.SignalR.Common;

internal class AzureSignalRAccessTokenNotAuthorizedException : AzureSignalRException
{
    private const string Template = "The [{0}] is not available to generate access tokens for negotiation. {1}";

    public AzureSignalRAccessTokenNotAuthorizedException(TokenCredential credential, Exception innerException) : 
        base(BuildExceptionMessage(credential, innerException), innerException)
    {
    }

    private static string GetInnerReason(Exception exception)
    {
        return exception switch
        {
            AzureSignalRUnauthorizedException => "",
            AzureSignalRForbiddenException => "ssd",
            _ => "",
        };
    }

    private static string BuildExceptionMessage(TokenCredential credential, Exception innerException)
    {
        return string.Format(Template, credential.GetType().Name, GetInnerReason(innerException));
    }
}
