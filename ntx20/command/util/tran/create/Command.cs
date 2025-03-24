using System.Threading;
using Microsoft.Extensions.CommandLineUtils;
using System.Threading.Tasks;
using ntx20.api.io;
using ntx20.api;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using ntx20.api.pipe;
using ntx20.api.utils;
using Google.Protobuf.Collections;
using Microsoft.Extensions.Logging;
using System;
using Google.Protobuf.WellKnownTypes;
using System.Data;
using System.IO.Pipelines;

namespace ntx20.command.util.tran.create
{
    class Command : ICommand
    {
        private static readonly ILogger _logger = Logging.LoggerFactory.CreateLogger("ntx20.command.util.tran.create");
        internal static void Configure(CommandLineApplication command, CommandLineOptions options)
        {
            
            command.Description = "merge atran and diar to transcription";
            command.HelpOption("-h|--help");
            var outputUriOption = command.Option("-o|--output <->",
                "output proto url",
                CommandOptionType.SingleValue
                );
            var inputUriOption = command.Option(@"-i|--input <none>",
            "atran or dtran json url",
            CommandOptionType.SingleValue
            );

            var diarUriOption = command.Option(@"-d|--diar <none>",
            "diar json url",
            CommandOptionType.SingleValue
            );

            var flush = command.Option("-f|--flush",
                "enable flush on every write",
                CommandOptionType.NoValue
                );
            var trackOption = command.Option(@"-t|--track <pnc>",
            "set input track ids",
            CommandOptionType.SingleValue
            );
            var pipe = command.Option("-p|--pipe",
                "run in pipe mode",
                CommandOptionType.NoValue);
            var oFormat = command.Option($"-w|--writer <json>",
                "write output as json|trsx",
                CommandOptionType.SingleValue
                );
            var mediaUriOption = command.Option("-m|--media <none>",
                "media uri to link with",
                CommandOptionType.SingleValue
                );

            command.OnExecute(() =>
            {
                inputUriOption.MustSetValue(command);
                diarUriOption.MustSetValue(command);
                options.Command = new Command(command)
                {
                    OutputUriOption = outputUriOption.GetValueOrDefault(),
                    InputUriOption = inputUriOption.Value(),
                    DiarUriOption = diarUriOption.Value(),
                    TrackOption = trackOption.GetValueOrDefault(),
                    Pipe = pipe.HasValue(),
                    OFormat = oFormat.GetValueOrDefault(),
                    Flush = flush.HasValue(),
                    MediaUriOption = mediaUriOption.GetValueOrDefault(),
                };
                return 0;
            });
        }

        private string OutputUriOption { get; set; }
        private string InputUriOption { get; set; }
        private string DiarUriOption { get; set; }
        private string TrackOption { get; set; }
        private string OFormat { get; set; }
        private bool Pipe { get; set; }
        private string MediaUriOption { get; set; }
        private bool Flush { get; set; }
        private readonly CommandLineApplication _app;
        
        public Command(CommandLineApplication app)
        {
            _app = app;
        }
        public async Task<int> RunAsync(CancellationToken breaker)
        {

            using var output = LazyStream.Output(OutputUriOption, "application/json");

            var dtranStream = InputUriOption == "none" 
                ? (new api.proto.Payload[0]).ToAsyncEnumerable()
                : LazyStream.Input(InputUriOption, breaker).AsProtoJsonSource<api.proto.Payload>(breaker);

            //if atran convert to dtran
            dtranStream = dtranStream.Where(x => x.Track != "ppc")
                .Select(x => { if (x.Track == TrackOption) { x.Track = "tpc"; return x; } else { return x; } });

            if(InputUriOption != DiarUriOption)
            {
                var diarStream = DiarUriOption == "none"
                ? (new List<api.proto.Payload>() {
                    new api.proto.Payload { Track = "spk", Chunk = {
                            new api.proto.Item {Key ="ts"},
                            new api.proto.Item { Key = "txt", S = "S0000" }
                        } },
                }).ToAsyncEnumerable()
                : LazyStream.Input(DiarUriOption, breaker)
                .AsProtoJsonSource<api.proto.Payload>(breaker).Where(x => x.Track != "vad");
                dtranStream = dtranStream.MergeByTsWith(diarStream);
            }



            var pipe = dtranStream.Where(x => x.Track != "tran")
                .CreateTranTrack(Pipe);


            await (OFormat switch
            {
                "json" => pipe.RunWithSink(output.AsJsonProtoSink<api.proto.Payload>(), autoFlush: Flush, cancellationToken: breaker),
                "trsx" => pipe.ToTrsx(MediaUriOption).RunWithSink(output.AsTextChunkSink(), autoFlush: Flush, cancellationToken: breaker),
                _ => throw new NotImplementedException($"unsuported output format {OFormat}"),
            });
            output.Complete();

            return 0;
        }
    }
}

