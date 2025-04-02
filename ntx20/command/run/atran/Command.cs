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
using System.Formats.Tar;

namespace ntx20.command.run.atran
{

    class Command : ICommand
    {
        private static readonly ILogger _logger = Logging.LoggerFactory.CreateLogger("ntx20.command.run.atran");

        
        internal static void Configure(CommandLineApplication command, CommandLineOptions options, bool dry = false)
        {
            command.Description = $"converts audio input to text";
            command.HelpOption("-h|--help");
            if(options.TheService!=null)
                command.FullName = $"Application: {options.TheService.Service}:{options.TheService.Version}";

            var inputUriOption = command.Option(@"-i|--input",
            "one mode: input audio url | batch mode: stream with command lines for individual tasks",
            CommandOptionType.SingleValue
            );
            var outputUriOption = command.Option("-o|--output <->",
                 "output result url",
                 CommandOptionType.SingleValue
                 );


            var iFormat = command.Option($"-r|--reader <raw:4096>",
                "read input as raw:$chunkSizeBytes|proto|json",
                CommandOptionType.SingleValue
                );

            var oFormat = command.Option($"-w|--writer <json>",
                "write output as json|proto|simple|text:v2t|text:ppc|text:pnc|ntext:v2t|ntext:ppc|ntext:pnc|console:v2t|console:ppc|console:pnc",
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
                    Flush = flush.HasValue(),
                    Pipe = pipe.HasValue(),
                    DecoderFeatures = decoderFeatures.GetValueOrDefault(),
                    LexiconUrlOption = lexiconOption.GetValueOrDefault(),
                    ProcessingMode = CmdProcessingMode.Parse(processingMode.GetValueOrDefault()),
                    Retry = CmdRetryWithBackoff.ParseFromCmd(retryOption.GetValueOrDefault()),

                };

                return 0;
            });
        }
        private readonly CommandLineApplication _app;
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
                ProcessingMode.Mode == "one" ? "application/json" : "application/tar",
                breaker,true);

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



            async Task DoJob(Stream localInput, Stream localOutput)
            {
                using var call = _opts.CreateStreaming();
                var configured = await call.Configure(configuration, breaker);
                var accepts = configured.Chunk.First(x => x.Key == "accepts").Tags.ToArray();

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
                    "proto" => pipe.RunWithSink(localOutput.AsBinaryProtoSink<api.proto.Payload>(), autoFlush: Flush, cancellationToken: breaker),
                    "json" => pipe.RunWithSink(localOutput.AsJsonProtoSink<api.proto.Payload>(), autoFlush: Flush, cancellationToken: breaker),
                    "simple" => pipe.ToSimpleText().RunWithSink(localOutput.AsTextChunkSink(), autoFlush: Flush, cancellationToken: breaker),
                    "text:v2t" => pipe.ToText("v2t").RunWithSink(localOutput.AsTextChunkSink(), autoFlush: Flush, cancellationToken: breaker),
                    "text:ppc" => pipe.ToText("ppc").RunWithSink(localOutput.AsTextChunkSink(), autoFlush: Flush, cancellationToken: breaker),
                    "text:pnc" => pipe.ToText("pnc").RunWithSink(localOutput.AsTextChunkSink(), autoFlush: Flush, cancellationToken: breaker),
                    "ntext:v2t" => pipe.ToNText("v2t").RunWithSink(localOutput.AsTextChunkSink(), autoFlush: Flush, cancellationToken: breaker),
                    "ntext:ppc" => pipe.ToNText("ppc").RunWithSink(localOutput.AsTextChunkSink(), autoFlush: Flush, cancellationToken: breaker),
                    "ntext:pnc" => pipe.ToNText("pnc").RunWithSink(localOutput.AsTextChunkSink(), autoFlush: Flush, cancellationToken: breaker),
                    "console:v2t" => pipe.RunWithSink(Sink.ConsolePayloadSink("v2t"), autoFlush: Flush, cancellationToken: breaker),
                    "console:ppc" => pipe.RunWithSink(Sink.ConsolePayloadSink("ppc"), autoFlush: Flush, cancellationToken: breaker),
                    "console:pnc" => pipe.RunWithSink(Sink.ConsolePayloadSink("pnc"), autoFlush: Flush, cancellationToken: breaker),
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
                var newname = x.Name + "."+OFormat.Replace(':','-');
                _logger.LogInformation($"Starting {x.Name}");
                var ret = new UstarTarEntry(TarEntryType.RegularFile, newname);
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
