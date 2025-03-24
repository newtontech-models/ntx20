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
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech;
namespace ntx20.command.run.mstran
{

    class Command : ICommand
    {
        private static readonly ILogger _logger = Logging.LoggerFactory.CreateLogger("ntx20.command.run.mstran");

        internal static void Configure(CommandLineApplication command, CommandLineOptions options)
        {
            command.Description = $"trascription with msft";
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
                "write output as json|proto|text:v2t,text:tpc",
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
                 $"features ",
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
        public Command(CommandLineApplication app, CommandLineOptions opts)
        {
            _app = app;
            _opts = opts;
        }
        public async Task<int> RunAsync(CancellationToken breaker)
        {


            using var input = LazyStream.Input(InputUriOption, breaker);
            using var output = LazyStream.Output(OutputUriOption, "binary", breaker);


            var configuration = _opts.CreateConfig();

            var lang = _opts.TheService.Service.Trim().Substring(7);
            configuration.SpeechRecognitionLanguage = lang;

            var pipe = (IFormat switch
            {
                "raw" => input.AsRawAudioSource(chunkSize: (int)ChunkSizeBytes, cancellationToken: breaker),
                _ => throw new NotImplementedException($"unsuported input format {IFormat}"),
            });


            pipe = pipe.ViaMSFTRt(configuration);

            await (OFormat switch
            {
                "json" => pipe.RunWithSink(output.AsJsonProtoSink<api.proto.Payload>(), autoFlush: Flush, cancellationToken: breaker),
                "proto" => pipe.RunWithSink(output.AsBinaryProtoSink<api.proto.Payload>(), autoFlush: Flush, cancellationToken: breaker),
                "simple" => pipe.ToSimpleText().RunWithSink(output.AsTextChunkSink(), autoFlush: Flush, cancellationToken: breaker),
                "text:v2t" => pipe.ToText("v2t").RunWithSink(output.AsTextChunkSink(), autoFlush: Flush, cancellationToken: breaker),
                "text:tpc" => pipe.ToText("tpc").RunWithSink(output.AsTextChunkSink(), autoFlush: Flush, cancellationToken: breaker),
                _ => throw new NotImplementedException($"unsuported output format {OFormat}"),
            });
            output.Complete();
            _logger.LogInformation($"Task  {_opts.TheService.Service}:{_opts.TheService.Version} completed");
            return 0;
        }


    }


}
