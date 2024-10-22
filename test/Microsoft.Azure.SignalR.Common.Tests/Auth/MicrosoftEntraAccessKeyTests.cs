using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Moq;
using Xunit;

namespace Microsoft.Azure.SignalR.Common.Tests.Auth;

[Collection("Auth")]
public class MicrosoftEntraAccessKeyTests
{
    private const string DefaultSigningKey = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    private const string DefaultToken = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    private static readonly Uri DefaultEndpoint = new Uri("http://localhost");

    [Theory]
    [InlineData("https://a.bc", "https://a.bc/api/v1/auth/accessKey")]
    [InlineData("https://a.bc:80", "https://a.bc:80/api/v1/auth/accessKey")]
    [InlineData("https://a.bc:443", "https://a.bc/api/v1/auth/accessKey")]
    public void TestExpectedGetAccessKeyUrl(string endpoint, string expectedGetAccessKeyUrl)
    {
        var key = new MicrosoftEntraAccessKey(new Uri(endpoint), new DefaultAzureCredential());
        Assert.Equal(expectedGetAccessKeyUrl, key.GetAccessKeyUrl);
    }

    [Fact]
    public async Task TestUpdateAccessKey()
    {
        var mockCredential = new Mock<TokenCredential>();
        mockCredential.Setup(credential => credential.GetTokenAsync(
            It.IsAny<TokenRequestContext>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Mock GetTokenAsync throws an exception"));
        var key = new MicrosoftEntraAccessKey(DefaultEndpoint, mockCredential.Object);

        var audience = "http://localhost/chat";
        var claims = Array.Empty<Claim>();
        var lifetime = TimeSpan.FromHours(1);
        var algorithm = AccessTokenAlgorithm.HS256;

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<TaskCanceledException>(
            async () => await key.GenerateAccessTokenAsync(audience, claims, lifetime, algorithm, cts.Token)
        );

        var (kid, accessKey) = ("foo", DefaultSigningKey);
        key.UpdateAccessKey(kid, accessKey);

        var token = await key.GenerateAccessTokenAsync(audience, claims, lifetime, algorithm);
        Assert.NotNull(token);
    }

    [Theory]
    [InlineData(false, 1, true)]
    [InlineData(false, 4, true)]
    [InlineData(false, 6, false)]
    [InlineData(true, 6, true)]
    [InlineData(true, 54, true)]
    [InlineData(true, 56, false)]
    public async Task TestUpdateAccessKeyAsyncShouldSkip(bool isAuthorized, int timeElapsed, bool shouldSkip)
    {
        var mockCredential = new Mock<TokenCredential>();
        mockCredential.Setup(credential => credential.GetTokenAsync(
            It.IsAny<TokenRequestContext>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Mock GetTokenAsync throws an exception"));
        var key = new MicrosoftEntraAccessKey(DefaultEndpoint, mockCredential.Object);
        var isAuthorizedField = typeof(MicrosoftEntraAccessKey).GetField("_isAuthorized", BindingFlags.NonPublic | BindingFlags.Instance);
        isAuthorizedField.SetValue(key, isAuthorized);
        Assert.Equal(isAuthorized, (bool)isAuthorizedField.GetValue(key));

        var lastUpdatedTime = DateTime.UtcNow - TimeSpan.FromMinutes(timeElapsed);
        var lastUpdatedTimeField = typeof(MicrosoftEntraAccessKey).GetField("_lastUpdatedTime", BindingFlags.NonPublic | BindingFlags.Instance);
        lastUpdatedTimeField.SetValue(key, lastUpdatedTime);

        var initializedTcsField = typeof(MicrosoftEntraAccessKey).GetField("_initializedTcs", BindingFlags.NonPublic | BindingFlags.Instance);
        var initializedTcs = (TaskCompletionSource<object>)initializedTcsField.GetValue(key);

        await key.UpdateAccessKeyAsync().OrTimeout(TimeSpan.FromSeconds(30));
        var actualLastUpdatedTime = Assert.IsType<DateTime>(lastUpdatedTimeField.GetValue(key));

        if (shouldSkip)
        {
            Assert.Equal(isAuthorized, Assert.IsType<bool>(isAuthorizedField.GetValue(key)));
            Assert.Equal(lastUpdatedTime, actualLastUpdatedTime);
            Assert.Null(key.LastException);
            Assert.False(initializedTcs.Task.IsCompleted);
        }
        else
        {
            Assert.False(Assert.IsType<bool>(isAuthorizedField.GetValue(key)));
            Assert.True(lastUpdatedTime < actualLastUpdatedTime);
            Assert.NotNull(Assert.IsType<InvalidOperationException>(key.LastException));
            Assert.True(initializedTcs.Task.IsCompleted);
        }
    }

    [Fact]
    public async Task TestInitializeFailed()
    {
        var mockCredential = new Mock<TokenCredential>();
        mockCredential.Setup(credential => credential.GetTokenAsync(
            It.IsAny<TokenRequestContext>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Mock GetTokenAsync throws an exception"));
        var key = new MicrosoftEntraAccessKey(DefaultEndpoint, mockCredential.Object);

        var audience = "http://localhost/chat";
        var claims = Array.Empty<Claim>();
        var lifetime = TimeSpan.FromHours(1);
        var algorithm = AccessTokenAlgorithm.HS256;

        await key.UpdateAccessKeyAsync();

        var exception = await Assert.ThrowsAsync<AzureSignalRAccessTokenNotAuthorizedException>(
            async () => await key.GenerateAccessTokenAsync(audience, claims, lifetime, algorithm)
        );
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public async Task TestUpdateAccessKeyAfterInitializeFailed()
    {
        var mockCredential = new Mock<TokenCredential>();
        mockCredential.Setup(credential => credential.GetTokenAsync(
            It.IsAny<TokenRequestContext>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Mock GetTokenAsync throws an exception"));
        var key = new MicrosoftEntraAccessKey(DefaultEndpoint, mockCredential.Object);

        var audience = "http://localhost/chat";
        var claims = Array.Empty<Claim>();
        var lifetime = TimeSpan.FromHours(1);
        var algorithm = AccessTokenAlgorithm.HS256;

        await key.UpdateAccessKeyAsync();

        var exception = await Assert.ThrowsAsync<AzureSignalRAccessTokenNotAuthorizedException>(
            async () => await key.GenerateAccessTokenAsync(audience, claims, lifetime, algorithm)
        );
        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Same(exception.InnerException, key.LastException);

        var (kid, accessKey) = ("foo", DefaultSigningKey);
        key.UpdateAccessKey(kid, accessKey);
        Assert.Null(key.LastException);
    }

    [Theory]
    [InlineData(DefaultSigningKey)]
    [InlineData("fooooooooooooooooooooooooooooooooobar")]
    public async Task TestUpdateAccessKeySendRequest(string signingKey)
    {
        var token = new AccessToken(DefaultToken, DateTimeOffset.UtcNow.Add(TimeSpan.FromHours(1)));

        var mockCredential = new Mock<TokenCredential>();
        mockCredential.Setup(credential => credential.GetTokenAsync(
            It.IsAny<TokenRequestContext>(),
            It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(token));

        var httpClientFactory = new TestHttpClientFactory(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new AccessKeyResponse()
            {
                KeyId = "foo",
                AccessKey = signingKey,
            })
        });
        var key = new MicrosoftEntraAccessKey(DefaultEndpoint, mockCredential.Object, httpClientFactory: httpClientFactory);

        await key.UpdateAccessKeyAsync();

        Assert.True(key.IsAuthorized);
        Assert.Equal("foo", key.Id);
        Assert.Equal(signingKey, key.Value);
    }

    private sealed class TestHttpClientFactory(HttpResponseMessage message) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new TestHttpClient(message);
        }
    }

    private sealed class TestHttpClient(HttpResponseMessage message) : HttpClient
    {
        public override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(DefaultToken, request.Headers.Authorization.ToString().Substring("Bearer ".Length));
            return Task.FromResult(message);
        }
    }
}
