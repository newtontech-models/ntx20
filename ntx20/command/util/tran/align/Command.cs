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
using System.Security.Cryptography.Xml;
using static Grpc.Core.Metadata;
using System.Formats.Tar;

namespace ntx20.command.util.tran.align
{
    class Command : ICommand
    {
        private static readonly ILogger _logger = Logging.LoggerFactory.CreateLogger("ntx20.command.util.tran.eval.one");

        internal static void Configure(CommandLineApplication command, CommandLineOptions options)
        {

            command.Description = "eval one";
            command.HelpOption("-h|--help");
            command.ExtendedHelpText =
             Environment.NewLine
             + $"default split: {Constants.DefaultTextSplit}" + Environment.NewLine
             + $"default match: {Constants.DefaultPlusMatch}" + Environment.NewLine;

            var outputUriOption = command.Option("-o|--output <->",
                "output proto url",
                CommandOptionType.SingleValue
                );
            var inputUriOption = command.Option(@"-i|--input",
            "json url",
            CommandOptionType.SingleValue
            );

            var iFormat = command.Option($"--iformat <auto>",
              "read input as json|text|trsx",
              CommandOptionType.SingleValue
              );

            var rFormat = command.Option($"--rformat <auto>",
              "read input as json|text|trsx",
              CommandOptionType.SingleValue
              );


            var splitOption = command.Option($"-s|--split <default>",
                "split regex",
                CommandOptionType.SingleValue
                );

            var plusOption = command.Option($"-p|--plus <default>",
                "match plus items",
                CommandOptionType.SingleValue
                );

            var refUriOption = command.Option(@"-r|--reference",
            "reference url",
            CommandOptionType.SingleValue
            );

            var flush = command.Option("-f|--flush",
                "enable flush on every write",
                CommandOptionType.NoValue
                );
            var oFormat = command.Option($"--oformat <json>",
                "write output as json|html",
                CommandOptionType.SingleValue
                );

            var processingMode = command.Option("-m|--mode <one>",
               "processing mode one|tar:xx where xx is paralelism ",
               CommandOptionType.SingleValue
               );

            command.OnExecute(() =>
            {
                inputUriOption.MustSetValue(command);
                refUriOption.MustSetValue(command);
                options.Command = new Command(command)
                {
                    OutputUriOption = outputUriOption.GetValueOrDefault(),
                    InputUriOption = inputUriOption.Value(),
                    RefUriOption = refUriOption.Value(),
                    OFormat = oFormat.GetValueOrDefault(),
                    IFormatGlobal = CmdFormat.FromSuffix(iFormat.GetValueOrDefault(), inputUriOption.GetValueOrDefault()),
                    RFormatGlobal = CmdFormat.FromSuffix(rFormat.GetValueOrDefault(), refUriOption.GetValueOrDefault()),
                    Flush = flush.HasValue(),
                    SplitOption = splitOption.GetValueOrDefault()=="default" ? Constants.DefaultTextSplit : splitOption.GetValueOrDefault(),
                    PlusOption = plusOption.GetValueOrDefault() == "default" ? Constants.DefaultPlusMatch : plusOption.GetValueOrDefault(),
                    ProcessingMode = CmdProcessingMode.Parse(processingMode.GetValueOrDefault()),
                };
                return 0;
            });
        }

        private CmdProcessingMode ProcessingMode { get; set; }
        private string OutputUriOption { get; set; }
        private string InputUriOption { get; set; }
        private string RefUriOption { get; set; }
        private string OFormat { get; set; }
        private CmdFormat IFormatGlobal { get; set; }
        private CmdFormat RFormatGlobal { get; set; }
        private string SplitOption { get; set; }
        private string PlusOption { get; set; }
        private bool Flush { get; set; }
        private readonly CommandLineApplication _app;

        public Command(CommandLineApplication app)
        {
            _app = app;
        }
        public async Task<int> RunAsync(CancellationToken breaker)
        {

            using var output = LazyStream.Output(OutputUriOption, 
                ProcessingMode.Mode == "one" ? "application/json" : "application/tar", breaker, true);
            using var input = LazyStream.Input(InputUriOption, breaker);
            using var reference = LazyStream.Input(RefUriOption, breaker);


            async Task DoJob(Stream istream, Stream rstream, Stream oStream, CmdFormat IFormat,CmdFormat RFormat, string id)
            {
                var pipe = (IFormat.Format switch
                {
                    "json" => istream.AsProtoJsonSource<api.proto.Payload>(breaker)
                    .RemoveItem(x => x.Tags.Intersect(["la", "noise"]).Count()!=0),
                    "trsx" => istream.AsTrsxTranSource(breaker),
                    "text" => istream.AsTextChunkSource(breaker).ToTranStream(),
                    _ => throw new NotImplementedException($"unsuported input format {IFormat}"),
                });
                var rpipe = (RFormat.Format switch
                {
                    "json" => rstream.AsProtoJsonSource<api.proto.Payload>(breaker)
                    .RemoveItem(x => x.Tags.Intersect(["+", "noise"]).Count() != 0),
                    "trsx" => rstream.AsTrsxTranSource(breaker),
                    "text" => rstream.AsTextChunkSource(breaker).ToTranStream(),
                    _ => throw new NotImplementedException($"unsuported reference format {RFormat}"),
                });
                var alignment = await pipe.AlignWith(rpipe, SplitOption, PlusOption);
                alignment.Id = id;
                await (OFormat switch
                {
                    "json" => (new[] { alignment }).ToAsyncEnumerable().RunWithSink(oStream.AsJsonProtoSink<api.proto.Evaluation>(), autoFlush: Flush, cancellationToken: breaker),
                    "html" => alignment.ToHtmlStrings().ToAsyncEnumerable().RunWithSink(oStream.AsTextChunkSink(), autoFlush: Flush, cancellationToken: breaker),
                    _ => throw new NotImplementedException($"unsuported output format {OFormat}"),
                });
            }
            async Task RunOne()
            {
                _logger.LogInformation($"Starting {OutputUriOption}");
                var id = Path.ChangeExtension(OutputUriOption, "");
                await DoJob(input, reference, output, IFormatGlobal,RFormatGlobal,id);
                _logger.LogInformation($"Completed {OutputUriOption}");
            }

            async Task<TarEntry> RunTar(Tuple<TarEntry, TarEntry> x)
            {
                var newname = Path.ChangeExtension(x.Item1.Name, OFormat.Replace(':', '-'));
                var id = Path.ChangeExtension(newname, "");
                _logger.LogInformation($"Starting {newname}");
                var ret = new UstarTarEntry(TarEntryType.RegularFile, newname);
                ret.DataStream = new MemoryStream();
                await DoJob(x.Item1.DataStream, x.Item2.DataStream, ret.DataStream, 
                    IFormatGlobal.ForPath(x.Item1.Name),
                    RFormatGlobal.ForPath(x.Item2.Name),
                    id
                    );
                ret.DataStream.Seek(0, SeekOrigin.Begin);
                _logger.LogInformation($"Completed {ret.Name}");
                return ret;
            }

            await (ProcessingMode.Mode switch
            {
                "one" => RunOne(),
                "tar" => input.AsTarSource(breaker).ZipWith(reference.AsTarSource(breaker))
                        .ViaAsyncMapperParallel(RunTar, ProcessingMode.Parallelism, null, breaker)
                        .RunWithSink(output.AsTarSink()),
                _ => throw new NotImplementedException(ProcessingMode.Mode)
            });
            output.Complete();
            return 0;
        }
    }
}

