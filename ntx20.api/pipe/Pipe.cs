using Google.Protobuf;
using Grpc.Core;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using ntx20.api.proto;
using ntx20.api.utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ntx20.api.pipe
{
    public static class Pipe
    {
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public static async IAsyncEnumerable<T> AsProtoSource<T>(this IEnumerable<T> list, [EnumeratorCancellation] CancellationToken cancellationToken = default)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            foreach (T v in list)
            {
                if (cancellationToken != default && cancellationToken.IsCancellationRequested)
                    break;
                yield return v;
            }
        }

        public static async Task RunWithSink<T>(this IAsyncEnumerable<T> source, IAsyncSink<T> sink, bool autoFlush = true, bool autoComplete = true, CancellationToken cancellationToken = default)
        {
            await foreach(T v in source)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                await sink.WriteAsync(v, cancellationToken);
                if(autoFlush)
                    await sink.FlushAsync(cancellationToken);
            }
            if(autoComplete)
                await sink.CompleteAsync(cancellationToken);
        }

        public static async IAsyncEnumerable<Y> ViaMapper<X,Y>(this IAsyncEnumerable<X> source, Func<X,Y> mapper)
        {
            await foreach (X x in source)
            {
                yield return mapper(x);
            }
        }

        public static async IAsyncEnumerable<proto.Tensor> ToTensor(this IAsyncEnumerable<byte[]> source)
        {
            await foreach (var x in source)
            {
                var ret = new proto.Tensor() { Data = Google.Protobuf.ByteString.CopyFrom(x, 0, x.Length) };
                yield return ret;
            }
        }

        public static async IAsyncEnumerable<byte[]> ToBinary(this IAsyncEnumerable<proto.Tensor> source)
        {
            await foreach (var x in source)
            {
                if(x.Data != null)
                    yield return x.Data.ToArray();
            }
        }

        public static async IAsyncEnumerable<proto.Payload> ToStreamOfBytes(this IAsyncEnumerable<byte[]> source)
        {
            await foreach (var x in source)
            {
                var ret =
                new proto.Payload { };
                ret.Chunk.Add(
                    new proto.Item
                    {
                        B = Google.Protobuf.ByteString.CopyFrom(x, 0, x.Length)
                    }
                );
                yield return ret;
            }
        }

        public static async IAsyncEnumerable<proto.Tensor> ToTensor(this IAsyncEnumerable<proto.Payload> source)
        {
            await foreach (var x in source)
            {
                

                foreach(var c in x.Chunk)
                {
                    if(c.B.Length > 0)
                        yield return new api.proto.Tensor { Data = c.B };

                    if(c.T != null)
                        yield return c.T;

                }
                
            }
        }

        public static async IAsyncEnumerable<proto.Payload> PrintTensor(this IAsyncEnumerable<proto.Payload> source)
        {
            await foreach (var x in source)
            {
                var ret = new Payload { };
                foreach (var v in x.Chunk)
                {
                    if(v.Type == "t")
                    {
                        ret.Chunk.Add(new Item { Type = "s", S = v.T.Print() + "\n" });
                    }
                }
                yield return ret;

            }
        }


        public static async IAsyncEnumerable<string> ToText(this IAsyncEnumerable<proto.Payload> source)
        {
            await foreach (var x in source)
            {
                foreach(var v  in x.Chunk)
                {
                   if(v.Key == "label")
                   {
                        if ((v.Type == "item" || v.Type == "plus") && !v.Tags.Contains("la")) 
                            
                            yield return v.S;
                   }
                }

            }
        }

        public static async IAsyncEnumerable<byte[]> ToBinary(this IAsyncEnumerable<string> source)
        {
            await foreach (var x in source)
            {
                yield return UTF8Encoding.UTF8.GetBytes(x);
            }
        }

        public static async IAsyncEnumerable<T> InterceptAsync<T>(this IAsyncEnumerable<T> source, Func<T,Task> action)
        {
            await foreach(var x in source)
            {
                await action(x);
                yield return x;
            }
        }

        public static async IAsyncEnumerable<T> Intercept<T>(this IAsyncEnumerable<T> source, Action<T> action)
        {
            await foreach (var x in source)
            {
                action(x);
                yield return x;
            }
        }

        public static async IAsyncEnumerable<Payload> InterceptItem(this IAsyncEnumerable<Payload> source, Action<Item> action)
        {
            await foreach (var x in source)
            {
                foreach (var c in x.Chunk)
                {
                    action(c);
                }
                yield return x;
            }
        }

        public static async IAsyncEnumerable<Payload> RemoveItem(this IAsyncEnumerable<Payload> source, Func<Item, bool> condition)
        {
            
            await foreach (var x in source)
            {
                var ret = new Payload() {Track = x.Track };
                foreach (var c in x.Chunk)
                {
                    if (!condition(c))
                        ret.Chunk.Add(c);
                }
                yield return ret;
            }
        }

        public static async IAsyncEnumerable<T> Remove<T>(this IAsyncEnumerable<T> source, Func<T, bool> condition)
        {
            await foreach (var x in source)
            {
                if (!condition(x))
                {
                    yield return x;
                }
            }
        }

        public static async IAsyncEnumerable<byte[]> TensorToBinaryChunk(this IAsyncEnumerable<proto.Payload> source)
        {
            await foreach (var x in source)
            {
                foreach(var z in x?.Chunk){
                    var d = z?.T?.Data?.ToArray();
                    yield return d;
                }
            }
        }

        
        public static async IAsyncEnumerable<byte[]> ToBinaryProto<T>(this IAsyncEnumerable<T> source) where T : Google.Protobuf.IMessage
        {
            await foreach (var message in source)
            {
                using var m = new MemoryStream();
                int size = message.CalculateSize();
                var bSize = BitConverter.GetBytes(size);
                await m.WriteAsync(bSize.AsMemory(0, bSize.Length));
                byte[] result = new byte[size];
                CodedOutputStream output = new(m);
                message.WriteTo(output);
                yield return m.ToArray();
            }
        }

        public static async IAsyncEnumerable<byte[]> ToJsonProto<T>(this IAsyncEnumerable<T> source) where T : Google.Protobuf.IMessage
        {
            await foreach (var message in source)
            {
                using var m = new MemoryStream();

                using (var s = new StreamWriter(m, new UTF8Encoding(false)))
                {
                    await s.WriteLineAsync(m.ToString());
                };
                yield return m.ToArray();
            }
        }

        public static async Task<proto.Payload> Configure(this Grpc.Core.AsyncDuplexStreamingCall<proto.Payload, proto.Payload> call, proto.Payload config)
        {
            await call.RequestStream.WriteAsync(config);
            await call.ResponseStream.MoveNext();
            return call.ResponseStream.Current;
        }

        public static async IAsyncEnumerable<proto.Payload> ViaGRPCCall(this IAsyncEnumerable<proto.Payload> source, Grpc.Core.AsyncDuplexStreamingCall<proto.Payload, proto.Payload> call)
        {
            var upstream = source.RunWithSink(call.RequestStream.AsGrpcSink());

            await foreach (var i in call.ResponseStream.AsProtoSource())
            {
                yield return i;
            }

            await upstream;
        }

        public static async IAsyncEnumerable<proto.Payload> ViaTaskRunner(this IAsyncEnumerable<proto.Payload> source, Grpc.Core.AsyncDuplexStreamingCall<proto.Payload, proto.Payload> call, 
            string[] accepts , bool pipeMode, int bufferSize = 10)
        {
            using BlockingCollection<proto.Payload> bc = bufferSize == 0 ? new BlockingCollection<proto.Payload>() : new BlockingCollection<proto.Payload>(bufferSize);
            SemaphoreSlim _semaphore = new SemaphoreSlim(0);

            if (pipeMode)
            {
                source = source.Intercept(x => {
                    bc.Add(x.Clone());
                    _semaphore.Release();
                });
            }


            var writer = Task.Run(async () =>
            {
                await foreach (var v in source.Remove(x => !accepts.Contains(x.Track)).ViaGRPCCall(call))
                {
                    bc.Add(v);
                    _semaphore.Release();
                }
                bc.CompleteAdding();
                _semaphore.Release();
            });


            while (true)
            {
                await _semaphore.WaitAsync();
                if (bc.IsCompleted)
                    break;
                yield return bc.Take();
            }


            await writer;
        }
    }
}

