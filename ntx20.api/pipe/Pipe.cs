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
    public static partial class Pipe
    {
        internal static Regex firstalpha = new Regex(@"^(\s*)(\S)(.*)$");

        private static readonly ILogger _logger = Logging.LoggerFactory.CreateLogger("ntx20.api.pipe");

        private const double SpeakerWordMinOverlapRatio = 0.60;
        private const double SpeakerSegmentMinSpeechCoverage = 0.50;
        private const double SpeakerSegmentMinSpeechOverlapMs = 500.0;
        private const int SpeakerSegmentMinWords = 2;
        private const double SpeakerPastMinDurationMs = 1000.0;
        private const double SpeakerPastMinSpeechCoverage = 0.20;
        private const double SpeakerShortSegmentMaxDurationMs = 1500.0;
        private const int SpeakerShortSegmentMaxWords = 2;
        private const double SpeakerSegmentStrongSpeechCoverage = 0.70;
        private const double SpeakerSegmentStrongSpeechOverlapMs = 1500.0;
        private const int SpeakerSegmentStrongWords = 4;

        private sealed class SpeakerEvent
        {
            public double Time { get; init; }
            public string Speaker { get; init; }
        }

        private sealed class DiarSegment
        {
            public double Start { get; init; }
            public double End { get; init; }
            public string Speaker { get; init; }
            public double SpeechOverlap { get; set; }
            public double SpeechCoverage { get; set; }
            public int SpeechWordCount { get; set; }
            public double Duration => End - Start;
            public bool IsSpeechCovered => SpeechCoverage >= SpeakerSegmentMinSpeechCoverage
                && (SpeechOverlap >= SpeakerSegmentMinSpeechOverlapMs || SpeechWordCount >= SpeakerSegmentMinWords);
            public bool IsStrongSpeechCovered => SpeechCoverage >= SpeakerSegmentStrongSpeechCoverage
                && (SpeechOverlap >= SpeakerSegmentStrongSpeechOverlapMs || SpeechWordCount >= SpeakerSegmentStrongWords);
            public bool NeedsPastSupport => !IsStrongSpeechCovered
                && (Duration <= SpeakerShortSegmentMaxDurationMs || SpeechWordCount <= SpeakerShortSegmentMaxWords);
        }

        private sealed class SpeakerSupport
        {
            public double Duration { get; set; }
            public double SpeechOverlap { get; set; }
            public double SpeechCoverage => Duration > 0 ? SpeechOverlap / Duration : 0.0;
            public bool HasEnoughHistory => Duration >= SpeakerPastMinDurationMs;
            public bool IsSpeechSupported => SpeechCoverage >= SpeakerPastMinSpeechCoverage;
        }

        private sealed class TranscriptWord
        {
            public int StartTsIndex { get; init; }
            public double Start { get; init; }
            public double End { get; init; }
            public bool HasText { get; init; }
            public string Speaker { get; set; }
            public string BestSpeaker { get; set; }
            public double BestOverlapRatio { get; set; }
            public double Duration => End - Start;
        }
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
        public static async IAsyncEnumerable<proto.Payload> SelectChunkItem(this IAsyncEnumerable<proto.Payload> source, Func<Item, Item> select)
        {
            await foreach (var x in source)
            {
                var ret = new proto.Payload() { Track = x.Track };

                foreach (var v in x.Chunk)
                {
                    ret.Chunk.Add(select(v));
                }

                yield return ret;
            }
        }

        public static async IAsyncEnumerable<proto.Payload> WhereChunkItem(this IAsyncEnumerable<proto.Payload> source, Func<Item,bool> predicate)
        {
            await foreach (var x in source)
            {
                var ret = new proto.Payload() { Track = x.Track };

                foreach (var v in x.Chunk)
                {
                    if (!predicate(v))
                        continue;
                    ret.Chunk.Add(v);
                }

                yield return ret;
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
            Func<X, Task<Y>> mapper,int parallelism, CmdRetryWithBackoff retry = null, [EnumeratorCancellation]  CancellationToken breaker = default)
        {

            if (retry == null)
                retry = new CmdRetryWithBackoff(0);

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
            ).ContinueWith(x => 
                    {
                        output.CompleteAdding();
                        if (!x.IsCompletedSuccessfully) {
                            throw x.Exception;
                        }
                        
                
                    }).ConfigureAwait(false);
            
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
        private static IEnumerable<Tuple<double, double, string>> ToWordBlocks(this IEnumerable<api.proto.Item> items)
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

        private static async IAsyncEnumerable<KeyValuePair<string,List<proto.Item>>> ToSpkBlocks(this IAsyncEnumerable<proto.Item> source)
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

            
            await foreach (var x in source.Where(x=> x.Track == "tran").SelectMany(x=>x.Chunk.ToAsyncEnumerable()).Where(x=> !x.Tags.Contains("la")).ToSpkBlocks())
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
                
                foreach ( var word in x.Value.ToWordBlocks())
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

        private static List<TranscriptWord> BuildTranscriptWords(List<proto.Item> items)
        {
            var words = new List<TranscriptWord>();
            int startTsIndex = -1;
            double startTs = 0.0;
            bool hasWordText = false;

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Tags.Contains("la"))
                {
                    continue;
                }

                if (item.Key == "ts")
                {
                    if (startTsIndex >= 0)
                    {
                        words.Add(new TranscriptWord
                        {
                            StartTsIndex = startTsIndex,
                            Start = startTs,
                            End = item.D,
                            HasText = hasWordText,
                        });
                    }

                    startTsIndex = i;
                    startTs = item.D;
                    hasWordText = false;
                    continue;
                }

                if (startTsIndex >= 0
                    && item.Key == "txt"
                    && !item.Tags.Contains("+")
                    && !item.Tags.Contains("noise")
                    && item.S.Trim().Length > 0)
                {
                    hasWordText = true;
                }
            }

            return words;
        }

        private static double Overlap(double startA, double endA, double startB, double endB)
        {
            return Math.Max(0.0, Math.Min(endA, endB) - Math.Max(startA, startB));
        }

        private static List<DiarSegment> BuildDiarSegments(List<SpeakerEvent> speakerEvents, double transcriptStart, double transcriptEnd)
        {
            var events = speakerEvents.OrderBy(x => x.Time).ToList();
            var segments = new List<DiarSegment>();

            if (events.Count == 0)
            {
                segments.Add(new DiarSegment
                {
                    Start = transcriptStart,
                    End = transcriptEnd,
                    Speaker = "nobody",
                });
                return segments;
            }

            for (int i = 0; i < events.Count; i++)
            {
                var start = events[i].Time;
                var end = i + 1 < events.Count ? events[i + 1].Time : transcriptEnd;
                if (end <= start)
                {
                    continue;
                }

                segments.Add(new DiarSegment
                {
                    Start = start,
                    End = end,
                    Speaker = events[i].Speaker,
                });
            }

            return segments;
        }

        private static void MarkSpeechCoveredDiarSegments(List<DiarSegment> diarSegments, List<TranscriptWord> words)
        {
            foreach (var segment in diarSegments)
            {
                segment.SpeechOverlap = words
                    .Where(x => x.HasText)
                    .Sum(x => Overlap(segment.Start, segment.End, x.Start, x.End));
                segment.SpeechWordCount = words
                    .Where(x => x.HasText)
                    .Count(x => Overlap(segment.Start, segment.End, x.Start, x.End) > 0.0);

                segment.SpeechCoverage = segment.Duration > 0
                    ? segment.SpeechOverlap / segment.Duration
                    : 0.0;
            }
        }

        private static void AddSpeakerSupport(Dictionary<string, SpeakerSupport> supportBySpeaker, DiarSegment segment)
        {
            if (!supportBySpeaker.TryGetValue(segment.Speaker, out var support))
            {
                support = new SpeakerSupport();
                supportBySpeaker[segment.Speaker] = support;
            }

            support.Duration += segment.Duration;
            support.SpeechOverlap += segment.SpeechOverlap;
        }

        private static bool IsCausallySupported(DiarSegment segment, Dictionary<string, SpeakerSupport> pastSupportBySpeaker)
        {
            if (!segment.IsSpeechCovered)
            {
                return false;
            }

            if (!segment.NeedsPastSupport)
            {
                return true;
            }

            if (!pastSupportBySpeaker.TryGetValue(segment.Speaker, out var support))
            {
                return false;
            }

            return support.HasEnoughHistory && support.IsSpeechSupported;
        }

        private static string AssignSpeakerByOverlap(TranscriptWord word, List<DiarSegment> diarSegments)
        {
            DiarSegment bestSegment = null;
            double bestOverlap = 0.0;

            foreach (var segment in diarSegments.Where(x => x.IsSpeechCovered))
            {
                var overlap = Overlap(word.Start, word.End, segment.Start, segment.End);
                if (overlap > bestOverlap)
                {
                    bestOverlap = overlap;
                    bestSegment = segment;
                }
            }

            word.BestSpeaker = bestSegment?.Speaker;
            word.BestOverlapRatio = word.Duration > 0 ? bestOverlap / word.Duration : 0.0;

            if (word.BestOverlapRatio >= SpeakerWordMinOverlapRatio)
            {
                return word.BestSpeaker;
            }

            return null;
        }

        private static string FallbackSpeaker(TranscriptWord word, string previousSpeaker)
        {
            return previousSpeaker
                ?? word.BestSpeaker
                ?? "nobody";
        }

        private static void AssignSpeakersToWords(List<TranscriptWord> words, List<DiarSegment> diarSegments)
        {
            MarkSpeechCoveredDiarSegments(diarSegments, words);
            var orderedDiarSegments = diarSegments
                .OrderBy(x => x.Start)
                .ToList();
            var pastSupportBySpeaker = new Dictionary<string, SpeakerSupport>();
            var nextSupportSegmentIndex = 0;
            string previousSpeaker = null;

            // Speaker changes are derived from word-to-diar interval overlap.
            // Very short weak segments can only continue a speaker that has
            // previous word support. They do not blacklist the speaker: a longer
            // or strongly covered segment can still introduce that speaker later.
            // Boundary words need a clear overlap winner before they can introduce
            // a new speaker.
            for (int i = 0; i < words.Count; i++)
            {
                var word = words[i];
                while (nextSupportSegmentIndex < orderedDiarSegments.Count
                    && orderedDiarSegments[nextSupportSegmentIndex].End <= word.Start)
                {
                    AddSpeakerSupport(pastSupportBySpeaker, orderedDiarSegments[nextSupportSegmentIndex]);
                    nextSupportSegmentIndex++;
                }

                if (!word.HasText)
                {
                    continue;
                }

                var validDiarSegments = orderedDiarSegments
                    .Where(x => IsCausallySupported(x, pastSupportBySpeaker))
                    .ToList();

                word.Speaker = AssignSpeakerByOverlap(word, validDiarSegments);
                if (word.Speaker == null)
                {
                    word.Speaker = FallbackSpeaker(word, previousSpeaker);
                }

                if (word.Speaker != null)
                {
                    previousSpeaker = word.Speaker;
                }
            }
        }

        private static proto.Payload CreateTranPayloadByWordSpeakers(List<proto.Item> tpcItems, List<TranscriptWord> words)
        {
            Regex lastPunct = new Regex(@"(,|([^.!?]))\s*$");
            var speakerByTsIndex = words
                .Where(x => x.HasText && x.Speaker != null)
                .ToDictionary(x => x.StartTsIndex, x => x.Speaker);
            var ret = new proto.Payload { Track = "tran" };

            string currentSpeaker = null;
            proto.Item lastOne = null;
            bool needSos = true;

            for (int i = 0; i < tpcItems.Count; i++)
            {
                var item = tpcItems[i];
                ret.Chunk.Add(item);

                if (item.Key == "ts"
                    && !item.Tags.Contains("la")
                    && speakerByTsIndex.TryGetValue(i, out var speaker)
                    && speaker != currentSpeaker)
                {
                    if (currentSpeaker != null && lastOne != null)
                    {
                        lastOne.S = lastPunct.Replace(lastOne.S, m =>
                          m.Groups[2].Value + "."
                          );
                    }

                    ret.Chunk.Add(new Item { Key = "spk", S = speaker });
                    currentSpeaker = speaker;
                    needSos = true;
                    continue;
                }

                if (item.Key == "txt"
                    && !item.Tags.Contains("la")
                    && !item.Tags.Contains("noise")
                    && item.S.Trim().Length > 0)
                {
                    if (needSos)
                    {
                        if (!item.Tags.Contains("sos"))
                            item.Tags.Add("sos");
                        needSos = false;
                    }
                    lastOne = item;
                }
            }

            return ret;
        }

        internal static async IAsyncEnumerable<proto.Payload> createTranTrack(this IAsyncEnumerable<proto.Payload> source, bool pipe)
        {
            var passthrough = new List<proto.Payload>();
            var tpcItems = new List<proto.Item>();
            var speakerEvents = new List<SpeakerEvent>();
            string cSpeaker = null;
            double spkHead = 0.0;

            await foreach(var x in source)
            {
                if(pipe && x.Track!="tran")
                {
                    passthrough.Add(x);
                }

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
                                    speakerEvents.Add(new SpeakerEvent { Time = spkHead, Speaker = x2.S });
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
                    tpcItems.AddRange(x.Chunk);
                }
            }

            foreach (var payload in passthrough)
            {
                yield return payload;
            }

            if (tpcItems.Count == 0)
            {
                yield break;
            }

            var words = BuildTranscriptWords(tpcItems);
            var transcriptStart = words.FirstOrDefault()?.Start ?? 0.0;
            var transcriptEnd = words.LastOrDefault()?.End ?? transcriptStart;
            var diarSegments = BuildDiarSegments(speakerEvents, transcriptStart, transcriptEnd);
            AssignSpeakersToWords(words, diarSegments);

            yield return CreateTranPayloadByWordSpeakers(tpcItems, words);
        }

        internal static void Add(this proto.Evaluation e, proto.Evaluation a)
        {
            e.Items.Add(a);
            if (a.Dscore != null)
            {
                e.Dscore.ClusterCount += a.Dscore.ClusterCount;
                e.Dscore.ClusterMatch += a.Dscore.ClusterMatch;
                e.Dscore.Count+= a.Dscore.Count;
                e.Dscore.Deletions += a.Dscore.Deletions;
                e.Dscore.Insertions+= a.Dscore.Insertions;
                e.Dscore.Hits += a.Dscore.Hits;
                //e.Dscore.GetResults();
            }
            if (a.Wscore != null)
            {
                e.Wscore.Count += a.Wscore.Count;
                e.Wscore.Deletions += a.Wscore.Deletions;
                e.Wscore.Insertions += a.Wscore.Insertions;
                e.Wscore.Hits += a.Wscore.Hits;
                e.Wscore.Substitutions += a.Wscore.Substitutions;
                //e.Wscore.GetResults();
            }
                
        }
        public static async Task<proto.Evaluation> Evaluate(this IAsyncEnumerable<proto.Evaluation> source, bool keepBlocks = false)
        {
            var ret = new Evaluation { Dscore = new DiarScore { }, Wscore = new WordScore { } };
            await foreach (var item in source)
            {
                if (!keepBlocks)
                {
                    item.Blocks.Clear();
                    if(item.Dscore!= null) 
                        item.Dscore.SpeakerMap.Clear();
                }

                ret.Add(item);
            }
            if (ret.Dscore != null)
                ret.Dscore.GetResults();
            if (ret.Wscore != null)
                ret.Wscore.GetResults();
            return ret;
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


