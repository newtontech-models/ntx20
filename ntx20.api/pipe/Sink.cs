using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ntx20.api.pipe
{
    public static partial class Sink
    {
        public static IAsyncSink<string> AsTextChunkSink(this Stream stream)
        {
            return new StringChunkWriter(stream);
        }
        public static IAsyncSink<byte[]> AsBinaryChunkSink(this Stream stream)
        {
            return new BinaryChunkWriter(stream);  
        }

        public static IAsyncSink<api.proto.Payload> AsRawChunkSink(this Stream stream)
        {
            return new RawStreamWriter(stream);
        }


        


        public static IAsyncSink<T> AsBinaryProtoSink<T>(this Stream stream) where T : Google.Protobuf.IMessage, new()
        {
            return new BinaryProtoWriter<T>(stream);
        }

        public static IAsyncSink<T> AsJsonProtoSink<T>(this Stream stream) where T : Google.Protobuf.IMessage, new()
        {
            return new JsonProtoWriter<T>(stream);
        }

        public static IAsyncSink<T> AsGrpcSink<T>(this Grpc.Core.IServerStreamWriter<T> writer)
        {
            return new GrpcServerWriter<T>(writer);
        }

        public static IAsyncSink<T> AsGrpcSink<T>(this Grpc.Core.IClientStreamWriter<T> writer)
        {
            return new GrpcClientWriter<T>(writer);
        }

        public static IAsyncSink<T> NullSink<T>()
        {
            return new NullWriter<T>();
        }
        public static IAsyncSink<api.proto.Payload> ConsolePayloadSink(string track)
        {
            return new ConsoleWriter(track);
        }


    }
}
