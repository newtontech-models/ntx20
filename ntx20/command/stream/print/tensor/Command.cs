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

namespace ntx20.command.stream.print.tensor
{
    class Command : ICommand
    {
        private static readonly ILogger _logger = Logging.LoggerFactory.CreateLogger("ntx20.command.tool.tprint");
        internal static void Configure(CommandLineApplication command, CommandLineOptions options)
        {
            
            command.Description = "print tensor";
            command.HelpOption("-h|--help");
            var outputUriOption = command.Option("-o|--output <->",
                "output url",
                CommandOptionType.SingleValue
                );
            var iFormatOption = command.Option($"-r|--reader <htk>",
             "read input as htk|proto|json",
             CommandOptionType.SingleValue
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
                    Iformat = iFormatOption.GetValueOrDefault()

                };
                return 0;
            });
        }

        private string OutputUriOption { get; set; }
        private string InputUriOption { get; set; }
        private string Iformat { get; set; }

        private readonly CommandLineApplication _app;
        
        public Command(CommandLineApplication app)
        {
            _app = app;
        }
        public async Task<int> RunAsync(CancellationToken breaker)
        {
            using var output = LazyStream.Output(OutputUriOption, "text", breaker);
            using var input = LazyStream.Input(InputUriOption, breaker);

            var pipe = (Iformat switch
            {
                "htk" => input.AsHtkTensorStreamSource(cancellationToken: breaker),
                "proto" => input.AsProtoBinarySource<api.proto.Payload>(breaker),
                "json" => input.AsProtoJsonSource<api.proto.Payload>(breaker),
                _ => throw new NotImplementedException($"unsuported input format {Iformat}"),
            });


            await pipe.PrintTensor().RunWithSink(output.AsRawChunkSink(), autoFlush: true);
            return 0;
        }
    }
}
