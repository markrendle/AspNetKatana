﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Owin;
using Shouldly;
using Xunit;

namespace Katana.Server.AspNet.Tests
{
    public class OwinHttpHandlerTests : TestsBase
    {

        [Fact]
        public void ProcessRequestIsNotImplemeted()
        {
            var httpHandler = new OwinHttpHandler();
            var httpContext = NewHttpContext(new Uri("http://localhost"));

            Should.Throw<NotImplementedException>(() => httpHandler.ProcessRequest(httpContext));
        }

        [Fact]
        public Task ItShouldCallAppDelegateWhenBeginProcessRequestCalled()
        {
            var httpHandler = new OwinHttpHandler("", WasCalledApp);
            var httpContext = NewHttpContext(new Uri("http://localhost"));

            var task = Task.Factory.FromAsync(httpHandler.BeginProcessRequest, httpHandler.EndProcessRequest, httpContext, null);
            return task.ContinueWith(_ => WasCalled.ShouldBe(true));
        }
    }
}
