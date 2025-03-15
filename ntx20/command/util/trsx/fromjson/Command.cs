using System.Threading;
using Microsoft.Extensions.CommandLineUtils;
using System.Threading.Tasks;
using ntx20.api.io;
using ntx20.api;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using ntx20.api.pipe;
using System.Linq;
using ntx20.api.utils;
using Google.Protobuf.Collections;
using Microsoft.Extensions.Logging;
using System;
using Google.Protobuf.WellKnownTypes;

namespace ntx20.command.util.trsx.fromjson
{
    class Command : ICommand
    {
        private static readonly ILogger _logger = Logging.LoggerFactory.CreateLogger("ntx20.command.tool.trsx.fromjson");
        internal static void Configure(CommandLineApplication command, CommandLineOptions options)
        {
            
            command.Description = "converts to legacy format";
            command.HelpOption("-h|--help");
            var outputUriOption = command.Option("-o|--output <->",
                "output proto url",
                CommandOptionType.SingleValue
                );
            var inputUriOption = command.Option(@"-i|--input <none>",
            "transcription json url",
            CommandOptionType.SingleValue
            );

            var diarUriOption = command.Option(@"-d|--diar <none>",
            "diar json url",
            CommandOptionType.SingleValue
            );
            var mediaUriOption = command.Option("-m|--media <none>",
                "media uri to link with",
                CommandOptionType.SingleValue
                );

            var flush = command.Option("-f|--flush",
                "enable flush on every write",
                CommandOptionType.NoValue
                );
            var trackOption = command.Option(@"-t|--track <auto>",
            "set input track id",
            CommandOptionType.SingleValue
            );

            command.OnExecute(() =>
            {
                var iuo = inputUriOption.GetValueOrDefault();
                var dia = diarUriOption.GetValueOrDefault();
                if (iuo == "none" && dia == "none")
                {
                    command.ShowHelp();
                    command.ShowHelp();
                    command.Error.WriteLine($"Please set  one of {inputUriOption.LongName} or {diarUriOption.LongName}");
                    
                    throw new ArgumentException("Invalid option");
                }
                options.Command = new Command(command)
                {
                    OutputUriOption = outputUriOption.GetValueOrDefault(),
                    InputUriOption = iuo,
                    Flush = flush.HasValue(),
                    TrackOption = trackOption.GetValueOrDefault(),
                    DiarUriOption = dia,
                    MediaUriOption = mediaUriOption.GetValueOrDefault()

                };
                return 0;
            });
        }

        private string OutputUriOption { get; set; }
        private string InputUriOption { get; set; }
        private string DiarUriOption { get; set; }
        private string TrackOption { get; set; }
        private string MediaUriOption { get; set; }
        private bool Flush { get; set; }
        private readonly CommandLineApplication _app;
        
        public Command(CommandLineApplication app)
        {
            _app = app;
        }
        public async Task<int> RunAsync(CancellationToken breaker)
        {

            var tracks = TrackOption == "auto" ? new string[] { "tpc","pnc" } : new string[] { TrackOption };
            IEnumerable<api.proto.Item> diarTrack;
           IEnumerable<api.proto.Item> atranTrack;

            if (InputUriOption == DiarUriOption)
            {
                var dtranStream = await
                    LazyStream.Input(InputUriOption, breaker)
                    .AsProtoJsonSource<api.proto.Payload>(breaker).ToListAsync();

                diarTrack = dtranStream.Where(x => x.Track == "spk").SelectMany(x=>x.Chunk).Where(x => !x.Tags.Contains("la"));
                
                atranTrack = dtranStream.Where(x=> tracks.Contains(x.Track)).SelectMany(x=>x.Chunk).Where(x => !x.Tags.Contains("la"));
                if (atranTrack.Count() == 0)
                {
                    throw new Exception($"Transcription track {TrackOption} is empty");
                }
            }
            else
            {
                if(InputUriOption == "none")
                {
                    atranTrack =  new List<api.proto.Item>();
                }
                else
                {
                    atranTrack = await LazyStream.Input(InputUriOption, breaker)
                        .AsProtoJsonSource<api.proto.Payload>(breaker)
                        .Where(x => tracks.Contains(x.Track))
                        .SelectMany(x => x.Chunk.ToAsyncEnumerable()).Where(x=>!x.Tags.Contains("la"))
                        .ToListAsync();
                    if (atranTrack.Count() == 0)
                    {
                        throw new Exception($"Transcription track {TrackOption} is empty");
                    }

                }

                if (DiarUriOption == "none")
                {
                    var lastTs = atranTrack.Last(x => x.Key == "ts");
                    diarTrack = new List<api.proto.Item>()
                    {
                        new api.proto.Item{ Key="ts"},
                        new api.proto.Item{Key="txt",S="spk00"},
                        lastTs,
                    };
                }
                else
                {
                    diarTrack = await LazyStream.Input(DiarUriOption, breaker)
                        .AsProtoJsonSource<api.proto.Payload>(breaker)
                        .Where(x => x.Track == "spk")
                        .SelectMany(x => x.Chunk.ToAsyncEnumerable()).Where(x => !x.Tags.Contains("la"))
                        .ToListAsync();
                    if (diarTrack.Count() == 0)
                    {
                        throw new Exception($"Diarization track spk is empty");
                    }
                }
            }


            using var output = LazyStream.Output(OutputUriOption, "text/xml");
            var writer = new StringChunkWriter(output);
            var trsx = Trsx.Create(diarTrack, atranTrack, MediaUriOption);
            await writer.WriteAsync(trsx.Declaration.ToString() + "\n");
            await writer.WriteAsync(trsx.ToString());
            await writer.FlushAsync();  
            output.Complete();
            return 0;
        }
    }
}

