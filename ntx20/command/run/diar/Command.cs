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
using static Grpc.Core.Metadata;
using System.Formats.Tar;
using Azure.Core;
namespace ntx20.command.run.diar
{

    class Command : ICommand
    {
        private static readonly ILogger _logger = Logging.LoggerFactory.CreateLogger("ntx20.command.run.diar");

        internal static void Configure(CommandLineApplication command, CommandLineOptions options)
        {
            command.Description = $"audio diarization";
            command.HelpOption("-h|--help");
            command.FullName = $"Application: {options.TheService.Service}:{options.TheService.Version}";

            var inputUriOption = command.Option(@"-i|--input",
            "input audio url",
            CommandOptionType.SingleValue
            );
            var outputUriOption = command.Option("-o|--output <->",
                 "output features url",
                 CommandOptionType.SingleValue
                 );

            var iFormat = command.Option($"-r|--reader <raw:4096>",
                "read input as raw:$chunkSizeBytes|proto|json",
                CommandOptionType.SingleValue
                );

            var oFormat = command.Option($"-w|--writer <json>",
                "write output as json|proto|text",
                CommandOptionType.SingleValue
                );
            var flush = command.Option("-f|--flush",
                "enable flush on every write",
                CommandOptionType.NoValue
                );
            var processingMode = command.Option("-m|--mode <one>",
                 "processing mode one|tar:xx where xx is paralelism ",
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
                 $"features (lookahead,latency,novad,nospk,novprint)",
                CommandOptionType.SingleValue
                );

            var pipe = command.Option("-p|--pipe",
                "run in pipe mode",
                CommandOptionType.NoValue);


            command.ExtendedHelpText += decoderFeatures.RenderOption();

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
                    Flush = flush.HasValue(),
                    Pipe = pipe.HasValue(),
                    DecoderFeatures = decoderFeatures.GetValueOrDefault(),
                    ProcessingMode = CmdProcessingMode.Parse(processingMode.GetValueOrDefault()),
                    Retry = CmdRetryWithBackoff.ParseFromCmd(retryOption.GetValueOrDefault()),

                };

                return 0;
            });
        }
        private readonly CommandLineApplication _app;
        private CommandLineOptions _opts;
        private string OutputUriOption { get; set; }
        private string InputUriOption { get; set; }
        private string AudioFormatOption { get; set; }
        private string ChannelOption { get; set; }
        private uint ChunkSizeBytes { get; set; }
        private string IFormat { get; set; }
        private string OFormat { get; set; }

        private string DecoderFeatures { get; set; }
        private bool Pipe { get; set; }

        private bool Flush { get; set; }

        private CmdProcessingMode ProcessingMode { get; set; }
        private CmdRetryWithBackoff Retry { get; set; }
        public Command(CommandLineApplication app, CommandLineOptions opts)
        {
            _app = app;
            _opts = opts;
        }
        public async Task<int> RunAsync(CancellationToken breaker)
        {


            using var input = LazyStream.Input(InputUriOption, breaker);
            using var output = LazyStream.Output(OutputUriOption, 
                ProcessingMode.Mode == "one" ? "application/json" : "application/tar",breaker, true);


            var configuration = new api.proto.Payload
            {
                Chunk =
                    {
                        new api.proto.Item{ Key = Const.i_audio_format, S = AudioFormatOption, Type = "s" },
                        new api.proto.Item { Key = Const.i_audio_channel, S = ChannelOption, Type = "s" },
                        new api.proto.Item { Key = Const.features, S = DecoderFeatures, Type = "s" },
                    }
            };


            async Task DoJob(Stream istrem, Stream ostream)
            {
                using var call = _opts.CreateStreaming();
                var configured = await call.Configure(configuration, breaker);
                var accepts = configured.Chunk.First(x => x.Key == "accepts").Tags.ToArray();
                var pipe = (IFormat switch
                {
                    "raw" => istrem.AsRawAudioSource(chunkSize: (int)ChunkSizeBytes, cancellationToken: breaker),
                    "proto" => istrem.AsProtoBinarySource<api.proto.Payload>(breaker),
                    "json" => istrem.AsProtoJsonSource<api.proto.Payload>(breaker),
                    _ => throw new NotImplementedException($"unsuported input format {IFormat}"),
                });


                pipe = pipe.ViaTaskRunner(call, accepts, Pipe);

                await (OFormat switch
                {
                    "proto" => pipe.RunWithSink(ostream.AsBinaryProtoSink<api.proto.Payload>(), autoFlush: Flush, cancellationToken: breaker),
                    "json" => pipe.RunWithSink(ostream.AsJsonProtoSink<api.proto.Payload>(), autoFlush: Flush, cancellationToken: breaker),
                    _ => throw new NotImplementedException($"unsuported output format {OFormat}"),
                });
            }
            async Task RunOne()
            {
                _logger.LogInformation($"Starting {InputUriOption}");
                await DoJob(input, output);
                _logger.LogInformation($"Completed {InputUriOption}");
            }

            async Task<TarEntry> RunTar(TarEntry x)
            {
                var newname = x.Name + "." + OFormat.Replace(':', '-');
                _logger.LogInformation($"Starting {x.Name}");
                var ret = new PaxTarEntry(TarEntryType.RegularFile, newname);
                ret.DataStream = new MemoryStream();
                await DoJob(x.DataStream, ret.DataStream);
                ret.DataStream.Seek(0, SeekOrigin.Begin);
                _logger.LogInformation($"Completed {ret.Name}");
                return ret;
            }

            await (ProcessingMode.Mode switch
            {
                "one" => RunOne(),
                "tar" => input.AsTarSource(breaker).ViaAsyncMapperParallel(RunTar, ProcessingMode.Parallelism, Retry, breaker).RunWithSink(output.AsTarSink()),
                _ => throw new NotImplementedException(ProcessingMode.Mode)
            });
            output.Complete();
            return 0;
        }


    }


}
