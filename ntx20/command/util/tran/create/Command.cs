using System.Threading;
using Microsoft.Extensions.CommandLineUtils;
using System.Threading.Tasks;
using ntx20.api.io;
using ntx20.api;
using System.Collections.Generic;
using System.Linq;
using ntx20.api.pipe;
using Microsoft.Extensions.Logging;
using System;
using System.Data;
using static Grpc.Core.Metadata;
using System.IO;
using System.Diagnostics;
using System.Formats.Tar;

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
            var oFormat = command.Option($"--oformat <trsx>",
                "write output as json|trsx",
                CommandOptionType.SingleValue
                );
            var mediaUriOption = command.Option("--media <none>",
                "media uri to link with",
                CommandOptionType.SingleValue
                );
            var processingMode = command.Option("-m|--mode <one>",
                 "processing mode one|tar:xx where xx is paralelism ",
                 CommandOptionType.SingleValue
                 );
            var iFormat = command.Option($"--iformat <json:pnc>",
                "read input as json:pnc|json:tpc",
                CommandOptionType.SingleValue
                );
            var dFormat = command.Option($"--dformat <json:spk>",
                "read input as json:spk",
                CommandOptionType.SingleValue
                );

            command.OnExecute(() =>
            {
                if(("none" == inputUriOption.GetValueOrDefault()) && 
                    ("none" == diarUriOption.GetValueOrDefault()))
                {
                    throw new ArgumentException("Must set either --input or --diar");
                }
                diarUriOption.MustSetValue(command);
                options.Command = new Command(command)
                {
                    OutputUriOption = outputUriOption.GetValueOrDefault(),
                    InputUriOption = inputUriOption.GetValueOrDefault(),
                    DiarUriOption = diarUriOption.GetValueOrDefault(),
                    OFormat = oFormat.GetValueOrDefault(),
                    Flush = flush.HasValue(),
                    ProcessingMode = CmdProcessingMode.Parse(processingMode.GetValueOrDefault()),
                    MediaUriOption = mediaUriOption.GetValueOrDefault(),
                    IFormat = CmdFormat.FromSuffix(iFormat.GetValueOrDefault(), inputUriOption.GetValueOrDefault()),
                    DFormat = CmdFormat.FromSuffix(dFormat.GetValueOrDefault(), diarUriOption.GetValueOrDefault()),

                };
                return 0;
            });
        }

        private string OutputUriOption { get; set; }
        private string InputUriOption { get; set; }
        private string DiarUriOption { get; set; }
        private string OFormat { get; set; }
        private string MediaUriOption { get; set; }
        private bool Flush { get; set; }
        private CmdFormat IFormat { get; set; }
        private CmdFormat DFormat { get; set; }

        private CmdProcessingMode ProcessingMode { get; set; }

        private readonly CommandLineApplication _app;
        
        public Command(CommandLineApplication app)
        {
            _app = app;
        }
        
        public async Task<int> RunAsync(CancellationToken breaker)
        {

            using var output = LazyStream.Output(OutputUriOption, "application/json");
            using var input = LazyStream.Input(InputUriOption, breaker);
            using var diar = LazyStream.Input(DiarUriOption, breaker);


            async Task DoJob(Stream astream,Stream dstream, Stream oStream, string Media)
            {

                //TODO if astream == dstream, read only once

                var atranStream = (IFormat.Format switch
                {
                    "none" => (new api.proto.Payload[0]).ToAsyncEnumerable(),
                    "json" => astream.AsProtoJsonSource<api.proto.Payload>(breaker).Where(x=>x.Track == IFormat.Param)
                               .Select(x => { if (x.Track == IFormat.Param) { x.Track = "tpc"; return x; } else { return x; } }),

                     _ => throw new NotImplementedException($"unsuported input format {IFormat}"),
                });

                atranStream = atranStream.RemoveItem(x => x.Tags.Intersect(["la", "noise"]).Count() != 0);

                var diarStream = (DFormat.Format switch
                {
                    "none" => (new List<api.proto.Payload>() {
                            new api.proto.Payload { Track = "spk", Chunk = {
                            new api.proto.Item {Key ="ts"},
                            new api.proto.Item { Key = "txt", S = "S0000" }
                        } },}).ToAsyncEnumerable(),
                    "json" => dstream.AsProtoJsonSource<api.proto.Payload>(breaker).Where(x => x.Track == DFormat.Param)
                    .Select(x => { if (x.Track == DFormat.Param) { x.Track = "spk"; return x; } else { return x; } }),
                    _ => throw new NotImplementedException($"unsuported input format {DFormat}"),
                });

                var pipe  = atranStream
                    .MergeByTsWith(diarStream)
                    .CreateTranTrack(false);

                await (OFormat switch
                {
                    "json" => pipe.RunWithSink(oStream.AsJsonProtoSink<api.proto.Payload>(), autoFlush: Flush, cancellationToken: breaker),
                    "trsx" => pipe.ToTrsx(Media).RunWithSink(oStream.AsTextChunkSink(), autoFlush: Flush, cancellationToken: breaker),
                    _ => throw new NotImplementedException($"unsuported output format {OFormat}"),
                });
            }

            async Task RunOne()
            {
                _logger.LogInformation($"Starting {OutputUriOption}");
                await DoJob(input, diar, output, MediaUriOption);
                _logger.LogInformation($"Completed {OutputUriOption}");
            }

            async Task<TarEntry> RunTar1(TarEntry x)
            {
                var newname = Path.ChangeExtension(x.Name, OFormat.Replace(':', '-'));
                _logger.LogInformation($"Starting {newname}");
                var ret = new UstarTarEntry(TarEntryType.RegularFile, newname);
                ret.DataStream = new MemoryStream();
                await DoJob(null, x.DataStream,ret.DataStream, Path.GetFileNameWithoutExtension(x.Name));
                ret.DataStream.Seek(0, SeekOrigin.Begin);
                _logger.LogInformation($"Completed {ret.Name}");
                return ret;
            }
            async Task<TarEntry> RunTar2(TarEntry x)
            {
                var newname = Path.ChangeExtension(x.Name, OFormat.Replace(':', '-'));
                _logger.LogInformation($"Starting {newname}");
                var ret = new UstarTarEntry(TarEntryType.RegularFile, newname);
                ret.DataStream = new MemoryStream();
                await DoJob(x.DataStream, null, ret.DataStream, Path.GetFileNameWithoutExtension(x.Name));
                ret.DataStream.Seek(0, SeekOrigin.Begin);
                _logger.LogInformation($"Completed {ret.Name}");
                return ret;
            }

            async Task<TarEntry> RunTar(Tuple<TarEntry,TarEntry> x)
            {
                var newname = Path.ChangeExtension(x.Item1.Name, OFormat.Replace(':', '-'));
                _logger.LogInformation($"Starting {newname}");
                var ret = new UstarTarEntry(TarEntryType.RegularFile, newname);
                ret.DataStream = new MemoryStream();
                await DoJob(x.Item1.DataStream,x.Item2.DataStream, ret.DataStream, 
                    Path.GetFileNameWithoutExtension(x.Item1.Name));
                ret.DataStream.Seek(0, SeekOrigin.Begin);
                _logger.LogInformation($"Completed {ret.Name}");
                return ret;
            }

            switch (ProcessingMode.Mode)
            {
                case "one":
                    await RunOne();
                    break;
                case "tar" when input==null:
                    await diar.AsTarSource(breaker)
                        .ViaAsyncMapperParallel(RunTar1, ProcessingMode.Parallelism, null, breaker)
                        .RunWithSink(output.AsTarSink());
                    break;
                case "tar" when diar == null:
                    await input.AsTarSource(breaker)
                        .ViaAsyncMapperParallel(RunTar2, ProcessingMode.Parallelism, null, breaker)
                        .RunWithSink(output.AsTarSink());
                    break;
                case "tar":
                    await input.AsTarSource(breaker).ZipWith(diar.AsTarSource(breaker))
                        .ViaAsyncMapperParallel(RunTar, ProcessingMode.Parallelism, null, breaker)
                        .RunWithSink(output.AsTarSink());
                    break;
                default:
                    throw new NotImplementedException(ProcessingMode.Mode);
            };
                       
            
            output.Complete();
            return 0;
        }
    }
}

