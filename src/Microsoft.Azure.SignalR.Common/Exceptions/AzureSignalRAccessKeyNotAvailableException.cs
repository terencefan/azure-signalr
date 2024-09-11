// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Azure.Core;

namespace Microsoft.Azure.SignalR.Common;

internal class AzureSignalRAccessKeyNotAvailableException : AzureSignalRException
{
    private const string Template = "The [{0}] is not available to generate access tokens for negotiation.";

    public AzureSignalRAccessKeyNotAvailableException(TokenCredential credential, Exception innerException) : 
        base(BuildExceptionMessage(credential), innerException)
    {
    }

    private static string BuildExceptionMessage(TokenCredential credential)
    {
        return string.Format(Template, credential.GetType().Name);
    }
}
