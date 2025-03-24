using Google.Protobuf;
using Microsoft.Extensions.Logging;

using ntx20.api.proto;
using ntx20.api.utils;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Xml.Linq;
using static Grpc.Core.Metadata;


namespace ntx20.api.pipe
{
    public static class Pipe
    {
        internal static Regex firstalpha = new Regex(@"^(\s*)(\S)(.*)$");

        private static readonly ILogger _logger = Logging.LoggerFactory.CreateLogger("ntx20.api.pipe");
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

        public static async IAsyncEnumerable<T> AsSource<T>(this BufferBlock<T> buffer, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (await buffer.OutputAvailableAsync())
            {
                yield return await buffer.ReceiveAsync();
            }
        }

        public static async Task RunWithSink<T>(this IAsyncEnumerable<T> source, IAsyncSink<T> sink, bool autoFlush = true, bool autoComplete = true, CancellationToken cancellationToken = default)
        {
            await foreach(T v in source.WithCancellation(cancellationToken))
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                await sink.WriteAsync(v, cancellationToken);
                if(autoFlush)
                    await sink.FlushAsync(cancellationToken);
            }
            if (autoComplete)
            {
                await sink.FlushAsync(cancellationToken);
                await sink.CompleteAsync(cancellationToken);
            }
        }

        public static async IAsyncEnumerable<Y> ViaMapper<X,Y>(this IAsyncEnumerable<X> source, Func<X,Y> mapper)
        {
            await foreach (X x in source)
            {
                yield return mapper(x);
            }
        }

        public static async IAsyncEnumerable<Z> ViaOneToMany<X, Z>(this IAsyncEnumerable<X> source, Func<X, IEnumerable<Z>> mapper)
        {
            await foreach (X x in source)
            {
                foreach (var y in mapper(x))
                {
                    yield return y;
                }
                
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
                    if (c.B.Length > 0)
                    {
                        yield return new api.proto.Tensor { Data = Google.Protobuf.ByteString.CopyFrom(c.B.ToByteArray()) };

                    }


                    if(c.T != null)
                        yield return c.T.Clone();

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

        public static async IAsyncEnumerable<string> ToText(this IAsyncEnumerable<proto.Payload> source, string track)
        {
            bool fnoise = true;
            await foreach (var x in source)
            {
                if (x.Track != track)
                    continue;
                foreach (var v in x.Chunk)
                {
                    if (v.Key != "txt")
                        continue;
                    if (v.Tags.Contains("la"))
                        continue;

                    var s = v.S;
                    if (v.Tags.Contains("sos"))
                    {

                        s = firstalpha.Replace(s, m =>
                        m.Groups[1].Value + m.Groups[2].Value.ToUpperInvariant() + m.Groups[3].Value
                        );

                    }
                    if (v.Tags.Contains("noise")) {
                        if (fnoise)
                        {
                            s = " *";
                            fnoise = false;
                        }
                        else
                        {
                            s = "*";
                        }
                    }
                    else
                    {
                        fnoise = true;
                    }


                    yield return s;


                }
            }
        }

        public static async IAsyncEnumerable<string> ToNText(this IAsyncEnumerable<proto.Payload> source, string track)
        {
            Regex firstalpha = new Regex(@"^(\s*)(\S)(.*)$");
            await foreach (var x in source)
            {
                if (x.Track != track)
                    continue;
                foreach (var v in x.Chunk)
                {
                    if (v.Key != "txt")
                        continue;
                    if (v.Tags.Contains("la"))
                        continue;

                    var s = v.S;
                    if (v.Tags.Contains("sos"))
                    {

                        s = firstalpha.Replace(s, m =>
                        m.Groups[1].Value + m.Groups[2].Value.ToUpperInvariant() + m.Groups[3].Value
                        );

                    }


                    yield return s;


                }
            }
        }


        public static async IAsyncEnumerable<string> ToSimpleText(this IAsyncEnumerable<proto.Payload> source)
        {
            await foreach (var x in source)
            {
                var ss = new List<string>{ x.Track};
                foreach(var v  in x.Chunk)
                {

                    var value = v.Type switch
                    {
                        "s" => v.S,
                        "t" => v.T.ToString(),
                        "d" => v.D.ToString(),
                        "f" => v.F.ToString(),
                        "i" => v.I.ToString(),
                        _ => "unk",
                    };
                    var labels = string.Join(' ',v.Labels.Select(x => $"{x.Key}={x.Value}"));
                    ss.Add($"{v.Key}|{v.Type}|{value}|{string.Join(' ', v.Tags)}|{labels}");
                }
                yield return string.Join('|', ss) + System.Environment.NewLine;

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

        public static async IAsyncEnumerable<Y> ViaAsyncMapper<X, Y>(this IAsyncEnumerable<X> source, Func<X, Task<Y>> mapper)
        {
            await foreach (X x in source)
            {
                yield return await mapper(x);
            }
        }

        public static async IAsyncEnumerable<Y> ViaAsyncMapperParallel<X, Y>(this IAsyncEnumerable<X> source, 
            Func<X, Task<Y>> mapper,int parallelism, util.RetryWithBackoff retry, [EnumeratorCancellation]  CancellationToken breaker = default)
        {

            using BlockingCollection<Y> output = new BlockingCollection<Y>(2 * (int)parallelism);
            var feed = Parallel.ForEachAsync(source,
                new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = breaker },

                async (x, breaker) =>
                {
                    while (true)
                    {
                        var _retry = retry.Clone();
                        try
                        {
                            output.Add(await mapper(x));
                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Failed: {ex.Message}");
                            if (breaker.IsCancellationRequested)
                                throw;
                            if (await _retry.Next(breaker))
                                throw;
                            _logger.LogWarning($"Retry: #{_retry.Retry}/{_retry.MaxRetries}");
                        }
                    }
                }
            ).ContinueWith(x => output.CompleteAdding());
            
            foreach (var x in output.GetConsumingEnumerable())
            {
                yield return x;
            }

            await feed;
            
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

        
        public static async IAsyncEnumerable<proto.Payload> ToRawHtk(this IAsyncEnumerable<proto.Payload> source, proto.Payload start, bool seek)
        {
            uint noFrames = 0;
            uint framePeriod = (uint)start.GetSingleParamOrThrow("framePeriod").I;
            ushort frameSizeBytes = (ushort)start.GetSingleParamOrThrow("frameSize").I;
            ushort nine = 9;
        
            List<byte> header = new();
            header.AddRange(BitConverter.GetBytes(noFrames));
            header.AddRange(BitConverter.GetBytes(framePeriod));
            header.AddRange(BitConverter.GetBytes(frameSizeBytes));
            header.AddRange(BitConverter.GetBytes(nine));


            long totalBytes = 0;
            var first = new proto.Payload();
            first.Chunk.Add(new Item {Key="header:htk", Type = "b",  B = Google.Protobuf.ByteString.CopyFrom(header.ToArray(), 0, header.Count) });
            yield return first;
            await foreach (var x in source)
            {
                if(x.Track != "adsp")
                {
                    continue;
                }
                foreach(var c in x.Chunk)
                {
                    if (c.T != null)
                    {
                        totalBytes += c.T.Data.Length;
                    }
                }
                yield return x;
            }

            if (seek)
            {
                header.Clear();
                noFrames = (uint)(totalBytes / frameSizeBytes);
                header.AddRange(BitConverter.GetBytes(noFrames));
                header.AddRange(BitConverter.GetBytes(framePeriod));
                header.AddRange(BitConverter.GetBytes(frameSizeBytes));
                header.AddRange(BitConverter.GetBytes(nine));

                var last = new proto.Payload();
                last.Chunk.Add(new Item { Key = "seek:begin", Type = "i", I = 0 });
                last.Chunk.Add(new Item { Key = "header:htk", Type = "b", B = ByteString.CopyFrom(header.ToArray(), 0, header.Count) });

                yield return last;
            }

        }

        public static async Task<proto.Payload> Configure(this Grpc.Core.AsyncDuplexStreamingCall<proto.Payload, proto.Payload> call, proto.Payload config, CancellationToken cancellationToken)
        {
            
            await call.RequestStream.WriteAsync(config);
            var meta = await call.ResponseHeadersAsync;
            foreach (var item in meta)
            {
                if (!item.IsBinary)
                {
                    _logger.LogTrace($"{item.Key}={item.Value}");
                }
            }
            await call.ResponseStream.MoveNext(cancellationToken);
            return call.ResponseStream.Current;
        }
        public static async IAsyncEnumerable<proto.Payload> ViaClientMetaInjector(this IAsyncEnumerable<proto.Payload> source, Dictionary<string,string> labels)
        {
            var meta = new api.proto.Item { Key = "meta"};
            meta.Labels.Add(labels);
            
            await foreach (var i in source)
            {
                i.Chunk.Add(meta.Clone());
                yield return i;
            }
        }
        public static async IAsyncEnumerable<proto.Payload> ViaOCWrapper(this IAsyncEnumerable<proto.Payload> source)
        {
            bool first = true;
            await foreach (var i in source)
            {
                if (first)
                {
                    yield return new Payload { Track = "open" };
                    first = false;
                }
                yield return i;
            }

            if (!first)
            {
                yield return new Payload { Track = "close" };
            }

        }

        public static async IAsyncEnumerable<proto.Payload> MergeByTsWith(this IAsyncEnumerable<proto.Payload> first, IAsyncEnumerable<proto.Payload> second)
        {
            var ctime = 0.0;
            bool fc = true;
            bool sc = true;
            var f = first.GetAsyncEnumerator();
            var s = second.GetAsyncEnumerator();
            while (fc || sc)
            {
                while(fc =await f.MoveNextAsync()) {
                    yield return f.Current;
                    var l = f.Current.Chunk.LastOrDefault(x => x.Key == "ts" && x.D > ctime);
                    if (l!= null)
                    {
                        ctime = l.D;
                        break;
                    }
                }
                while(sc = await s.MoveNextAsync()) {
                    yield return s.Current;
                    var l = s.Current.Chunk.LastOrDefault(x => x.Key == "ts" && x.D > ctime);
                    if (l != null)
                    {
                        ctime = l.D;
                        break;
                    }
                }
            }
        }
        internal static IEnumerable<Tuple<double, double, string>> ToTrsxWords(this IEnumerable<api.proto.Item> items)
        {
            var bStart = -1.0;
            var bEnd = -1.0;
            string cWord = null;
            foreach (var item in items)
            {
                if (item.Key == "ts")
                {
                    if (cWord == null)
                    {
                        bStart = item.D;
                        continue;
                    }
                    if (cWord.Trim().Length == 0)
                    {
                        bStart = item.D;
                        continue;
                    }
                    bEnd = item.D;
                    yield return Tuple.Create(bStart, bEnd, cWord);
                    cWord = null;
                    bStart = item.D;

                    continue;
                }
                if (item.Key == "txt")
                {
                    var s = item.S;
                    if (item.Tags.Contains("noise"))
                    {
                        s = "";
                    }

                    if (cWord == null)
                    {
                        cWord = s;
                    }
                    else
                    {
                        cWord += s;
                    }
                }
            }

        }

        internal static async IAsyncEnumerable<KeyValuePair<string,List<proto.Item>>> ToTrsxBlocks(this IAsyncEnumerable<proto.Item> source)
        {
            string cSpeaker = null;
            double lastTs = 0.0;
            var cBlock = new List<proto.Item>();
            await foreach (var x in source)
            {
                if (x.Tags.Contains("la"))
                {
                    continue;
                }
                if (x.Key == "spk")
                {
                    if(cSpeaker == null)
                    {
                        cSpeaker = x.S;
                    }
                    else
                    {
                        yield return new KeyValuePair<string, List<Item>>(cSpeaker,cBlock) ;
                        cBlock = new List<Item>() { new Item { Key = "ts", D = lastTs }, x };
                        cSpeaker = x.S;
                    }
                    continue;
                }
                if(x.Key == "ts")
                {
                    lastTs = x.D;
                }
                cBlock.Add(x);
            }
            if (cSpeaker == null)
            {
                cSpeaker = "nobody";
            }
            if (cBlock.Count > 0)
            {
                yield return new KeyValuePair<string, List<Item>>(cSpeaker, cBlock);
            }

        }

        public static async IAsyncEnumerable<string> ToTrsx(this IAsyncEnumerable<proto.Payload> source, string mediaUri)
        {
            var root = new XElement("transcription",
                     new XAttribute("mediauri", mediaUri),
                     new XAttribute("version", "3.0")
                     );
            var doc = new XDocument(root) { Declaration = new XDeclaration("1.0", "UTF-8", "yes") };
            

            var speakers = new Dictionary<string, string>();
            var channel = new XElement("se", new XAttribute("name", "transcription"));
            root.Add(new XElement("ch", new XAttribute("name", mediaUri), channel));
            var speaker = new XElement("sp");
            root.Add(speaker);

            
            await foreach (var x in source.Where(x=> x.Track == "tran").SelectMany(x=>x.Chunk.ToAsyncEnumerable()).Where(x=> !x.Tags.Contains("la")).ToTrsxBlocks())
            {
                if (!speakers.ContainsKey(x.Key))
                {
                    speakers[x.Key] = speakers.Count.ToString();
                    speaker.Add(
                        new XElement("s",
                            new XAttribute("id", speakers[x.Key]),
                            new XAttribute("surname", x.Key),
                            new XAttribute("firstname", ""),
                            new XAttribute("lang", ""),
                            new XAttribute("sex", "")
                            )
                        );
                }
                var startTime = x.Value.First(x => x.Key == "ts").D;
                var stopTime = x.Value.Last(x => x.Key == "ts").D;
                List<XElement> words = new List<XElement>();
                
                foreach ( var word in x.Value.ToTrsxWords())
                {
                    words.Add(
                 new XElement("p",
                 new XAttribute("b", TimeSpan.FromMilliseconds(word.Item1)),
                 new XAttribute("e", TimeSpan.FromMilliseconds(word.Item2)),
                 word.Item3));
                }

                channel.Add(new XElement("pa",
                                               new XAttribute("a", ""),
                                               new XAttribute("b", TimeSpan.FromMilliseconds(startTime)),
                                               new XAttribute("e", TimeSpan.FromMilliseconds(stopTime)),
                                               new XAttribute("s", speakers[x.Key]),
                                               words

                                           ));


            }
            yield return doc.Declaration.ToString() + "\n";
            yield return root.ToString();
        }
        internal static async IAsyncEnumerable<proto.Payload> withPostprocessing(this IAsyncEnumerable<proto.Payload> source)
        {
            await foreach (var x in source)
            {

                if (x.Track != "tran")
                {
                    yield return x;
                    continue;
                }


                foreach (var item in x.Chunk)
                {
                    if (item.Key == "txt")
                    {
                        var s = item.S;
                        if (item.Tags.Contains("sos"))
                        {
                            s = firstalpha.Replace(s, m =>
                            m.Groups[1].Value + m.Groups[2].Value.ToUpperInvariant() + m.Groups[3].Value
                            );
                            item.S = s;
                        }
                        
                    }
                }
                yield return x;
            }
        }
        internal static IEnumerable<Tuple<double, bool>> ToTimetampsWithPunct(this IEnumerable<api.proto.Item> items)
        {
            Regex hasPunctRegex = new Regex(@"[\.,:!?]\s*$");
            bool hasPunct = false;
            foreach (var item in items.Where(x=>!x.Tags.Contains("la")))
            {
                if (item.Key == "txt")
                {
                    if (item.S.Trim().Length == 0)
                        continue;
                    hasPunct = hasPunctRegex.IsMatch(item.S);
                    continue;
                }
                if(item.Key == "ts")
                {
                    yield return Tuple.Create(item.D, hasPunct);
                }
            }
        }
        public static async IAsyncEnumerable<proto.Payload> CreateTranTrack(this IAsyncEnumerable<proto.Payload> source, bool pipe)
        {
            await foreach (var item in source.createTranTrack(pipe).withPostprocessing())
            {
                yield return item;
            }
        }
        internal static async IAsyncEnumerable<proto.Payload> createTranTrack(this IAsyncEnumerable<proto.Payload> source, bool pipe)
        {

            Regex lastPunct = new Regex(@"(,|([^.!?]))\s*$");
            
            string cSpeaker = null;
            double tpcHead = 0.0;
            double spkHead = 0.0;

            Queue <proto.Item> spk = new();
            Queue<proto.Item> tpc = new();
            bool needSos = true;
            await foreach(var x in source)
            {
                
                if(pipe && x.Track!="tran")
                    yield return x;
                if (x.Track == "spk")
                {
                    foreach(var x2 in x.Chunk)
                    {
                        if (x2.Tags.Contains("la"))
                            continue;
                        switch (x2.Key) {
                            case "ts":
                                spkHead = x2.D;
                                break;
                            case "txt":
                                if(cSpeaker != x2.S)
                                {
                                    spk.Enqueue(new Item { Key= "spk",S=x2.S, D= spkHead});
                                    cSpeaker = x2.S;
                                }
                                break;
                            default:
                                break;
                        }
                    }

                }
                if(x.Track == "tpc")
                {
                    foreach(var x2 in x.Chunk)
                    {
                        if (x2.Key == "ts" && !x2.Tags.Contains("la"))
                        {
                            tpcHead = x2.D;
                        }
                        tpc.Enqueue(x2);
                    }
                }
                

                while(spk.Count > 0 && spk.Peek().D < tpcHead)
                {
                    var cspk = spk.Dequeue();
                    var changePoints = tpc.ToTimetampsWithPunct().OrderBy(x => Math.Abs(cspk.D - x.Item1)).ToArray();
                    var chp = changePoints[0];
                    /*
                    if (!chp.Item2 && changePoints.Count() >0)
                    {
                        var second = changePoints[1];
                        if(Math.Abs(cspk.D - second.Item1) < 250 && second.Item2)
                        {
                            chp = second;
                        }
                        
                    }
                    */
                    var changePoint = chp.Item1;
                    var ret = new proto.Payload() { Track = "tran" };
                    proto.Item lastOne = null;
                    while (true)
                    {
                        var ctpc = tpc.Dequeue();
                        ret.Chunk.Add(ctpc);
                        if(ctpc.Key == "txt" && !ctpc.Tags.Contains("la") && !ctpc.Tags.Contains("noise"))
                        {
                            if (ctpc.S.Trim().Length > 0)
                            {
                                if (needSos)
                                {
                                    if (!ctpc.Tags.Contains("sos"))
                                        ctpc.Tags.Add("sos");
                                    needSos = false;
                                }
                                lastOne = ctpc;
                            }

                        }
                        if (ctpc.Key == "ts" && !ctpc.Tags.Contains("la") && ctpc.D == changePoint){

                            if (lastOne != null) {
                                lastOne.S = lastPunct.Replace(lastOne.S, m =>
                                  m.Groups[2].Value + "."
                                  );
                            }
                            

                            break;
                        }
                    }
                    ret.Chunk.Add(new Item { Key = "spk", S = cspk.S });
                    
                    needSos = true;

                    yield return ret;
                }
                //TODO add buffer dequeue when no speaker found
            }
            if (tpc.Count > 0)
            {
                var ret = new proto.Payload() { Track = "tran" };
                ret.Chunk.AddRange(tpc);
                yield return ret;
            }
        }


        public static async IAsyncEnumerable<proto.Payload> ViaGRPCCall(this IAsyncEnumerable<proto.Payload> source, Grpc.Core.AsyncDuplexStreamingCall<proto.Payload, proto.Payload> call)
        {
            using var upstream = source.RunWithSink(call.RequestStream.AsGrpcSink());
            
            await foreach (var i in call.ResponseStream.AsProtoSource())
            {
                yield return i;
            }
            await upstream;
        }

        public static async IAsyncEnumerable<proto.Payload> ViaTaskRunner(this IAsyncEnumerable<proto.Payload> source, Grpc.Core.AsyncDuplexStreamingCall<proto.Payload, proto.Payload> call,
            string[] accepts, bool pipeMode, int bufferSize = 10)
        {
            BufferBlock<proto.Payload> bb = bufferSize == 0 ? new BufferBlock<Payload>() :
                new BufferBlock<Payload>(new DataflowBlockOptions { BoundedCapacity = bufferSize });

            if (pipeMode)
            {
                source = source.InterceptAsync(async x => {
                    await bb.SendAsync(x.Clone());
                });
            }


            using var writer = Task.Run(async () =>
            {

                try
                {
                    await foreach (var v in source.Remove(x => !accepts.Contains(x.Track)).ViaGRPCCall(call))
                    {
                        await bb.SendAsync(v);
                    }
                }
                finally
                {
                    bb.Complete();
                }
            });


            while (await bb.OutputAvailableAsync())
            {
                yield return await bb.ReceiveAsync();
            }

            await writer;
        }
    }

}


