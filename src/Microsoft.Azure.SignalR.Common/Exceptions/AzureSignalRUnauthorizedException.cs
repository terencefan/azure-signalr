// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace Microsoft.Azure.SignalR.Common;

[Serializable]
public class AzureSignalRUnauthorizedException : AzureSignalRException
{
    private const string ErrorMessage = "API failed with status code 401.";

    public AzureSignalRUnauthorizedException(string requestUri, Exception innerException) : base(
        string.IsNullOrEmpty(requestUri) ? ErrorMessage : $"{ErrorMessage} Request Uri: {requestUri}",
        innerException)
    {
    }

    internal AzureSignalRUnauthorizedException(Exception innerException) : base(ErrorMessage, innerException)
    {
    }
}