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

namespace ntx20.command.run.ppc
{

    class Command : ICommand
    {
        private static readonly ILogger _logger = Logging.LoggerFactory.CreateLogger("ntx20.command.run.v2t");

        internal static void Configure(CommandLineApplication command, CommandLineOptions options)
        {
            command.Description = $"text postprocessing";
            command.HelpOption("-h|--help");
            command.FullName = $"Application: {options.TheService.Service}:{options.TheService.Version}";

            var inputUriOption = command.Option(@"-i|--input",
            "input audio url",
            CommandOptionType.SingleValue
            );
            var outputUriOption = command.Option("-o|--output <->",
                 "output features url",
                 CommandOptionType.SingleValue
                 );

            var iFormat = command.Option($"-r|--reader <json>",
                "read input as proto|json|text",
                CommandOptionType.SingleValue
                );

            var oFormat = command.Option($"-w|--writer <json>",
                "write output as json|proto|text",
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

            var splitOption = command.Option($"-s|--split <default>",
               $"txt reader's split regex, default={Constants.DefaultTextSplit}",
               CommandOptionType.SingleValue
               );

            var plusOption = command.Option(@"-p|--plus <default>",
                $"txt reader's match plus items, default={Constants.DefaultPlusMatch}",
                CommandOptionType.SingleValue
                );
            var newLineOption = command.Option(@"-n|--newline <eof>",
                "txt reader's replace newline with",
                CommandOptionType.SingleValue
                );
            


            /*input*/
            //command.ExtendedHelpText = Environment.NewLine + command.Description + " options: " + Environment.NewLine;

            var ppcFeatures = command.Option($"--{Const.features} <none>",
                 $"enable features (lookahead,latency,noppc)",
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
                    IFormat = iFormat.GetValueOrDefault().StartsWith("raw:") ? "raw" : iFormat.GetValueOrDefault(),
                    ChunkSizeBytes = iFormat.GetValueOrDefault().StartsWith("raw:") ? uint.Parse(iFormat.GetValueOrDefault()[4..]) : 0,
                    OFormat = oFormat.GetValueOrDefault(),
                    Flush = flush.HasValue(),
                    Pipe = pipe.HasValue(),
                    PPCFeatures = ppcFeatures.GetValueOrDefault(),
                    SplitOption = splitOption.GetValueOrDefault(),
                    PlusOption = plusOption.GetValueOrDefault(),
                    NewLineOption = newLineOption.GetValueOrDefault() == "space" ? " " : newLineOption.GetValueOrDefault(),
                };

                return 0;
            });
        }
        private readonly CommandLineApplication _app;
        private readonly CommandLineOptions _opts;

        private string SplitOption { get; set; }
        private string NewLineOption { get; set; }
        private string PlusOption { get; set; }

        private string OutputUriOption { get; set; }
        private string InputUriOption { get; set; }
        private uint ChunkSizeBytes { get; set; }
        private string IFormat { get; set; }
        private string OFormat { get; set; }

        private string PPCFeatures { get; set; }

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
            Regex split = new Regex(SplitOption == "default" ? Constants.DefaultTextSplit : SplitOption);
            Regex plus = new Regex(PlusOption == "default" ? Constants.DefaultPlusMatch : PlusOption);

            var configuration = new api.proto.Payload
            {
                Chunk =
                {
                    new api.proto.Item { Key = Const.features, S = PPCFeatures, Type = "s" }
                }
            };

            api.proto.Payload txt2v2t(string x)
            {
                var r = new api.proto.Payload
                {
                    Track = "v2t",
                };
                if(NewLineOption != "eof")
                    x += NewLineOption;

                foreach (var s in split.Split(x))
                {
                    if (s == "")
                        continue;
                    var z = new api.proto.Item { Key = "txt", Type = "s", S = s };
                    if (plus.IsMatch(s))
                        z.Tags.Add("+");
                    r.Chunk.Add(z);
                }

                return r;
            }

            async Task<api.proto.Payload> rewrite(api.proto.Payload x)
            {
                using var call = _opts.CreateStreaming();
                var configured = await call.Configure(configuration, breaker);
                var accepts = configured.Chunk.First(x => x.Key == "accepts").Tags.ToArray();
                var p = new List<api.proto.Payload> { x }
                .AsProtoSource()
                .ViaTaskRunner(call, accepts, false)
                .RemoveItem(x => (x.Tags.Contains("la")));
                var ret = new api.proto.Payload { Track = "ppc" };

                await foreach(var pp in p)
                {
                    ret.Chunk.AddRange(pp.Chunk);
                }
                ret.Chunk.Add(new api.proto.Item { Key = "txt", Type = "s", S = "\n" });
                return ret;

            }
            if (IFormat == "text" && OFormat =="text" && NewLineOption == "eof")
            {
                _logger.LogInformation("Using line by line mode");
                await input.AsTextChunkSource(breaker).ViaMapper(txt2v2t).ViaAsyncMapper(rewrite).RunWithSink(output.AsRawChunkSink(), autoFlush: Flush, cancellationToken: breaker);
                return 0;
            }












            using var call = _opts.CreateStreaming();


           

            var configured = await call.Configure(configuration, breaker);
            var accepts = configured.Chunk.First(x => x.Key == "accepts").Tags.ToArray();
            
            _logger.LogInformation($"Task {_opts.TheService.Service}:{_opts.TheService.Version} configured");


           

            var pipe = (IFormat switch
            {
                "proto" => input.AsProtoBinarySource<api.proto.Payload>(breaker),
                "json" => input.AsProtoJsonSource<api.proto.Payload>(breaker),
                "text" => input.AsTextChunkSource(breaker).ViaMapper(txt2v2t),
                _ => throw new NotImplementedException($"unsuported input format {IFormat}"),
            });


            pipe=pipe.ViaTaskRunner(call, accepts, Pipe);

            await (OFormat switch
            {
                "proto" => pipe.RunWithSink(output.AsBinaryProtoSink<api.proto.Payload>(), autoFlush: Flush, cancellationToken: breaker),
                "json" => pipe.RunWithSink(output.AsJsonProtoSink<api.proto.Payload>(), autoFlush: Flush, cancellationToken: breaker),
                "text" => pipe.RemoveItem(x=> (x.Tags.Contains("la"))).RunWithSink(output.AsRawChunkSink(), autoFlush: Flush, cancellationToken: breaker),
                _ => throw new NotImplementedException($"unsuported output format {OFormat}"),
            });

            _logger.LogInformation($"Task  {_opts.TheService.Service}:{_opts.TheService.Version} completed");
            return 0;
        }


    }


}
