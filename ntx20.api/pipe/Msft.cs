using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using ntx20.api.proto;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace ntx20.api.pipe
{
    public static class Msft
    {
        private static async IAsyncEnumerable<proto.Payload> ViaFFmpeg(this IAsyncEnumerable<proto.Payload> source)
        {

            var p = new Process();
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.FileName = "ffmpeg";
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.Arguments = "-i pipe:0 -ac 1 -ar 16000 -f s16le pipe:1";
            p.StartInfo.RedirectStandardInput = true;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = false;
            p.Start();

            using var writer = Task.Run(async () =>
            {
                await foreach (var item in source)
                {
                    foreach(var b in item.Chunk)
                    {
                        await p.StandardInput.BaseStream.WriteAsync(b.B.ToByteArray());
                        
                    }
                }
            });

            await  foreach (var item in p.StandardOutput.BaseStream.AsRawAudioSource())
            {
                yield return item;
            }
            await writer;
        }

        internal class MSAudioStream : PullAudioInputStreamCallback
        {
            public MSAudioStream() { }

            public override int Read(byte[] buffer, uint size)
            {
                // Returns audio data to the caller.
                // E.g., return read(config.YYY, buffer, size);
                return 0;
            }

            public override void Close()
            {
                // Close and clean up resources.
            }
        }

        public static async IAsyncEnumerable<proto.Payload> ViaMSFTRt(this IAsyncEnumerable<proto.Payload> source, SpeechConfig speechConfig)
        {
            using var stream = AudioInputStream.CreatePushStream();
            using var audioConfig = AudioConfig.FromStreamInput(stream);
            var oBuffer = new BufferBlock<SpeechRecognitionResult>();
            speechConfig.RequestWordLevelTimestamps();
            speechConfig.OutputFormat = OutputFormat.Detailed;
            var speechRecognizer = new SpeechRecognizer(speechConfig, audioConfig);

            using var writer = Task.Run(async () =>
            {
                await foreach (var item in source)
                {
                    foreach (var b in item.Chunk)
                    {
                       stream.Write(b.B.ToByteArray());
                    }
                }
                stream.Close();
            });
            
            await speechRecognizer.StartContinuousRecognitionAsync();
            speechRecognizer.Recognized += (object sender, SpeechRecognitionEventArgs e) => {
                if (e.Result.Reason == ResultReason.RecognizedSpeech)
                {
                    oBuffer.Post(e.Result);
                }
            };
            speechRecognizer.SessionStopped += (object sender, SessionEventArgs e) => { 
                oBuffer.Complete(); 
            };
            var lastTs = -1.0;
            var lastWordTs = -1.0;
            await foreach (var v in oBuffer.AsSource())
            {
                var r = new proto.Payload { Track = "v2t" };
                try
                {
                    var json = v.Properties.GetProperty(PropertyId.SpeechServiceResponse_JsonResult);
                    JsonNode document = JsonNode.Parse(json);
                    foreach (var w in document["NBest"]!.AsArray()[0]["Words"].AsArray())
                    {
                        var start = w["Offset"]!.GetValue<long>() / 10000.0;
                        var stop = (w["Offset"]!.GetValue<long>() + w["Duration"]!.GetValue<long>()) / 10000.0;
                        if (lastWordTs != start)
                        {
                            r.Chunk.Add(new Item { Key = "ts", D = start });
                        }
                        r.Chunk.Add(new Item { Key = "txt", S = w["Word"].GetValue<string>() });
                        r.Chunk.Add(new Item { Key = "ts", D = stop });
                        lastWordTs = stop;
                    }

                    
                }catch
                {

                }
                yield return r;



                var ret = new proto.Payload { Track = "tpc" };
                var offsetMs = v.OffsetInTicks / 10000.0;
                
                if (lastTs != offsetMs) {
                    ret.Chunk.Add(new Item { Key = "ts", D = offsetMs });
                }
                ret.Chunk.Add(new Item { Key = "txt", S = v.Text });
                lastTs = offsetMs + v.Duration.TotalMilliseconds;
                ret.Chunk.Add(new Item { Key = "ts", D = lastTs });
                yield return ret;
            }

            await writer;
        }
        
    }
}
