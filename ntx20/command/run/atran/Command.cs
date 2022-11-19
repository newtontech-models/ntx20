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


namespace ntx20.command.run.atran
{

    class Command : ICommand
    {
        private static readonly ILogger _logger = Logging.LoggerFactory.CreateLogger("ntx20.command.run.atran");

        internal static void Configure(CommandLineApplication command, CommandLineOptions options)
        {
            command.Description = $"converts audio input to text";
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
                "write output as json|proto|simple|text:v2t|text:ppc|text:pnc|ntext:v2t|ntext:ppc|ntext:pnc",
                CommandOptionType.SingleValue
                );
            var flush = command.Option("-f|--flush",
                "enable flush on every write",
                CommandOptionType.NoValue
                );

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
                    LexiconUrlOption = lexiconOption.GetValueOrDefault()

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
        public Command(CommandLineApplication app, CommandLineOptions opts)
        {
            _app = app;
            _opts = opts;
        }
        public async Task<int> RunAsync(CancellationToken breaker)
        {

            using var input = LazyStream.Input(InputUriOption, breaker);
            using var output = LazyStream.Output(OutputUriOption, "binary", breaker);
            
            using var call = _opts.CreateStreaming();

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
            
            var configured = await call.Configure(configuration, breaker);
            var accepts = configured.Chunk.First(x => x.Key == "accepts").Tags.ToArray();

            _logger.LogInformation($"Task {_opts.TheService.Service}:{_opts.TheService.Version} configured");

            var pipe = (IFormat switch
            {
                "raw" => input.AsRawAudioSource(chunkSize: (int)ChunkSizeBytes, cancellationToken: breaker),
                "proto" => input.AsProtoBinarySource<api.proto.Payload>(breaker),
                "json" => input.AsProtoJsonSource<api.proto.Payload>(breaker),
                _ => throw new NotImplementedException($"unsuported input format {IFormat}"),
            });


            pipe = pipe.ViaTaskRunner(call, accepts, Pipe);

            await (OFormat switch
            {
                "proto" => pipe.RunWithSink(output.AsBinaryProtoSink<api.proto.Payload>(), autoFlush: Flush, cancellationToken: breaker),
                "json" => pipe.RunWithSink(output.AsJsonProtoSink<api.proto.Payload>(), autoFlush: Flush, cancellationToken: breaker),
                "simple" => pipe.ToSimpleText().RunWithSink(output.AsTextChunkSink(), autoFlush: Flush, cancellationToken: breaker),
                "text:v2t" => pipe.ToText("v2t").RunWithSink(output.AsTextChunkSink(), autoFlush: Flush, cancellationToken: breaker),
                "text:ppc" => pipe.ToText("ppc").RunWithSink(output.AsTextChunkSink(), autoFlush: Flush, cancellationToken: breaker),
                "text:pnc" => pipe.ToText("pnc").RunWithSink(output.AsTextChunkSink(), autoFlush: Flush, cancellationToken: breaker),
                "ntext:v2t" => pipe.ToNText("v2t").RunWithSink(output.AsTextChunkSink(), autoFlush: Flush, cancellationToken: breaker),
                "ntext:ppc" => pipe.ToNText("ppc").RunWithSink(output.AsTextChunkSink(), autoFlush: Flush, cancellationToken: breaker),
                "ntext:pnc" => pipe.ToNText("pnc").RunWithSink(output.AsTextChunkSink(), autoFlush: Flush, cancellationToken: breaker),
                _ => throw new NotImplementedException($"unsuported output format {OFormat}"),
            });

            _logger.LogInformation($"Task {_opts.TheService.Service}:{_opts.TheService.Version} completed");
            return 0;
        }


    }


}
