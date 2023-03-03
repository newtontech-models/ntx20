using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Threading;

namespace ntx20.api.pipe
{
    public class StringChunkWriter : IAsyncSink<string>
    {
        StreamWriter stream = null;
        readonly Stream streamResolver;
        
        public StringChunkWriter(Stream streamResolver)
        {
            this.streamResolver = streamResolver;
        }
        public async Task FlushAsync(CancellationToken cancellationToken = default)
        {
            if (stream == null)
            {
                stream = new StreamWriter(streamResolver, new UTF8Encoding(false));
            }
            await stream.FlushAsync();
        }
        public async  Task WriteAsync(string item, CancellationToken cancellationToken = default)
        {
            if (stream == null)
            {
                stream = new StreamWriter(streamResolver, new UTF8Encoding(false));
            }

            await stream.WriteAsync(item);
        }
        public Task CompleteAsync(CancellationToken cancellationToken = default)
        {
            if (stream == null)
            {
                stream = new StreamWriter(streamResolver, new UTF8Encoding(false));
            }
            return Task.CompletedTask;
        }
    }
}
