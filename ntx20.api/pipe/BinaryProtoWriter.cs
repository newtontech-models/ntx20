using Google.Protobuf;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ntx20.api.pipe
{
    public class BinaryProtoWriter<T> : IAsyncSink<T> where T : Google.Protobuf.IMessage, new()
    {
        readonly System.IO.Stream stream = null;

        public BinaryProtoWriter(Stream stream)
        {
            this.stream = stream;
        }

        public async Task CompleteAsync(CancellationToken cancelationToken = default)
        {
            await stream.FlushAsync(cancelationToken);
        }

        public async Task FlushAsync(CancellationToken cancelationToken = default)
        {
            await stream.FlushAsync(cancelationToken);
        }

        public async Task WriteAsync(T message, CancellationToken cancelationToken = default)
        {
            int size = message.CalculateSize();
            var bSize = BitConverter.GetBytes(size);
            await stream.WriteAsync(bSize.AsMemory(0, bSize.Length), cancelationToken);
            byte[] result = new byte[size];
            CodedOutputStream output = new(result);
            message.WriteTo(output);
            await stream.WriteAsync(result.AsMemory(0, result.Length), cancelationToken);
            
        }
    }
}
