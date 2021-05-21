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

namespace ntx20.command.stream.proto2json
{
    class Command : ICommand
    {
        private static readonly ILogger _logger = Logging.LoggerFactory.CreateLogger("ntx20.command.tool.proto2json");
        internal static void Configure(CommandLineApplication command, CommandLineOptions options)
        {

            command.Description = "converts proto to json";
            command.HelpOption("-h|--help");
            var outputUriOption = command.Option("-o|--output <->",
                "output json url",
                CommandOptionType.SingleValue
                );
            var inputUriOption = command.Option(@"-i|--input",
            "input proto url",
            CommandOptionType.SingleValue
            );

            var flush = command.Option("-f|--flush",
                "enable flush on every write",
                CommandOptionType.NoValue
                );

            command.OnExecute(() =>
            {
                inputUriOption.MustSetValue(command);
                options.Command = new Command(command)
                {
                    OutputUriOption = outputUriOption.GetValueOrDefault(),
                    InputUriOption = inputUriOption.Value(),
                    Flush = flush.HasValue(),

                };
                return 0;
            });
        }

        private string OutputUriOption { get; set; }
        private string InputUriOption { get; set; }
        private bool Flush { get; set; }
        private readonly CommandLineApplication _app;

        public Command(CommandLineApplication app)
        {
            _app = app;
        }
        public async Task<int> RunAsync(CancellationToken breaker)
        {
            using var output = LazyStream.Output(OutputUriOption, "text", breaker);
            using var input = LazyStream.Input(InputUriOption, breaker);

            await input.AsProtoBinarySource<api.proto.Payload>(breaker).RunWithSink(output.AsJsonProtoSink<api.proto.Payload>(), autoFlush: Flush, cancellationToken: breaker);
            return 0;
        }
    }
}
