using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging;
using ntx20.api;
using ntx20.api.io;
using ntx20.api.pipe;
using ntx20.api.proto;
using ntx20.api.utils;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ntx20.command.task.run
{
    
    class Command :ICommand
    {

        private static readonly string[] pcmFormats = { "s16le", "alaw", "mulaw" };
        private static readonly string[] sampleFormats = { "i2" };
        private static readonly string[] sampleRates = { "8000", "16000", "32000", "48000", "96000", "11025", "22050", "44100" };
        private static readonly string[] channelLayouts = { "mono", "stereo" };
        private static readonly string[] channelSelects = { "downmix", "left", "right" };

        private static readonly ILogger _logger = Logging.LoggerFactory.CreateLogger("ntx20.command.task.run");
        internal static void Configure(CommandLineApplication command, CommandLineOptions options)
        {
            
            var taskOption = command.Argument("task", "task name");
            command.Description = "run task";
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
                "write output as json|proto",
                CommandOptionType.SingleValue
                );
            var flush = command.Option("-f|--flush",
                "enable flush on every write",
                CommandOptionType.NoValue
                );

            /*input*/
            command.ExtendedHelpText = Environment.NewLine + "Options: " + Environment.NewLine;
            var audioFormatOption = command.Option($"--audio-format <auto:0>",
                 $"input format auto:$probeSizeBytes|pcm:$pcmFormat:$sampleRate:$channelLayout",
                CommandOptionType.SingleValue
                );


            var channelOption = command.Option($"--audio-channel <downmix>"
               , $"choose audio channel {string.Join("|", channelSelects)}"
              , CommandOptionType.SingleValue);

            command.ExtendedHelpText += audioFormatOption.RenderOption();
            command.ExtendedHelpText += pcmFormats.RenderEnumHelp("$pcmFormat");
            command.ExtendedHelpText += sampleRates.RenderEnumHelp("$sampleRate");
            command.ExtendedHelpText += channelLayouts.RenderEnumHelp("$channelLayout");

            command.ExtendedHelpText += channelOption.RenderOption();

            var decoderFeatures = command.Option($"--features <none>",
                 $"features (lookahead,latency,dictation,novad,nospk,novprint,noppc,nopnc)",
                CommandOptionType.SingleValue
                );

            var lexiconOption = command.Option($"--lexicon <none>",
                 $"extra lexicon to use",
                CommandOptionType.SingleValue
                );

            var pipe = command.Option("-p|--pipe",
                "run in pipe mode",
                CommandOptionType.NoValue);

            command.OnExecute(() =>
            {
                taskOption.MustSetValue(command);
                options.Command = new Command(command)
                {
                    OutputUriOption = outputUriOption.GetValueOrDefault(),
                    InputUriOption = inputUriOption.GetValueOrDefault(),
                    AudioFormatOption = audioFormatOption.GetValueOrDefault(),
                    AudioChannelOption = channelOption.GetValueOrDefault(),
                    IFormat = iFormat.GetValueOrDefault().StartsWith("raw:") ? "raw" : iFormat.GetValueOrDefault(),
                    ChunkSizeBytes = iFormat.GetValueOrDefault().StartsWith("raw:") ? uint.Parse(iFormat.GetValueOrDefault()[4..]) : 0,
                    OFormat = oFormat.GetValueOrDefault(),
                    Flush = flush.HasValue(),
                    Pipe = pipe.HasValue(),
                    Features = decoderFeatures.GetValueOrDefault(),
                    TaskName = taskOption.Value,
                    LexiconUriOption = lexiconOption.GetValueOrDefault(),
                };
                return 0;
            });

           
        }

        private string OutputUriOption { get; set; }
        private string InputUriOption { get; set; }
        private string AudioFormatOption { get; set; }
        
        private string LexiconUriOption { get; set; }
        private string AudioChannelOption { get; set; }
        private uint ChunkSizeBytes { get; set; }
        private string IFormat { get; set; }
        private string OFormat { get; set; }

        private string TaskName { get; set; }

        private string Features { get; set; }
        private bool Pipe { get; set; }

        private bool Flush { get; set; }

        private readonly CommandLineApplication _app;

        public Command(CommandLineApplication app)
        {
            _app = app;
        }
        public async Task<int> RunAsync(CancellationToken breaker)
        {
            var endpoint = "http://localhost:6666";
            using var channel = GrpcChannel.ForAddress(endpoint);

            using var input = LazyStream.Input(InputUriOption, breaker);
            using var output = LazyStream.Output(OutputUriOption, "binary", breaker);


            var configuration = new Payload()
            {
                Chunk =
                {
                    new Item{ Key = "audio-format", S = AudioFormatOption, Type = "s" },
                    new Item{ Key = "audio-channel", S = AudioChannelOption, Type = "s" },
                    new Item{ Key = "features", S = Features, Type = "s" },
                    CmdUtils.LexiconFromUrl(LexiconUriOption),
                }
            };

            //add metadata
            var meta = new Metadata
            {
                { "task",  TaskName},
            };

            //estabilish connection
            
            using var call = new EngineService.EngineServiceClient(channel).Streaming(meta);
            _logger.LogInformation($"Task {TaskName} created");


            //start & configure service
            var configured = await call.Configure(configuration);
            _logger.LogInformation($"Task {TaskName} configured");

            //accepts tracks
            var accepts = configured.Chunk.First(x => x.Key == "accepts").Tags.ToArray();

            if(IFormat == "raw")
            {
                if(!accepts.Contains("aud"))
                    throw new Exception("This task doesn't support raw input format!");

                IFormat = accepts.Contains("vad") ?  "raw+aud+vad" : "raw+aud";
            }
            
            var pipe = (IFormat switch
            {
                "raw+aud" => input.AsRawAudioSource(chunkSize: (int)ChunkSizeBytes, cancellationToken: breaker),
                "raw+aud+vad" => input.AsRawAudioSourceWithVAD(chunkSize: (int)ChunkSizeBytes, cancellationToken: breaker),
                "proto" => input.AsProtoBinarySource<api.proto.Payload>(breaker),
                "json" => input.AsProtoJsonSource<api.proto.Payload>(breaker),
                _ => throw new NotImplementedException($"unsuported input format {IFormat}"),
            });


            pipe = pipe.ViaTaskRunner(call, accepts, Pipe);

            await (OFormat switch
            {
                "proto" => pipe.RunWithSink(output.AsBinaryProtoSink<api.proto.Payload>(), autoFlush: Flush, cancellationToken: breaker),
                "json" => pipe.RunWithSink(output.AsJsonProtoSink<api.proto.Payload>(), autoFlush: Flush, cancellationToken: breaker),
                _ => throw new NotImplementedException($"unsuported output format {OFormat}"),
            });

            _logger.LogInformation($"Task {TaskName} completed");


            return 0;
        }


    }


}
