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

            var iFormat = command.Option($"--iformat <json>",
              "read input as json|txt|trsx",
              CommandOptionType.SingleValue
              );

            var rFormat = command.Option($"--rformat <json>",
              "read input as json|txt|trsx",
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
            var oFormat = command.Option($"-w|--writer <json>",
                "write output as json|html",
                CommandOptionType.SingleValue
                );


            command.OnExecute(() =>
            {
                inputUriOption.MustSetValue(command);
                options.Command = new Command(command)
                {
                    OutputUriOption = outputUriOption.GetValueOrDefault(),
                    InputUriOption = inputUriOption.Value(),
                    RefUriOption = refUriOption.Value(),
                    OFormat = oFormat.GetValueOrDefault(),
                    IFormat = iFormat.GetValueOrDefault(),
                    RFormat = rFormat.GetValueOrDefault(),
                    Flush = flush.HasValue(),
                    SplitOption = splitOption.GetValueOrDefault()=="default" ? Constants.DefaultTextSplit : splitOption.GetValueOrDefault(),
                    PlusOption = plusOption.GetValueOrDefault() == "default" ? Constants.DefaultPlusMatch : plusOption.GetValueOrDefault(),
                };
                return 0;
            });
        }

        private string OutputUriOption { get; set; }
        private string InputUriOption { get; set; }
        private string RefUriOption { get; set; }
        private string TrackOption { get; set; }
        private string OFormat { get; set; }
        private string IFormat { get; set; }
        private string RFormat { get; set; }
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

            using var output = LazyStream.Output(OutputUriOption, "application/json");
            using var input = LazyStream.Input(InputUriOption, breaker);
            using var reference = LazyStream.Input(RefUriOption, breaker);


            var pipe = (IFormat switch
            {
                "json" => input.AsProtoJsonSource<api.proto.Payload>(breaker),
                "trsx" => input.AsTrsxTranSource(breaker),
                _ => throw new NotImplementedException($"unsuported input format {IFormat}"),
            });

            var rpipe = (RFormat switch
            {
                "json" => reference.AsProtoJsonSource<api.proto.Payload>(breaker),
                "trsx" => reference.AsTrsxTranSource(breaker),
                _ => throw new NotImplementedException($"unsuported reference format {IFormat}"),
            });

            pipe = pipe.AlignWith(rpipe, SplitOption, PlusOption);

            await (OFormat switch
            {
                "json" => pipe.RunWithSink(output.AsJsonProtoSink<api.proto.Payload>(), autoFlush: Flush, cancellationToken: breaker),
                _ => throw new NotImplementedException($"unsuported output format {OFormat}"),
            });
            output.Complete();
            return 0;
        }
    }
}

