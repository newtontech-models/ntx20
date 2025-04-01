using Microsoft.Extensions.Logging;
using ntx20.api.proto.legacy.v2t.engine;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;


namespace ntx20.api.pipe
{
    public static class Source
    {
        private static readonly ILogger _logger = Logging.LoggerFactory.CreateLogger("ntx20.api.pipe.source");
        public static async IAsyncEnumerable<string> AsTextChunkSource(this Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {

            using (var sr = new StreamReader(stream, Encoding.UTF8))
            {
                while (true)
                {
                    string ret = null;
                    try
                    {
                        ret = await sr.ReadLineAsync();
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                    if (ret == null)
                        break;
                    yield return ret;

                }
            }
        }

        public static async IAsyncEnumerable<byte[]> AsBinaryChunkSource(this Stream stream, int chunkSize = 4096, long limitBytes = 0, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var buffer = new byte[chunkSize];
            int br;
            long brr = 0;
            while (true)
            {

                try
                {
                    br = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                    if (br < 1)
                        break;
                }
                catch (TaskCanceledException)
                {

                    break;
                }

                brr += br;
                if (limitBytes > 0 && brr > limitBytes)
                    break;

                var ret = new byte[br];
                Array.Copy(buffer, ret, br);
                yield return ret;
            }
        }

        public static async IAsyncEnumerable<api.proto.Payload> AsHtkTensorStreamSource(this Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var buffer = new byte[12];
            bool header = false;
            int br = 0;
            while (true)
            {

                try
                {
                    br = await stream.ReadAsync(buffer, br, buffer.Length - br, cancellationToken);
                    if (br < 1)
                        break;
                }
                catch (TaskCanceledException)
                {

                    break;
                }
                if (br == buffer.Length)
                {
                    if (!header)
                    {

                        var noFrames = BitConverter.ToUInt32(buffer, 0);
                        var frameRate = BitConverter.ToUInt32(buffer, 4);
                        var frameSizeBytes = BitConverter.ToUInt16(buffer, 8);
                        var nine = BitConverter.ToUInt16(buffer, 10);
                        buffer = new byte[frameSizeBytes];
                        header = true;
                    }
                    else
                    {
                        yield return new proto.Payload
                        {
                            Chunk = { new proto.Item { Type = "t", Key = "ten", T = new proto.Tensor { Data = Google.Protobuf.ByteString.CopyFrom(buffer, 0, buffer.Length), Dtype = "f4" } } }
                        };
                    }
                    br = 0;
                }
            }
        }


        public static async IAsyncEnumerable<api.proto.Payload> AsRawStreamSource(this Stream stream, int chunkSize = 4096, long limitBytes = 0, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var s in stream.AsBinaryChunkSource(chunkSize, limitBytes, cancellationToken).ToStreamOfBytes())
            {
                yield return s;
            }
        }

        public static async IAsyncEnumerable<api.proto.Payload> AsRawAudioSourceWithVAD(this Stream stream, int chunkSize = 4096, long limitBytes = 0, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {

            yield return new proto.Payload()
            {
                Chunk = { new proto.Item { Key = "ts", Type = "d", D = 0.0 }, new proto.Item { Key = "txt", Type = "s", S = "speech" }, new proto.Item { Key = "ts", Type = "d", D = double.MaxValue } },
                Track = "vad"
            };

            await foreach (var s in stream.AsBinaryChunkSource(chunkSize, limitBytes, cancellationToken).ToStreamOfBytes())
            {
                s.Track = "aud";
                yield return s;
            }
        }


        public static async IAsyncEnumerable<api.proto.Payload> AsRawAudioSource(this Stream stream, int chunkSize = 4096, long limitBytes = 0, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var s in stream.AsBinaryChunkSource(chunkSize, limitBytes, cancellationToken).ToStreamOfBytes())
            {
                s.Track = "aud";
                yield return s;
            }
        }


        public static async IAsyncEnumerable<T> AsProtoSource<T>(this Grpc.Core.IAsyncStreamReader<T> requestStream, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (true)
            {
                try
                {
                    if (!await requestStream.MoveNext(cancellationToken))
                    {
                        break;
                    }
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    //_logger.LogWarning(ex.ToString());
                    throw;
                }
                yield return requestStream.Current;
            }
        }




        public static async IAsyncEnumerable<T> AsProtoJsonSource<T>(this Stream stream, [EnumeratorCancellation] CancellationToken token) where T : Google.Protobuf.IMessage, new()
        {
            using StreamReader _stream = new(stream, new UTF8Encoding());
            string line;
            while (true)
            {
                try
                {
                    line = await _stream.ReadLineAsync();
                    if (line == null)
                        break;
                    if (line.Trim().Length == 0)
                        continue;
                }
                catch (TaskCanceledException) { break; }
                T ret = default(T);
                try
                {
                    ret = Google.Protobuf.JsonParser.Default.Parse<T>(line);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex.ToString());
                    break;
                }
                yield return ret;
            }

        }
        public static async IAsyncEnumerable<T> AsProtoSource<T>(this List<T> bc)
        {
            foreach (var x in bc)
            {
                yield return await Task.FromResult(x);
            }
        }

        public static async IAsyncEnumerable<T> AsProtoBinarySource<T>(this Stream stream, [EnumeratorCancellation] CancellationToken token) where T : Google.Protobuf.IMessage, new()
        {
            var buffer = new byte[4];
            int br, size;
            byte[] data;
            while (true)
            {
                try
                {

                    br = await stream.ReadAsync(buffer.AsMemory(0, 4), token);
                    if (br < 4)
                        break;
                    size = BitConverter.ToInt32(buffer, 0);
                    data = new byte[size];
                    br = await stream.ReadAsync(data.AsMemory(0, size), token);
                }
                catch (TaskCanceledException) { break; }
                if (br < size)
                    throw new Exception("Invalid stream");
                var t = new T();
                t.MergeFrom(new Google.Protobuf.CodedInputStream(data));
                yield return t;

            }
        }


        public static async IAsyncEnumerable<Tuple<TarEntry,TarEntry>> ZipWith(this IAsyncEnumerable<TarEntry> first, IAsyncEnumerable<TarEntry> second)
        {
            Dictionary<string,TarEntry> cache = new Dictionary<string,TarEntry>();

            var se = second.GetAsyncEnumerator();
            await foreach(var f in first)
            {
                var id = Path.ChangeExtension(f.Name, "");
                if (cache.ContainsKey(id))
                {
                    yield return Tuple.Create(f,cache[id]);
                    cache.Remove(id);
                    continue;
                }
                bool found = false;
                while (await se.MoveNextAsync())
                {
                    var sid = Path.ChangeExtension(se.Current.Name, "");
                    if (sid == id)
                    {
                        yield return Tuple.Create(f, se.Current);
                        found = true;
                        break;
                    }
                    else
                    {
                        cache[sid] = se.Current;
                    }
                }
                if (!found)
                {
                    throw new Exception($"Can't create tuple for {id}");
                }
            }
        }

        
        public static async IAsyncEnumerable<TarEntry> AsTarSource(this Stream stream, [EnumeratorCancellation] CancellationToken token)
        {
            using (var tarReader = new TarReader(stream))
            {
                TarEntry entry;
                while ((entry = tarReader.GetNextEntry()) != null)
                {
                    if (entry.EntryType is not TarEntryType.RegularFile)
                        continue;

                    var ret = new UstarTarEntry(TarEntryType.RegularFile, entry.Name);
                    ret.DataStream = new MemoryStream();
                    await entry.DataStream.CopyToAsync(ret.DataStream);
                    ret.DataStream.Seek(0, SeekOrigin.Begin);
                    yield return ret;
                }
            }
        }

        internal static Dictionary<string, string> GetSpeakerMap(XElement speakers)
        {
            var ret = new Dictionary<string, string>();
            var reverse = new Dictionary<string, string>();
            foreach (var spk in speakers.XPathSelectElements("//s"))
            {
                var val = spk.Attribute("id").Value.Trim();
                var name = (spk.Attribute("firstname").Value.Trim() + " " + spk.Attribute("surname").Value.Trim()).Trim();
                if(reverse.ContainsKey(name) && reverse[name]!=val)
                {
                    name += " "+val;
                }
                name = name.Trim();
                ret[spk.Attribute("id").Value] = name.Trim();
                reverse[name] = val;
            }
            return ret;
        }
        public static async IAsyncEnumerable<api.proto.Payload> AsTrsxTranSource(this Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using (var reader = new StreamReader(stream))
            {
                XDocument doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
                var paragraphs = doc.XPathSelectElements("//pa");
                var speakers = GetSpeakerMap(doc.XPathSelectElement("//sp"));
                var lastSpkId = "";
                foreach (var par in paragraphs)
                {
                    var bstart = XmlConvert.ToTimeSpan(par.Attribute("b").Value).TotalMilliseconds;
                    var bend = XmlConvert.ToTimeSpan(par.Attribute("e").Value).TotalMilliseconds;
                    if (bstart == bend)
                    {
                        continue;
                    }
                    var spkId = par.Attribute("s")?.Value == null ? "nobody": speakers[par.Attribute("s").Value];
                    
                    var ret = new proto.Payload() {Track="tran", Chunk = { new proto.Item {Key="ts", D= bstart }, new proto.Item { Key="spk", S= spkId} } };
                    lastSpkId = spkId;
                    var lastTs = bstart;
                    foreach (var p in par.XPathSelectElements("./p"))
                    {
                        var hasCaption = p.Attribute("caption") != null;
                        if (hasCaption)
                            continue;

                        var b = p.Attribute("b")?.Value;
                        var e = p.Attribute("e")?.Value;
                        var w = p.Value;
                        if(b!= null)
                        {
                            var bp = XmlConvert.ToTimeSpan(b).TotalMilliseconds;
                            if(bp > lastTs)
                            {
                                ret.Chunk.Add(new proto.Item { Key = "ts", D = bp });
                                lastTs = bp;
                            }
                        }
                        ret.Chunk.Add(new proto.Item { Key = "txt", S = w });
                        if (e != null)
                        {
                            var ep = XmlConvert.ToTimeSpan(e).TotalMilliseconds;
                            if (ep > lastTs)
                            {
                                ret.Chunk.Add(new proto.Item { Key = "ts", D = ep });
                                lastTs = ep;
                            }
                        }
                    }
                    if (bend > lastTs)
                    {
                        ret.Chunk.Add(new proto.Item { Key = "ts", D = lastTs });
                    }
                    yield return ret;
                }
                
            }
        }

    }
}
