// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.SignalR.Common.Tests;

public class PauseHandlerTests
{
    [Fact]
    public async Task TestPauseAndResume()
    {
        var handler = new PauseHandler();
        Assert.False(handler.ShouldPauseAck);

        await handler.PauseAsync();
        Assert.False(handler.Wait());

        Assert.True(handler.ShouldPauseAck);
        Assert.False(handler.ShouldPauseAck); // ack only once

        await handler.PauseAsync(); // pause can be called multiple times.
        Assert.False(handler.Wait());
        Assert.False(handler.ShouldPauseAck); // already acked previously

        await handler.ResumeAsync();
        Assert.True(handler.Wait());
        Assert.False(handler.Wait()); // only 1 parallel
        handler.Release();
        Assert.True(handler.Wait());
    }
}
