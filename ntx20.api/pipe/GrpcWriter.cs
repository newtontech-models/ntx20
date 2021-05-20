using Grpc.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ntx20.api.pipe
{
    class GrpcServerWriter<T> : IAsyncSink<T>
    {
        private readonly IServerStreamWriter<T> writer;

        public GrpcServerWriter(IServerStreamWriter<T> writer){
            this.writer = writer;
        }
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async Task CompleteAsync(CancellationToken cancelationToken = default)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async Task FlushAsync(CancellationToken cancelationToken = default)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
                    }

        public async Task WriteAsync(T item, CancellationToken cancelationToken = default)
        {
            await writer.WriteAsync(item);
        }
    }

    class GrpcClientWriter<T> : IAsyncSink<T>
    {
        readonly IClientStreamWriter<T> writer;

        public GrpcClientWriter(IClientStreamWriter<T> writer)
        {
            this.writer = writer;
        }
        public async Task CompleteAsync(CancellationToken cancelationToken = default)
        {
            await writer.CompleteAsync();
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async Task FlushAsync(CancellationToken cancelationToken = default)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
        }

        public async Task WriteAsync(T item, CancellationToken cancelationToken = default)
        {
            await writer.WriteAsync(item);
        }
    }
}
