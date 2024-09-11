// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace Microsoft.Azure.SignalR.Common;

[Serializable]
public class AzureSignalRForbiddenException : AzureSignalRException
{
    public const string IngressDenied = "Nginx denied the access, please check your Networking settings.";

    public const string RuntimeDenied = "Azure SignalR service denied the access, please check your Access control (IAM) settings.";

    private const string ErrorMessage = "API failed with status code 403.";

    public string Reason { get; private set; }

    public AzureSignalRForbiddenException(string requestUri, Exception innerException, string responseContent) : base(
        GetExceptionMessage(requestUri, responseContent),
        innerException)
    {
        Reason = responseContent.Contains("nginx") ? IngressDenied : RuntimeDenied;
    }

    private static string GetExceptionMessage(string requestUri, string responseContent)
    {
        var reason = responseContent.Contains("nginx") ? IngressDenied : RuntimeDenied;
        return string.IsNullOrEmpty(requestUri) ?
            string.Format("{0} {1}", ErrorMessage, reason) :
            string.Format("{0} {1} Request Uri: {2}", ErrorMessage, reason, requestUri);
    }
}
