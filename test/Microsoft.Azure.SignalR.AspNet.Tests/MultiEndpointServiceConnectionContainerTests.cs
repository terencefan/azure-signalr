// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.SignalR.Common;
using Microsoft.Azure.SignalR.Protocol;
using Microsoft.Azure.SignalR.Tests.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Owin;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Azure.SignalR.AspNet.Tests;

public class MultiEndpointServiceConnectionContainerTests(ITestOutputHelper output) : VerifiableLoggedTest(output)
{
    private const string ConnectionStringFormatter = "Endpoint={0};AccessKey=ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789;";

    private const string Url1 = "http://url1";

    private const string Url2 = "https://url2";

    private static readonly JoinGroupWithAckMessage DefaultGroupMessage = new JoinGroupWithAckMessage("a", "a");

    private readonly string _connectionString1 = string.Format(ConnectionStringFormatter, Url1);

    private readonly string _connectionString2 = string.Format(ConnectionStringFormatter, Url2);

    [Fact]
    public async Task TestGetRoutedEndpointsReturnDistinctResultForMultiMessages()
    {
        var endpoints = new[]
        {
            new ServiceEndpoint(_connectionString1, EndpointType.Primary, "1"),
            new ServiceEndpoint(_connectionString1, EndpointType.Primary, "2"),
            new ServiceEndpoint(_connectionString2, EndpointType.Secondary, "11"),
            new ServiceEndpoint(_connectionString2, EndpointType.Secondary, "12")
        };

        var sem = new TestServiceEndpointManager(endpoints);

        var router = new TestEndpointRouter(false);
        var container = new TestMultiEndpointServiceConnectionContainer("hub",
            e => new TestBaseServiceConnectionContainer([
            new TestServiceConnection(),
            new TestServiceConnection(),
        ], e), sem, router, NullLoggerFactory.Instance);

        // Start the container for it to disconnect
        _ = container.StartAsync();
        await container.ConnectionInitializedTask.OrTimeout();

        var result = container.GetRoutedEndpoints(new MultiGroupBroadcastDataMessage(["group1", "group2"], null)).ToList();

        Assert.Equal(2, result.Count);

        result = container.GetRoutedEndpoints(new MultiUserDataMessage(["user1", "user2"], null)).ToList();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task TestEndpointsForDifferentContainersHaveDifferentStatus()
    {
        var endpoints = new[]
        {
            new ServiceEndpoint(_connectionString1, EndpointType.Primary, "1"),
            new ServiceEndpoint(_connectionString1, EndpointType.Primary, "2"),
            new ServiceEndpoint(_connectionString2, EndpointType.Secondary, "11"),
            new ServiceEndpoint(_connectionString2, EndpointType.Secondary, "12")
        };

        var sem = new TestServiceEndpointManager(endpoints);

        var router = new TestEndpointRouter(false);
        var container1 = new TestMultiEndpointServiceConnectionContainer("hub",
            e => new TestBaseServiceConnectionContainer([
            new TestServiceConnection(ServiceConnectionStatus.Disconnected),
            new TestServiceConnection(ServiceConnectionStatus.Disconnected),
        ], e), sem, router, NullLoggerFactory.Instance);

        var container2 = new TestMultiEndpointServiceConnectionContainer("hub",
            e => new TestBaseServiceConnectionContainer([
            new TestServiceConnection(),
            new TestServiceConnection(),
        ], e), sem, router, NullLoggerFactory.Instance);

        var container3 = new TestMultiEndpointServiceConnectionContainer("hub-another",
            e => new TestBaseServiceConnectionContainer([
            new TestServiceConnection(),
            new TestServiceConnection(),
        ], e), sem, router, NullLoggerFactory.Instance);

        // Start the container for it to disconnect
        _ = container1.StartAsync();
        await container1.ConnectionInitializedTask.OrTimeout();

        // Start the container for it to disconnect
        _ = container2.StartAsync();
        await container2.ConnectionInitializedTask.OrTimeout();

        // Start the container for it to disconnect
        _ = container3.StartAsync();
        await container3.ConnectionInitializedTask.OrTimeout();

        var result = container1.GetRoutedEndpoints(new MultiGroupBroadcastDataMessage(["group1", "group2"], null)).ToList();

        Assert.Equal(2, result.Count);

        result = container1.GetRoutedEndpoints(new MultiUserDataMessage(["user1", "user2"], null)).ToList();

        Assert.Equal(2, result.Count);

        // The same hub shares the same endpoints
        result = container2.GetRoutedEndpoints(new MultiGroupBroadcastDataMessage(["group1", "group2"], null)).ToList();

        Assert.Equal(2, result.Count);

        result = container2.GetRoutedEndpoints(new MultiUserDataMessage(["user1", "user2"], null)).ToList();

        Assert.Equal(2, result.Count);

        // different hubs have different endpoint status
        result = container3.GetRoutedEndpoints(new MultiGroupBroadcastDataMessage(["group1", "group2"], null)).ToList();

        Assert.Equal(2, result.Count);

        result = container3.GetRoutedEndpoints(new MultiUserDataMessage(["user1", "user2"], null)).ToList();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task TestEndpointManagerWithDuplicateEndpoints()
    {
        var sem = new TestServiceEndpointManager(
            new ServiceEndpoint(_connectionString1, EndpointType.Primary, "1"),
            new ServiceEndpoint(_connectionString1, EndpointType.Secondary, "2"),
            new ServiceEndpoint(_connectionString2, EndpointType.Secondary, "11"),
            new ServiceEndpoint(_connectionString2, EndpointType.Secondary, "12")
            );
        var endpoints = sem.Endpoints.Keys.OrderBy(x => x.Name).ToArray();
        Assert.Equal(2, endpoints.Length);
        Assert.Equal("1", endpoints[0].Name);
        Assert.Equal("11", endpoints[1].Name);

        var router = new TestEndpointRouter(false);
        var container = new TestMultiEndpointServiceConnectionContainer("hub",
            e => new TestBaseServiceConnectionContainer([
            new TestServiceConnection(),
            new TestServiceConnection(),
        ], e), sem, router, NullLoggerFactory.Instance);

        _ = container.StartAsync();
        await container.ConnectionInitializedTask.OrTimeout();

        endpoints = container.GetOnlineEndpoints().ToArray();
        Assert.Equal(2, endpoints.Length);
    }

    [Fact]
    public async Task TestEndpointManagerWithDuplicateEndpointsAndConnectionStarted()
    {
        var sem = new TestServiceEndpointManager(
            new ServiceEndpoint(_connectionString1, EndpointType.Primary, "1"),
            new ServiceEndpoint(_connectionString1, EndpointType.Secondary, "2"),
            new ServiceEndpoint(_connectionString2, EndpointType.Secondary, "11"),
            new ServiceEndpoint(_connectionString2, EndpointType.Secondary, "12")
            );
        var endpoints = sem.Endpoints.Keys.OrderBy(x => x.Name).ToArray();
        Assert.Equal(2, endpoints.Length);
        Assert.Equal("1", endpoints[0].Name);
        Assert.Equal("11", endpoints[1].Name);

        var router = new TestEndpointRouter(false);
        var container = new TestMultiEndpointServiceConnectionContainer("hub",
            e => new TestBaseServiceConnectionContainer([
            new TestServiceConnection(),
            new TestServiceConnection(),
        ], e), sem, router, NullLoggerFactory.Instance);

        _ = container.StartAsync();
        await container.ConnectionInitializedTask.OrTimeout();

        endpoints = [.. container.GetOnlineEndpoints().OrderBy(x => x.Name)];
        Assert.Equal(2, endpoints.Length);
        Assert.Equal("1", endpoints[0].Name);
        Assert.Equal("11", endpoints[1].Name);
    }

    [Fact]
    public void TestContainerWithNoEndpointThrowException()
    {
        Assert.Throws<AzureSignalRConfigurationNoEndpointException>(() => new TestServiceEndpointManager());
    }

    [Fact]
    public void TestContainerWithNoPrimaryEndpointDefinedThrows()
    {
        Assert.Throws<AzureSignalRNoPrimaryEndpointException>(() => new TestServiceEndpointManager(
        [
            new ServiceEndpoint(_connectionString1, EndpointType.Secondary)
        ]));
    }

    [Fact]
    public async Task TestContainerWithOneEndpointWithAllConnectedSucceeeds()
    {
        var sem = new TestServiceEndpointManager(new ServiceEndpoint(_connectionString1));
        var router = new DefaultEndpointRouter();
        var container = new TestMultiEndpointServiceConnectionContainer("hub",
            e => new TestBaseServiceConnectionContainer([
            new TestServiceConnection(),
            new TestServiceConnection(),
            new TestServiceConnection(),
            new TestServiceConnection(),
            new TestServiceConnection(),
            new TestServiceConnection(),
            new TestServiceConnection(),
        ], e), sem, router, NullLoggerFactory.Instance);

        _ = container.StartAsync();
        await container.ConnectionInitializedTask.OrTimeout();

        await container.WriteAsync(DefaultGroupMessage);
    }

    [Fact]
    public async Task TestContainerWithOneEndpointCustomizeRouterWithAllConnectedSucceeeds()
    {
        var sem = new TestServiceEndpointManager(new ServiceEndpoint(_connectionString1));
        var router = new TestEndpointRouter(false);
        var container = new TestMultiEndpointServiceConnectionContainer("hub",
            e => new TestBaseServiceConnectionContainer([
            new TestServiceConnection(),
            new TestServiceConnection(),
            new TestServiceConnection(),
            new TestServiceConnection(),
            new TestServiceConnection(),
            new TestServiceConnection(),
            new TestServiceConnection(),
        ], e), sem, router, NullLoggerFactory.Instance);

        _ = container.StartAsync();
        await container.ConnectionInitializedTask.OrTimeout();

        await container.WriteAsync(DefaultGroupMessage);
    }

    [Fact]
    public async Task TestContainerWithOneEndpointBadRouterWithConnectionStartedThrows()
    {
        var sem = new TestServiceEndpointManager(new ServiceEndpoint(_connectionString1));
        var router = new TestEndpointRouter(true);
        var container = new TestMultiEndpointServiceConnectionContainer("hub",
            e => new TestBaseServiceConnectionContainer([
            new TestServiceConnection(),
            new TestServiceConnection(),
            new TestServiceConnection(),
            new TestServiceConnection(),
            new TestServiceConnection(),
            new TestServiceConnection(),
            new TestServiceConnection(),
        ], e), sem, router, NullLoggerFactory.Instance);

        _ = container.StartAsync();
        await container.ConnectionInitializedTask.OrTimeout();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => container.WriteAsync(DefaultGroupMessage)
            );
    }

    [Fact]
    public async Task TestContainerWithOneEndpointWithAllDisconnectedAndWriteMessageThrows()
    {
        using (StartVerifiableLog(out var loggerFactory, LogLevel.Warning))
        {
            var endpoint = new ServiceEndpoint(_connectionString1);
            var sem = new TestServiceEndpointManager(endpoint);
            var router = new DefaultEndpointRouter();
            var container = new TestMultiEndpointServiceConnectionContainer("hub",
                e => new TestBaseServiceConnectionContainer([
                new TestServiceConnection(ServiceConnectionStatus.Disconnected),
                new TestServiceConnection(ServiceConnectionStatus.Disconnected),
                new TestServiceConnection(ServiceConnectionStatus.Disconnected),
                new TestServiceConnection(ServiceConnectionStatus.Disconnected),
                new TestServiceConnection(ServiceConnectionStatus.Disconnected),
                new TestServiceConnection(ServiceConnectionStatus.Disconnected),
                new TestServiceConnection(ServiceConnectionStatus.Disconnected),
            ], e), sem, router, loggerFactory);

            _ = container.StartAsync();
            await container.ConnectionInitializedTask.OrTimeout();
            var exception = await Assert.ThrowsAsync<FailedWritingMessageToServiceException>(() => container.WriteAsync(DefaultGroupMessage));
            Assert.Equal(endpoint.ServerEndpoint.AbsoluteUri, exception.EndpointUri);
            Assert.Equal($"Unable to write message to endpoint: {endpoint.ServerEndpoint.AbsoluteUri}", exception.Message);
        }
    }

    [Fact]
    public async Task TestContainerWithTwoEndpointWithAllConnectedFailsWithBadRouter()
    {
        using (StartVerifiableLog(out var loggerFactory, LogLevel.Debug, logChecker: logs => true))
        {
            var sem = new TestServiceEndpointManager(
                new ServiceEndpoint(_connectionString1),
                new ServiceEndpoint(_connectionString2));
            var logger = loggerFactory.CreateLogger<TestServiceConnection>();
            var router = new TestEndpointRouter(true);
            var container = new TestMultiEndpointServiceConnectionContainer("hub",
                e => new TestBaseServiceConnectionContainer([
                    new TestServiceConnection(logger: logger),
                    new TestServiceConnection(logger: logger),
                    new TestServiceConnection(logger: logger),
                    new TestServiceConnection(logger: logger),
                    new TestServiceConnection(logger: logger),
                    new TestServiceConnection(logger: logger),
                    new TestServiceConnection(logger: logger),
                ], e, logger), sem, router, loggerFactory);
            _ = Task.Run(container.StartAsync);
            await container.ConnectionInitializedTask.OrTimeout();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => container.WriteAsync(DefaultGroupMessage)
            );
        }
    }

    [Fact]
    public async Task TestContainerWithTwoEndpointWithAllConnectedSucceedsWithGoodRouter()
    {
        using (StartVerifiableLog(out var loggerFactory, LogLevel.Warning, logChecker: logs => true))
        {
            var sem = new TestServiceEndpointManager(
                new ServiceEndpoint(_connectionString1),
                new ServiceEndpoint(_connectionString2));

            var router = new TestEndpointRouter(false);
            var container = new TestMultiEndpointServiceConnectionContainer("hub",
                e => new TestBaseServiceConnectionContainer([
                    new TestServiceConnection(),
                    new TestServiceConnection(),
                    new TestServiceConnection(),
                    new TestServiceConnection(),
                    new TestServiceConnection(),
                    new TestServiceConnection(),
                    new TestServiceConnection(),
                ], e), sem, router, loggerFactory);

            _ = container.StartAsync();
            await container.ConnectionInitializedTask.OrTimeout();

            await container.WriteAsync(DefaultGroupMessage);
        }
    }

    [Fact]
    public async Task TestContainerWithTwoEndpointWithAllOfflineSucceedsWithWarning()
    {
        using (StartVerifiableLog(out var loggerFactory, LogLevel.Warning, logChecker: logs =>
        {
            var warns = logs.Where(s => s.Write.LogLevel == LogLevel.Warning).ToList();

            Assert.Single(warns);
            Assert.Equal("Message JoinGroupWithAckMessage is not sent because no endpoint is returned from the endpoint router.", warns[0].Write.Message);
            return true;
        }))
        {
            var sem = new TestServiceEndpointManager(
            new ServiceEndpoint(_connectionString1),
            new ServiceEndpoint(_connectionString2));

            var router = new TestEndpointRouter(false);
            var container = new TestMultiEndpointServiceConnectionContainer("hub",
                e => new TestBaseServiceConnectionContainer([
            new TestServiceConnection(ServiceConnectionStatus.Disconnected),
            new TestServiceConnection(ServiceConnectionStatus.Disconnected),
            new TestServiceConnection(ServiceConnectionStatus.Disconnected),
            new TestServiceConnection(ServiceConnectionStatus.Disconnected),
            new TestServiceConnection(ServiceConnectionStatus.Disconnected),
            new TestServiceConnection(ServiceConnectionStatus.Disconnected),
            new TestServiceConnection(ServiceConnectionStatus.Disconnected),
            ], e), sem, router, loggerFactory);

            _ = container.StartAsync();
            await container.ConnectionInitializedTask.OrTimeout();
            await container.WriteAsync(DefaultGroupMessage);
        }
    }

    [Fact]
    public async Task TestContainerWithTwoEndpointWithOneOfflineSucceeds()
    {
        using (StartVerifiableLog(out var loggerFactory, LogLevel.Warning))
        {
            var sem = new TestServiceEndpointManager(
            new ServiceEndpoint(_connectionString1),
            new ServiceEndpoint(_connectionString2, name: "online"));

            var router = new TestEndpointRouter(false);
            var container = new TestMultiEndpointServiceConnectionContainer("hub", e => string.IsNullOrEmpty(e.Name)
                    ? new TestBaseServiceConnectionContainer([
                    new TestServiceConnection(ServiceConnectionStatus.Disconnected),
                    new TestServiceConnection(ServiceConnectionStatus.Disconnected),
                    new TestServiceConnection(ServiceConnectionStatus.Disconnected),
                    new TestServiceConnection(ServiceConnectionStatus.Disconnected),
                    new TestServiceConnection(ServiceConnectionStatus.Disconnected),
                    new TestServiceConnection(ServiceConnectionStatus.Disconnected),
                    new TestServiceConnection(ServiceConnectionStatus.Disconnected),
                    ], e)
                    : (IServiceConnectionContainer)new TestBaseServiceConnectionContainer([
                    new TestServiceConnection(),
                    new TestServiceConnection(),
                    new TestServiceConnection(),
                    new TestServiceConnection(),
                    new TestServiceConnection(),
                    new TestServiceConnection(),
                    new TestServiceConnection(),
                    ], e), sem, router, loggerFactory);

            _ = container.StartAsync();
            await container.ConnectionInitializedTask.OrTimeout();
            await container.WriteAsync(DefaultGroupMessage);
        }
    }

    [Fact]
    public async Task TestContainerWithTwoEndpointWithPrimaryOfflineAndConnectionStartedSucceeds()
    {
        var sem = new TestServiceEndpointManager(
            new ServiceEndpoint(_connectionString1),
            new ServiceEndpoint(_connectionString2, EndpointType.Secondary, "online"));

        var router = new TestEndpointRouter(false);
        var container = new TestMultiEndpointServiceConnectionContainer("hub", e => string.IsNullOrEmpty(e.Name)
                ? new TestBaseServiceConnectionContainer([
                    new TestServiceConnection(ServiceConnectionStatus.Disconnected),
                    new TestServiceConnection(ServiceConnectionStatus.Disconnected),
                    new TestServiceConnection(ServiceConnectionStatus.Disconnected),
                    new TestServiceConnection(ServiceConnectionStatus.Disconnected),
                    new TestServiceConnection(ServiceConnectionStatus.Disconnected),
                    new TestServiceConnection(ServiceConnectionStatus.Disconnected),
                    new TestServiceConnection(ServiceConnectionStatus.Disconnected),
                ], e)
                : (IServiceConnectionContainer)new TestBaseServiceConnectionContainer([
                    new TestServiceConnection(),
                    new TestServiceConnection(),
                    new TestServiceConnection(),
                    new TestServiceConnection(),
                    new TestServiceConnection(),
                    new TestServiceConnection(),
                    new TestServiceConnection(),
                ], e), sem, router, NullLoggerFactory.Instance);

        _ = container.StartAsync();

        await container.ConnectionInitializedTask;

        await container.WriteAsync(DefaultGroupMessage);

        var endpoints = container.GetOnlineEndpoints().ToArray();
        Assert.Single(endpoints);

        Assert.Equal("online", endpoints.First().Name);
    }

    private sealed class TestMultiEndpointServiceConnectionContainer(string hub,
                                                                     Func<HubServiceEndpoint, IServiceConnectionContainer> generator,
                                                                     IServiceEndpointManager endpoint,
                                                                     IEndpointRouter router,
                                                                     ILoggerFactory loggerFactory) : MultiEndpointServiceConnectionContainer(hub, generator, endpoint, router, loggerFactory)
    {
    }

    private class TestServiceEndpointManager(params ServiceEndpoint[] endpoints) : ServiceEndpointManagerBase(endpoints, NullLogger.Instance)
    {
        public override IServiceEndpointProvider GetEndpointProvider(ServiceEndpoint endpoint)
        {
            return null;
        }
    }

    private class TestEndpointRouter(bool broken) : EndpointRouterDecorator()
    {
        private readonly bool _broken = broken;

        public override IEnumerable<ServiceEndpoint> GetEndpointsForBroadcast(IEnumerable<ServiceEndpoint> endpoints)
        {
            return _broken ? throw new InvalidOperationException() : base.GetEndpointsForBroadcast(endpoints);
        }

        public override IEnumerable<ServiceEndpoint> GetEndpointsForConnection(string connectionId, IEnumerable<ServiceEndpoint> endpoints)
        {
            return _broken ? throw new InvalidOperationException() : base.GetEndpointsForConnection(connectionId, endpoints);
        }

        public override IEnumerable<ServiceEndpoint> GetEndpointsForGroup(string groupName, IEnumerable<ServiceEndpoint> endpoints)
        {
            return _broken ? throw new InvalidOperationException() : base.GetEndpointsForGroup(groupName, endpoints);
        }

        public override IEnumerable<ServiceEndpoint> GetEndpointsForUser(string userId, IEnumerable<ServiceEndpoint> endpoints)
        {
            return _broken ? throw new InvalidOperationException() : base.GetEndpointsForUser(userId, endpoints);
        }

        public override ServiceEndpoint GetNegotiateEndpoint(IOwinContext context, IEnumerable<ServiceEndpoint> endpoints)
        {
            return _broken ? throw new InvalidOperationException() : base.GetNegotiateEndpoint(context, endpoints);
        }
    }
}
