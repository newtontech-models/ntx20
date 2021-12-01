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

namespace ntx20.command.util.conv.fromlegacy
{
    class Command : ICommand
    {
        private static readonly ILogger _logger = Logging.LoggerFactory.CreateLogger("ntx20.command.tool.conv.fromlegacy");
        internal static void Configure(CommandLineApplication command, CommandLineOptions options)
        {
            
            command.Description = "converts from legacy format";
            command.HelpOption("-h|--help");
            var outputUriOption = command.Option("-o|--output <->",
                "output proto url",
                CommandOptionType.SingleValue
                );
            var inputUriOption = command.Option(@"-i|--input",
            "input json url",
            CommandOptionType.SingleValue
            );

            var flush = command.Option("-f|--flush",
                "enable flush on every write",
                CommandOptionType.NoValue
                );
            var trackOption = command.Option(@"-t|--track",
            "set output track id",
            CommandOptionType.SingleValue
            );

            command.OnExecute(() =>
            {
                inputUriOption.MustSetValue(command);
                trackOption.MustSetValue(command);
                options.Command = new Command(command)
                {
                    OutputUriOption = outputUriOption.GetValueOrDefault(),
                    InputUriOption = inputUriOption.Value(),
                    Flush = flush.HasValue(),
                    TrackOption = trackOption.Value()

                };
                return 0;
            });
        }

        private string OutputUriOption { get; set; }
        private string InputUriOption { get; set; }
        private string TrackOption { get; set; }
        private bool Flush { get; set; }
        private readonly CommandLineApplication _app;
        
        public Command(CommandLineApplication app)
        {
            _app = app;
        }
        public async Task<int> RunAsync(CancellationToken breaker)
        {
            using var output = LazyStream.Output(OutputUriOption, "binary", breaker);
            using var input = LazyStream.Input(InputUriOption, breaker);
            await input.AsProtoJsonSource<api.proto.legacy.v2t.engine.Events>(breaker).ViaMapper(x => x.ToV2(TrackOption)).RunWithSink(output.AsJsonProtoSink<api.proto.Payload>(), autoFlush: Flush, cancellationToken: breaker);
            return 0;
        }
    }
}
