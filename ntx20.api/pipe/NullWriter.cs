using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ntx20.api.pipe
{
    public class NullWriter<T> : IAsyncSink<T>
    {
        public Task WriteAsync(T item, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
        public Task CompleteAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
        public Task FlushAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
