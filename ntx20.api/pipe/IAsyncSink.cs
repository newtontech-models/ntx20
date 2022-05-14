using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ntx20.api.pipe
{
    public interface IAsyncSink<T>
    {
        Task WriteAsync(T item, CancellationToken cancelationToken = default);
        Task CompleteAsync(CancellationToken cancelationToken = default);
        Task FlushAsync(CancellationToken cancelationToken = default);
    }
}
