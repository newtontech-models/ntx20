using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging;
using ntx20.api;
using ntx20.api.io;
using ntx20.api.pipe;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ntx20.api.utils;
using System.IO;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Components.Forms;
using System.Formats.Tar;
using static Grpc.Core.Metadata;

namespace ntx20.command.batch.run.atran
{

    class Command : ICommand
    {
        private static readonly ILogger _logger = Logging.LoggerFactory.CreateLogger("ntx20.command.batch.run.atran");

        
        internal static void Configure(CommandLineApplication command, CommandLineOptions options, bool dry = false)
        {
            command.Description = $"converts audio input to text";
            command.HelpOption("-h|--help");
            if(options.TheService!=null)
                command.FullName = $"Application: {options.TheService.Service}:{options.TheService.Version}";

            var inputUriOption = command.Option(@"-i|--input",
            "tar with audio files",
            CommandOptionType.SingleValue
            );
            var outputUriOption = command.Option("-o|--output",
                 "output result url",
                 CommandOptionType.SingleValue
                 );

            var parallelismOption = command.Option($"-t|--threads <1>",
                "parallelism",
                CommandOptionType.SingleValue
                );

            var iFormat = command.Option($"-r|--reader <raw:4096>",
                "read input as raw:$chunkSizeBytes|proto|json",
                CommandOptionType.SingleValue
                );

            var oFormat = command.Option($"-w|--writer <json>",
                "write output as json|proto|simple|text:v2t|text:ppc|text:pnc|ntext:v2t|ntext:ppc|ntext:pnc",
                CommandOptionType.SingleValue
                );
            var retryOption = command.Option("--retry <10:1000:1.5>"
              , "retry with exponencial backoff (batch mode only) count:initDelayMs:multiplier"
             , CommandOptionType.SingleValue);

            /*input*/
            command.ExtendedHelpText = Environment.NewLine + "Options: " + Environment.NewLine;
            var audioFormatOption = command.Option($"--{Const.i_audio_format} <auto:0>",
                 $"input format auto:$probeSizeBytes|pcm:$pcmFormat:$sampleRate:$channelLayout",
                CommandOptionType.SingleValue
                );
            

            var channelOption = command.Option($"--{Const.i_audio_channel} <downmix>"
               , $"choose audio channel {string.Join("|", AudioDecoder.channelSelects)}"
              , CommandOptionType.SingleValue);

            command.ExtendedHelpText += audioFormatOption.RenderOption();
            command.ExtendedHelpText += AudioDecoder.pcmFormats.RenderEnumHelp("$pcmFormat");
            command.ExtendedHelpText += AudioDecoder.sampleRates.RenderEnumHelp("$sampleRate");
            command.ExtendedHelpText += AudioDecoder.channelLayouts.RenderEnumHelp("$channelLayout");

            command.ExtendedHelpText += channelOption.RenderOption();

            var decoderFeatures = command.Option($"--{Const.features} <none>",
                 $"features (lookahead,latency,novad,nospk,novprint,noppc,nopnc)",
                CommandOptionType.SingleValue
                );

            var pipe = command.Option("-p|--pipe",
                "run in pipe mode",
                CommandOptionType.NoValue);

            var lexiconOption = command.Option($"--{Const.lexicon} <none>",
                 $"extra lexicon to use",
                CommandOptionType.SingleValue
                );

            command.ExtendedHelpText += decoderFeatures.RenderOption();
            command.ExtendedHelpText += lexiconOption.RenderOption();

            command.OnExecute(() =>
            {

                
                inputUriOption.MustSetValue(command);

                options.Command = new Command(command, options)
                {
                    OutputUriOption = outputUriOption.GetValueOrDefault(),
                    InputUriOption = inputUriOption.GetValueOrDefault(),
                    AudioFormatOption = audioFormatOption.GetValueOrDefault(),
                    ChannelOption = channelOption.GetValueOrDefault(),
                    IFormat = iFormat.GetValueOrDefault().StartsWith("raw:") ? "raw" : iFormat.GetValueOrDefault(),
                    ChunkSizeBytes = iFormat.GetValueOrDefault().StartsWith("raw:") ? uint.Parse(iFormat.GetValueOrDefault()[4..]) : 0,
                    OFormat = oFormat.GetValueOrDefault(),
                    Pipe = pipe.HasValue(),
                    DecoderFeatures = decoderFeatures.GetValueOrDefault(),
                    LexiconUrlOption = lexiconOption.GetValueOrDefault(),
                    Parallelism = uint.Parse(parallelismOption.GetValueOrDefault()),
                    Retry = api.util.RetryWithBackoff.ParseFromCmd(retryOption.GetValueOrDefault()),
                };

                return 0;
            });
        }
        
        private readonly CommandLineOptions _opts;
        private string OutputUriOption { get; set; }
        private string InputUriOption { get; set; }
        private string AudioFormatOption { get; set; }
        private string ChannelOption { get; set; }
        private uint ChunkSizeBytes { get; set; }
        private string IFormat { get; set; }
        private string OFormat { get; set; }

        private string LexiconUrlOption { get; set; }
        private string DecoderFeatures { get; set; }

        private uint Parallelism { get; set; }
        private bool Pipe { get; set; }
        
        private Dictionary<string,string> Labels { get; set; }
        private api.util.RetryWithBackoff Retry { get; set; }
        public Command(CommandLineApplication app, CommandLineOptions opts)
        {

            _opts = opts;
        }
        public async Task<int> RunAsync(CancellationToken breaker)
        {

            using var input = LazyStream.Input(InputUriOption, breaker);
            using var output = LazyStream.Output(OutputUriOption, "application/x-tar");

            var configuration = new api.proto.Payload
            {
                Chunk =
                    {
                        new api.proto.Item{ Key = Const.i_audio_format, S = AudioFormatOption, Type = "s" },
                        new api.proto.Item { Key = Const.i_audio_channel, S = ChannelOption, Type = "s" },
                        new api.proto.Item { Key = Const.features, S = DecoderFeatures, Type = "s" },
                        CmdUtils.LexiconFromUrl(LexiconUrlOption)
                    }
            };


            async Task<TarEntry> rewrite(TarEntry x)
            {

                _logger.LogInformation($"Processing {x.Name}");
                using var call = _opts.CreateStreaming();
                var configured = await call.Configure(configuration, breaker);
                var accepts = configured.Chunk.First(x => x.Key == "accepts").Tags.ToArray();
                var localInput = x.DataStream;

                var ret = new UstarTarEntry(TarEntryType.RegularFile, x.Name + ".atran");
                ret.DataStream = new MemoryStream();
                var localOutput = ret.DataStream;
                var pipe = (IFormat switch
                {
                    "raw" => localInput.AsRawAudioSource(chunkSize: (int)ChunkSizeBytes, cancellationToken: breaker),
                    "proto" => localInput.AsProtoBinarySource<api.proto.Payload>(breaker),
                    "json" => localInput.AsProtoJsonSource<api.proto.Payload>(breaker),
                    _ => throw new NotImplementedException($"unsuported input format {IFormat}"),
                });

                pipe = pipe.ViaTaskRunner(call, accepts, Pipe);

                await (OFormat switch
                {
                    "proto" => pipe.RunWithSink(localOutput.AsBinaryProtoSink<api.proto.Payload>(), cancellationToken: breaker),
                    "json" => pipe.RunWithSink(localOutput.AsJsonProtoSink<api.proto.Payload>(), cancellationToken: breaker),
                    "simple" => pipe.ToSimpleText().RunWithSink(localOutput.AsTextChunkSink(),cancellationToken: breaker),
                    "text:v2t" => pipe.ToText("v2t").RunWithSink(localOutput.AsTextChunkSink(),cancellationToken: breaker),
                    "text:ppc" => pipe.ToText("ppc").RunWithSink(localOutput.AsTextChunkSink(),cancellationToken: breaker),
                    "text:pnc" => pipe.ToText("pnc").RunWithSink(localOutput.AsTextChunkSink(),cancellationToken: breaker),
                    "ntext:v2t" => pipe.ToNText("v2t").RunWithSink(localOutput.AsTextChunkSink(),cancellationToken: breaker),
                    "ntext:ppc" => pipe.ToNText("ppc").RunWithSink(localOutput.AsTextChunkSink(),cancellationToken: breaker),
                    "ntext:pnc" => pipe.ToNText("pnc").RunWithSink(localOutput.AsTextChunkSink(),cancellationToken: breaker),
                    _ => throw new NotImplementedException($"unsuported output format {OFormat}"),
                });

                _logger.LogInformation($"Completed {ret.Name}");
                localOutput.Seek(0, SeekOrigin.Begin);
                return ret;

            }
            await input.AsTarSource(breaker).ViaAsyncMapperParallel(rewrite,(int)Parallelism, Retry,breaker).RunWithSink(output.AsTarSink());
            output.Complete();

            return 0;
        }

    }


}
