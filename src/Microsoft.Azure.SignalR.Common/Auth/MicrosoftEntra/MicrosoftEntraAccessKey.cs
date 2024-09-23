// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Azure.Core;

using Microsoft.Azure.SignalR.Common;

using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.SignalR;

#nullable enable

internal partial class MicrosoftEntraAccessKey : AccessKey
{
    internal static readonly TimeSpan GetAccessKeyTimeout = TimeSpan.FromSeconds(100);

    private const int GetAccessKeyIntervalInMinute = 55;

    private const int GetAccessKeyMaxRetryTimes = 3;

    private const int GetMicrosoftEntraTokenMaxRetryTimes = 3;

    private const string DefaultScope = "https://signalr.azure.com/.default";

    private static readonly TokenRequestContext DefaultRequestContext = new TokenRequestContext(new string[] { DefaultScope });

    private static readonly TimeSpan GetAccessKeyInterval = TimeSpan.FromMinutes(GetAccessKeyIntervalInMinute);

    private static readonly TimeSpan GetAccessKeyIntervalWhenUnauthorized = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan GetAccessKeyRetryInterval = TimeSpan.FromSeconds(3);

    private readonly TaskCompletionSource<object> _initializedTcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

    private volatile bool _isAuthorized = false;

    private Exception? _lastException;

    private DateTime _lastUpdatedTime = DateTime.MinValue;

    public bool IsAuthorized
    {
        get => _isAuthorized;
        private set
        {
            if (value)
            {
                _lastException = null;
            }
            _lastUpdatedTime = DateTime.UtcNow;
            _isAuthorized = value;
            _initializedTcs.TrySetResult(string.Empty);
        }
    }

    public TokenCredential TokenCredential { get; }

    internal string GetAccessKeyUrl { get; }

    internal bool HasExpired => DateTime.UtcNow - _lastUpdatedTime > TimeSpan.FromMinutes(GetAccessKeyIntervalInMinute * 2);

    private Task<object> InitializedTask => _initializedTcs.Task;

    public MicrosoftEntraAccessKey(Uri endpoint, TokenCredential credential, Uri? serverEndpoint = null) : base(endpoint)
    {
        var authorizeUri = (serverEndpoint ?? endpoint).Append("/api/v1/auth/accessKey");
        GetAccessKeyUrl = authorizeUri.AbsoluteUri;
        TokenCredential = credential;
    }

    public virtual async Task<string> GetMicrosoftEntraTokenAsync(CancellationToken ctoken = default)
    {
        Exception? last = null;
        for (var i = 0; i < GetMicrosoftEntraTokenMaxRetryTimes; i++)
        {
            try
            {
                var token = await TokenCredential.GetTokenAsync(DefaultRequestContext, ctoken);
                return token.Token;
            }
            catch (Exception e)
            {
                last = e;
            }
        }
        throw last ?? new InvalidOperationException();
    }

    public override async Task<string> GenerateAccessTokenAsync(string audience,
                                                                IEnumerable<Claim> claims,
                                                                TimeSpan lifetime,
                                                                AccessTokenAlgorithm algorithm,
                                                                CancellationToken ctoken = default)
    {
        var task = await Task.WhenAny(InitializedTask, ctoken.AsTask());

        if (task == InitializedTask || InitializedTask.IsCompleted)
        {
            await task;
            return IsAuthorized
                ? await base.GenerateAccessTokenAsync(audience, claims, lifetime, algorithm, ctoken)
                : throw new AzureSignalRAccessTokenNotAuthorizedException(TokenCredential.GetType().Name, _lastException);
        }
        else
        {
            throw new TaskCanceledException("Timeout reached when authorizing AzureAD identity.");
        }
    }

    internal void UpdateAccessKey(string kid, string accessKey)
    {
        Key = new Tuple<string, string>(kid, accessKey);
        IsAuthorized = true;
    }

    internal async Task UpdateAccessKeyAsync(CancellationToken ctoken = default)
    {
        var delta = DateTime.UtcNow - _lastUpdatedTime;
        if (IsAuthorized && delta < GetAccessKeyInterval)
        {
            return;
        }
        else if (!IsAuthorized && delta < GetAccessKeyIntervalWhenUnauthorized)
        {
            return;
        }

        for (var i = 0; i < GetAccessKeyMaxRetryTimes; i++)
        {
            var source = new CancellationTokenSource(GetAccessKeyTimeout);
            var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(source.Token, ctoken);
            try
            {
                var token = await GetMicrosoftEntraTokenAsync(linkedSource.Token);
                await GetAccessKeyInternalAsync(token, linkedSource.Token);
                return;
            }
            catch (OperationCanceledException e)
            {
                _lastException = e;
                break;
            }
            catch (Exception e)
            {
                _lastException = e;
                try
                {
                    await Task.Delay(GetAccessKeyRetryInterval, ctoken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        IsAuthorized = false;
    }

    internal virtual async Task<bool> HandleHttpResponseAsync(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return false;
        }

        var content = await response.Content.ReadAsStringAsync();
        var json = JObject.Parse(content);

        if (!json.TryGetValue("KeyId", out var keyId) || keyId.Type != JTokenType.String)
        {
            throw new AzureSignalRException("Missing required <KeyId> field.");
        }
        if (!json.TryGetValue("AccessKey", out var key) || key.Type != JTokenType.String)
        {
            throw new AzureSignalRException("Missing required <AccessKey> field.");
        }

        UpdateAccessKey(keyId.ToString(), key.ToString());
        return true;
    }

    private async Task GetAccessKeyInternalAsync(string accessToken, CancellationToken ctoken = default)
    {
        var api = new RestApiEndpoint(GetAccessKeyUrl, accessToken);
        var client = new RestClient();
        await client.SendAsync(api,
                               HttpMethod.Get,
                               handleExpectedResponseAsync: HandleHttpResponseAsync,
                               cancellationToken: ctoken);
    }
}
