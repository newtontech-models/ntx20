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
namespace ntx20.command.run.adsp
{

    class Command : ICommand
    {
        private static readonly ILogger _logger = Logging.LoggerFactory.CreateLogger("ntx20.command.run.afeat");

        internal static void Configure(CommandLineApplication command, CommandLineOptions options)
        {
            command.Description = $"converts audio input to stream of tensors";
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
                "write output as htk:file|htk:stream|json|proto",
                CommandOptionType.SingleValue
                );


            var pipe = command.Option("-p|--pipe",
                "run in pipe mode",
                CommandOptionType.NoValue
            );

            var flush = command.Option("-f|--flush",
                "enable flush on every write",
                CommandOptionType.NoValue
                );


            /*input*/
            command.ExtendedHelpText = Environment.NewLine + "Input options: " + Environment.NewLine;
            var audioFormatOption = command.Option($"--{Const.i_audio_format} <auto:0>",
                 $"format auto:$probeSizeBytes|pcm:$pcmFormat:$sampleRate:$channelLayout",
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
            
            /*output*/
            command.ExtendedHelpText += Environment.NewLine + "Output options: " + Environment.NewLine;

            command.OnExecute(() =>
            {

                inputUriOption.MustSetValue(command);

                options.Command = new Command(command,options)
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
                };

                return 0;
            });
        }
        private readonly CommandLineApplication _app;
        private string OutputUriOption { get; set; }
        private string InputUriOption { get; set; }
        private string AudioFormatOption { get; set; }
        private string ChannelOption { get; set; }
        private uint ChunkSizeBytes { get; set; }
        private string AudioFrameSize { get; set; }
        private string IFormat { get; set; }
        private string OFormat { get; set; }
        private bool Flush { get; set; }
        private bool Pipe { get; set; }
        private readonly CommandLineOptions _opts;
        public Command(CommandLineApplication app, CommandLineOptions opts)
        {
            _app = app;
            _opts = opts;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2016:Forward the 'CancellationToken' parameter to methods that take one", Justification = "<Pending>")]
        public async Task<int> RunAsync(CancellationToken breaker)
        {


            //var resource = await LazyStream.Input(Resource).GetTextAsync();

            using var input = LazyStream.Input(InputUriOption, breaker);
            using var output = LazyStream.Output(OutputUriOption, "binary");


            using var call = _opts.CreateStreaming();

            var configuration = new api.proto.Payload
            {
                Chunk =
                {
                    new api.proto.Item{ Key = Const.i_audio_format, S = AudioFormatOption, Type = "s" },
                    new api.proto.Item { Key = Const.i_audio_channel, S = ChannelOption, Type = "s" },
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


            pipe= pipe.ViaTaskRunner(call, accepts, Pipe);

            await (OFormat switch
            {
                "proto" => pipe.RunWithSink(output.AsBinaryProtoSink<api.proto.Payload>(), autoFlush: Flush),
                "json" => pipe.RunWithSink(output.AsJsonProtoSink<api.proto.Payload>(), autoFlush: Flush),
                "htk:file" => pipe.ToRawHtk(configured, true).RunWithSink(output.AsRawChunkSink(), autoFlush: Flush),
                "htk:stream" => pipe.ToRawHtk(configured, false).RunWithSink(output.AsRawChunkSink(), autoFlush: Flush),
                _ => throw new NotImplementedException($"unsuported output format {OFormat}"),
            });
            output.Complete();
            _logger.LogInformation($"Task  {_opts.TheService.Service}:{_opts.TheService.Version} completed");
            return 0;
        }


    }


}
