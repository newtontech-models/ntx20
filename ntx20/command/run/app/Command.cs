using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging;
using ntx20.api;
using ntx20.api.io;
using ntx20.api.pipe;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ntx20.api.utils;
using System.Text.RegularExpressions;

namespace ntx20.command.run.app
{

    class Command : ICommand
    {
        private static readonly ILogger _logger = Logging.LoggerFactory.CreateLogger("ntx20.command.run.base");

        internal static void Configure(CommandLineApplication command, CommandLineOptions options)
        {
            command.Description = $"base processing";
            command.HelpOption("-h|--help");
            command.FullName = $"Application: {options.TheService.Service}:{options.TheService.Version}";

            var inputUriOption = command.Option(@"-i|--input",
            "input url",
            CommandOptionType.SingleValue
            );
            var outputUriOption = command.Option("-o|--output <->",
                 "output url",
                 CommandOptionType.SingleValue
                 );

            var iFormat = command.Option($"-r|--reader <json>",
                "read input as proto|json",
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
            var pipe = command.Option("-p|--pipe",
                "run in pipe mode",
                CommandOptionType.NoValue
            );


            /*input*/
            //command.ExtendedHelpText = Environment.NewLine + command.Description + " options: " + Environment.NewLine;

            var Features = command.Option($"--{Const.features} <none>",
                 $"enable features",
                CommandOptionType.SingleValue
                );
            //command.ExtendedHelpText += ppcFeatures.RenderOption();

            command.OnExecute(() =>
            {

                inputUriOption.MustSetValue(command);

                options.Command = new Command(command, options)
                {
                    OutputUriOption = outputUriOption.GetValueOrDefault(),
                    InputUriOption = inputUriOption.GetValueOrDefault(),
                    IFormat = iFormat.GetValueOrDefault(),
                    OFormat = oFormat.GetValueOrDefault(),
                    Flush = flush.HasValue(),
                    Pipe = pipe.HasValue(),
                    Features = Features.GetValueOrDefault(),
                };

                return 0;
            });
        }
        private readonly CommandLineApplication _app;
        private readonly CommandLineOptions _opts;

        private string OutputUriOption { get; set; }
        private string InputUriOption { get; set; }
        private string IFormat { get; set; }
        private string OFormat { get; set; }

        private string Features { get; set; }

        private bool Flush { get; set; }
        private bool Pipe { get; set; }
        public Command(CommandLineApplication app, CommandLineOptions opt)
        {
            _app = app;
            _opts = opt;
        }
        public async Task<int> RunAsync(CancellationToken breaker)
        {

            
            using var input = LazyStream.Input(InputUriOption, breaker);
            using var output = LazyStream.Output(OutputUriOption, "binary", breaker);

            var configuration = new api.proto.Payload
            {
                Chunk =
                {
                    new api.proto.Item { Key = Const.features, S = Features, Type = "s" }
                }
            };


            using var call = _opts.CreateStreaming();


           

            var configured = await call.Configure(configuration, breaker);
            var accepts = configured.Chunk.First(x => x.Key == "accepts").Tags.ToArray();
            
            _logger.LogInformation($"Task {_opts.TheService.Service}:{_opts.TheService.Version} configured");


            var pipe = (IFormat switch
            {
                "proto" => input.AsProtoBinarySource<api.proto.Payload>(breaker),
                "json" => input.AsProtoJsonSource<api.proto.Payload>(breaker),
                _ => throw new NotImplementedException($"unsuported input format {IFormat}"),
            });


            pipe=pipe.ViaTaskRunner(call, accepts, Pipe);

            await (OFormat switch
            {
                "proto" => pipe.RunWithSink(output.AsBinaryProtoSink<api.proto.Payload>(), autoFlush: Flush, cancellationToken: breaker),
                "json" => pipe.RunWithSink(output.AsJsonProtoSink<api.proto.Payload>(), autoFlush: Flush, cancellationToken: breaker),
                _ => throw new NotImplementedException($"unsuported output format {OFormat}"),
            });
            output.Complete();
            _logger.LogInformation($"Task  {_opts.TheService.Service}:{_opts.TheService.Version} completed");
            return 0;
        }


    }


}
