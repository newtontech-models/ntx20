using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using System.Security.Cryptography;

namespace ntx20.api.pipe
{
    
    public class RawStreamWriter : IAsyncSink<api.proto.Payload>
    {
        
        Stream stream = null;
        readonly Stream streamResolver;
        public RawStreamWriter(Stream streamResolver)
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

        public async Task WriteAsync(api.proto.Payload chunk, CancellationToken cancellationToken = default)
        {
            if (stream == null)
            {
                stream = streamResolver;
            }

            foreach(var c in chunk.Chunk)
            {
                

                if (c.Key.StartsWith("seek"))
                {
                    if (stream.CanSeek)
                    {
                        switch (c.Key)
                        {
                            case "seek:begin":
                                stream.Seek(c.I, SeekOrigin.Begin);
                                break;
                            case "seek:end":
                                stream.Seek(c.I, SeekOrigin.End);
                                break;
                            case "seek:current":
                                stream.Seek(c.I, SeekOrigin.Current);
                                break;
                            default:
                                throw new Exception($"Invalid seek to  {c.Key}");
                        }
                    }
                    else
                    {
                        throw new Exception("Output stream not seekable");
                    }

                    continue;
                }

                switch (c.Type)
                {
                    case "s":
                        {
                            var b = Encoding.UTF8.GetBytes(c.S);
                            await stream.WriteAsync(b.AsMemory(0, b.Length), cancellationToken);
                        }
                        break;
                    case "t":
                        await stream.WriteAsync(c.T.Data.ToByteArray().AsMemory(0, c.T.Data.Length), cancellationToken);
                        break;
                    case "b":
                        await stream.WriteAsync(c.B.ToByteArray().AsMemory(0, c.B.Length), cancellationToken);
                        break;
                    /*
                    case "d":
                        {
                            var b = BitConverter.GetBytes(c.DValue);
                            await stream.WriteAsync(b, 0, b.Length, cancellationToken);
                        }
                        break;
                    case "f":
                        {
                            var b = BitConverter.GetBytes(c.FValue);
                            await stream.WriteAsync(b, 0, b.Length, cancellationToken);
                        }
                        break;
                    case "i":
                        {
                            var b = BitConverter.GetBytes(c.IValue);
                            await stream.WriteAsync(b, 0, b.Length, cancellationToken);
                        }
                        break;
                    default:
                        throw new Exception($"Invalid raw write type {c}");
                    */

                }
            }
            
        }
    }
}
