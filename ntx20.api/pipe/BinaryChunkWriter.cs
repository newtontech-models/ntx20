using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Threading;

namespace ntx20.api.pipe
{
    public class BinaryChunkWriter : IAsyncSink<byte[]>
    {
        Stream stream = null;
        readonly Stream streamResolver;
        public BinaryChunkWriter(Stream streamResolver)
        {
            this.streamResolver = streamResolver;
        }

        public async Task FlushAsync(CancellationToken cancellationToken = default)
        {
            await stream.FlushAsync(cancellationToken);
        }
        public Task CompleteAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public async Task WriteAsync(byte[] chunk, CancellationToken cancellationToken = default)
        {
            if (stream == null)
            {
                stream = streamResolver;
            }

            await stream.WriteAsync(chunk.AsMemory(0, chunk.Length), cancellationToken);
        }
    }
}
