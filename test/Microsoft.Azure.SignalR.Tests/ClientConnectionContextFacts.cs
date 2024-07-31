// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Protocol;
using Xunit;

using ServiceProtocol = Microsoft.Azure.SignalR.Protocol;

namespace Microsoft.Azure.SignalR.Tests;

#nullable enable

public class ClientConnectionContextFacts
{
    public static byte[] ReadOnlySequenceToArray(ReadOnlySequence<byte> sequence)
    {
        // Calculate the total length of the sequence
        var length = sequence.Length;

        // Create a byte array to hold the data
        var result = new byte[length];

        // Copy the data from the ReadOnlySequence to the byte array
        sequence.CopyTo(result);

        return result;
    }

    [Fact]
    public void SetUserIdFeatureTest()
    {
        var claims = new Claim[] { new(Constants.ClaimType.UserId, "testUser") };
        var connection = new ClientConnectionContext(new("connectionId", claims));
        var feature = connection.Features.Get<ServiceUserIdFeature>();
        Assert.NotNull(feature);
        Assert.Equal("testUser", feature.UserId);
    }

    [Fact]
    public void DoNotSetUserIdFeatureWithoutUserIdClaimTest()
    {
        var connection = new ClientConnectionContext(new("connectionId", Array.Empty<Claim>()));
        var feature = connection.Features.Get<ServiceUserIdFeature>();
        Assert.Null(feature);
    }

    [Fact]
    public async void TestForwardCloseMessage()
    {
        var pipeOptions = new PipeOptions();
        var pair = DuplexPipe.CreateConnectionPair(pipeOptions, pipeOptions);

        var serviceConnection = new TestServiceConnection();

        var connectionId = "testConnectionId";
        var connection = new ClientConnectionContext(new(connectionId, Array.Empty<Claim>()))
        {
            Application = pair.Application,
            ServiceConnection = serviceConnection
        };

        var protocol = new JsonHubProtocol();
        _ = connection.ProcessOutgoingMessagesAsync(protocol);

        var response = new HandshakeResponseMessage(null);
        HandshakeProtocol.WriteResponseMessage(response, pair.Transport.Output);

        var error = "foo";
        var closeMessage = new CloseMessage(error);
        protocol.WriteMessage(closeMessage, pair.Transport.Output);
        await pair.Transport.Output.FlushAsync();

        // forward handshake response;
        var delayTask = Task.Delay(3000);
        Assert.NotSame(delayTask, await Task.WhenAny(serviceConnection.MessageTask, delayTask));
        serviceConnection.Reset();

        var message = Assert.IsType<ServiceProtocol.ConnectionDataMessage>(await serviceConnection.MessageTask);
        Assert.Equal(ServiceProtocol.DataMessageType.Close, message.Type);
        Assert.Equal(connectionId, message.ConnectionId);

        var buffer = new ArrayBufferWriter<byte>();
        protocol.WriteMessage(closeMessage, buffer);
        Assert.Equal(buffer.WrittenSpan.ToArray(), message.Payload.ToArray());
    }

    [Fact]
    public async void TestForwardInvocationMessage()
    {
        var pipeOptions = new PipeOptions();
        var pair = DuplexPipe.CreateConnectionPair(pipeOptions, pipeOptions);

        var serviceConnection = new TestServiceConnection();

        var connectionId = "testConnectionId";
        var connection = new ClientConnectionContext(new(connectionId, Array.Empty<Claim>()))
        {
            Application = pair.Application,
            ServiceConnection = serviceConnection
        };

        var protocol = new JsonHubProtocol();
        _ = connection.ProcessOutgoingMessagesAsync(protocol);

        var response = new HandshakeResponseMessage(null);
        HandshakeProtocol.WriteResponseMessage(response, pair.Transport.Output);

        var invocationId = "id";
        var invocationMessage = new InvocationMessage(invocationId, "foo", new string[] { "1", "2" });
        protocol.WriteMessage(invocationMessage, pair.Transport.Output);
        await pair.Transport.Output.FlushAsync();

        // forward handshake response;
        var delayTask = Task.Delay(3000);
        Assert.NotSame(delayTask, await Task.WhenAny(serviceConnection.MessageTask, delayTask));
        serviceConnection.Reset();

        var message = Assert.IsType<ServiceProtocol.ConnectionDataMessage>(await serviceConnection.MessageTask);
        Assert.Equal(ServiceProtocol.DataMessageType.Invocation, message.Type);
        Assert.Equal(connectionId, message.ConnectionId);

        var buffer = new ArrayBufferWriter<byte>();
        protocol.WriteMessage(invocationMessage, buffer);
        Assert.Equal(buffer.WrittenSpan.ToArray(), message.Payload.ToArray());
    }

    [Fact]
    public async void TestForwardHandshakeResponse()
    {
        var pipeOptions = new PipeOptions();
        var pair = DuplexPipe.CreateConnectionPair(pipeOptions, pipeOptions);

        var serviceConnection = new TestServiceConnection();

        var connectionId = "testConnectionId";
        var connection = new ClientConnectionContext(new(connectionId, Array.Empty<Claim>()))
        {
            Application = pair.Application,
            ServiceConnection = serviceConnection
        };

        var protocol = new JsonHubProtocol();
        _ = connection.ProcessOutgoingMessagesAsync(protocol);

        var response = new HandshakeResponseMessage(null);
        HandshakeProtocol.WriteResponseMessage(response, pair.Transport.Output);
        await pair.Transport.Output.FlushAsync();

        var message = Assert.IsType<ServiceProtocol.ConnectionDataMessage>(await serviceConnection.MessageTask);
        Assert.Equal(ServiceProtocol.DataMessageType.Handshake, message.Type);
        Assert.Equal(connectionId, message.ConnectionId);

        var buffer = new ArrayBufferWriter<byte>();
        HandshakeProtocol.WriteResponseMessage(response, buffer);
        Assert.Equal(buffer.WrittenSpan.ToArray(), ReadOnlySequenceToArray(message.Payload));
    }

    [Fact]
    public async void TestSkipHandshakeResponse()
    {
        var pipeOptions = new PipeOptions();
        var pair = DuplexPipe.CreateConnectionPair(pipeOptions, pipeOptions);

        var serviceConnection = new TestServiceConnection();

        var connectionId = "testConnectionId";
        var connection = new ClientConnectionContext(new(connectionId, Array.Empty<Claim>())
        {
            Headers =
            {
                { Constants.AsrsMigrateFrom, "from-server"}
            }
        })
        {
            Application = pair.Application,
            ServiceConnection = serviceConnection
        };

        var protocol = new JsonHubProtocol();
        _ = connection.ProcessOutgoingMessagesAsync(protocol);

        var response = new HandshakeResponseMessage(null);
        HandshakeProtocol.WriteResponseMessage(response, pair.Transport.Output);
        await pair.Transport.Output.FlushAsync();

        var delayTask = Task.Delay(TimeSpan.FromSeconds(3));
        var actual = await Task.WhenAny(serviceConnection.MessageTask, delayTask);
        Assert.Same(delayTask, actual);
    }

    private sealed class TestServiceConnection : IServiceConnection
    {
        private TaskCompletionSource<ServiceProtocol.ServiceMessage> _messageTcs = new();

        public string ConnectionId => throw new NotImplementedException();

        public string ServerId => throw new NotImplementedException();

        public ServiceConnectionStatus Status => throw new NotImplementedException();

        public Task ConnectionInitializedTask => throw new NotImplementedException();

        public Task ConnectionOfflineTask => throw new NotImplementedException();

        public Task<ServiceProtocol.ServiceMessage> MessageTask => _messageTcs.Task;

        public event Action<StatusChange>? ConnectionStatusChanged;

        public Task<bool> SafeWriteAsync(ServiceProtocol.ServiceMessage serviceMessage)
        {
            throw new NotImplementedException();
        }

        public Task StartAsync(string? target = null)
        {
            throw new NotImplementedException();
        }

        public Task StopAsync()
        {
            throw new NotImplementedException();
        }

        public void Reset()
        {
            _messageTcs = new();
        }

        public Task WriteAsync(ServiceProtocol.ServiceMessage serviceMessage)
        {
            _messageTcs.TrySetResult(serviceMessage);
            return Task.CompletedTask;
        }
    }
}
