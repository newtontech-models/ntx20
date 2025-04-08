using System.Threading;
using Microsoft.Extensions.CommandLineUtils;
using System.Threading.Tasks;
using ntx20.api.io;
using ntx20.api;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using ntx20.api.pipe;
using Google.Protobuf.Collections;
using Microsoft.Extensions.Logging;
using System;
using System.Formats.Tar;

namespace ntx20.command.util.tran.eval
{
    class Command : ICommand
    {
        private static readonly ILogger _logger = Logging.LoggerFactory.CreateLogger("ntx20.command.tool.tprint");
        internal static void Configure(CommandLineApplication command, CommandLineOptions options)
        {
            
            command.Description = "eval transcription";
            command.HelpOption("-h|--help");
            var outputUriOption = command.Option("-o|--output <->",
                "output url",
                CommandOptionType.SingleValue
                );
            var iFormatOption = command.Option($"--iformat <json>",
             "read input as tar|json",
             CommandOptionType.SingleValue
             );

            var oFormatOption = command.Option($"--oformat <json>",
             "write output as json",
             CommandOptionType.SingleValue
             );
            var keepBlocksOption = command.Option($"--keepblocks",
            "keep align blocks in evaluation",
            CommandOptionType.NoValue
            );

            var idOption = command.Option($"--id <auto>",
             "set result id",
             CommandOptionType.SingleValue
             );
            var labelOption = command.Option($"--label <none>",
             "add labels key=value",
             CommandOptionType.MultipleValue
             );


            var inputUriOption = command.Option(@"-i|--input",
            "input audio url",
            CommandOptionType.SingleValue
            );

            command.OnExecute(() =>
            {
                inputUriOption.MustSetValue(command);

                options.Command = new Command(command)
                {
                    OutputUriOption = outputUriOption.GetValueOrDefault(),
                    InputUriOption = inputUriOption.Value(),
                    IFormat = iFormatOption.GetValueOrDefault(),
                    OFormat = oFormatOption.GetValueOrDefault(),
                    KeepBlocks = keepBlocksOption.HasValue(),
                    Id = idOption.GetValueOrDefault() == "auto" ? inputUriOption.Value() : idOption.GetValueOrDefault(),
                    Labels = labelOption.GetMapOrDefault()
                };
                return 0;
            });
        }

        private string OutputUriOption { get; set; }
        private string InputUriOption { get; set; }
        private string IFormat { get; set; }
        private string OFormat { get; set; }
        private string Id { get; set; }
        private Dictionary<string,string> Labels { get; set; }
        private bool KeepBlocks { get; set; }
        private readonly CommandLineApplication _app;
        
        public Command(CommandLineApplication app)
        {
            _app = app;
        }
        public async Task<int> RunAsync(CancellationToken breaker)
        {
            using var oStream = LazyStream.Output(OutputUriOption, OFormat == "json" ? "application/json" : "application/tar", breaker, true);
            using var input = LazyStream.Input(InputUriOption, breaker);


            var pipe = (IFormat switch
            {
                "json" => input.AsProtoJsonSource<api.proto.Evaluation>(breaker),
                "tar" => input.AsTarSource(breaker).SelectMany(x=> x.DataStream.AsProtoJsonSource<api.proto.Evaluation>(CancellationToken.None)),
                _ => throw new NotImplementedException($"unsuported input format {IFormat}"),
            });

            var evaluation = await pipe.Evaluate(KeepBlocks);
            evaluation.Id = Id;
            evaluation.Labels.Add(Labels);
            await (OFormat switch
            {
                "json" => (new[] { evaluation }).ToAsyncEnumerable().RunWithSink(oStream.AsJsonProtoSink<api.proto.Evaluation>(), autoFlush: true, cancellationToken: breaker),
                _ => throw new NotImplementedException($"unsuported output format {OFormat}"),
            });

            oStream.Complete();
            return 0;
        }
    }
}
