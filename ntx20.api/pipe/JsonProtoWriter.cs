using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ntx20.api.pipe
{
    public class JsonProtoWriter<T> : IAsyncSink<T> where T : Google.Protobuf.IMessage, new()
    {
        readonly StreamWriter stream=null;

        public JsonProtoWriter(Stream stream)
        {
            this.stream = new StreamWriter(stream, new UTF8Encoding(false))
            {
                AutoFlush = true
            };
        }

        public async Task WriteAsync(T message, CancellationToken cancelationToken = default)
        {
            await stream.WriteLineAsync(message.ToString());
        }

        public async Task CompleteAsync(CancellationToken cancelationToken = default)
        {
            await stream.FlushAsync();
        }

        public async Task FlushAsync(CancellationToken cancelationToken = default)
        {
            await stream.FlushAsync();
        }
    }
}
