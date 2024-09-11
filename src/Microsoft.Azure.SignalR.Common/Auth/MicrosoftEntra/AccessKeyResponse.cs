﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json.Serialization;

namespace Microsoft.Azure.SignalR.Common;

/// <summary>
/// The response object containing the dynamic access key for signing client tokens.
/// </summary>
public class AccessKeyResponse
{
    /// <summary>
    /// The string value of the access key for SignalR app server to sign client tokens.
    /// </summary>
    [JsonPropertyName("accessKey")]
    public string AccessKey { get; set; }

    /// <summary>
    /// The ID of the access key.
    /// </summary>
    [JsonPropertyName("keyId")]
    public string KeyId { get; set; }
}
