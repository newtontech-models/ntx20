using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Threading;

namespace ntx20.api.pipe
{
    public class StringChunkListWriter : IAsyncSink<string>
    {
        readonly List<string> streamResolver;

        public StringChunkListWriter(List<string> streamResolver)
        {
            this.streamResolver = streamResolver;
        }
        public Task FlushAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
        public Task WriteAsync(string item, CancellationToken cancellationToken = default)
        {
            streamResolver.Add(item);
            return Task.CompletedTask;
        }
        public Task CompleteAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
