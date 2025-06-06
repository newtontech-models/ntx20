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
using System.IO;
using System.Collections.Concurrent;
using System.Formats.Tar;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace ntx20.command.ws.atran
{

    class Command : ICommand
    {
        private static readonly ILogger _logger = Logging.LoggerFactory.CreateLogger("ntx20.command.ws.atran");


        internal static void Configure(CommandLineApplication command, CommandLineOptions options, bool dry = false)
        {
            command.Description = $"converts audio input to text";
            command.HelpOption("-h|--help");
            if (options.TheService != null)
                command.FullName = $"Application: {options.TheService.Service}:{options.TheService.Version}";

            var urlOption = command.Option(@"-l|--listen <http://127.0.0.1:8080>",
            "listen url",
                CommandOptionType.MultipleValue
            );

            var hostOption = command.Option(@"-h|--host <ws://127.0.0.1:8080>",
           "host",
               CommandOptionType.SingleValue
           );

            var oFormat = command.Option($"-w|--writer <console:pnc>",
                "write output as console:v2t|console:ppc|console:pnc",
                CommandOptionType.SingleValue
                );


            var decoderFeatures = command.Option($"--{Const.features} <none>",
                 $"features (lookahead,latency,novad,nospk,novprint,noppc,nopnc)",
                CommandOptionType.SingleValue
                );


            var lexiconOption = command.Option($"--{Const.lexicon} <none>",
                 $"extra lexicon to use",
                CommandOptionType.SingleValue
                );

            command.ExtendedHelpText += decoderFeatures.RenderOption();
            command.ExtendedHelpText += lexiconOption.RenderOption();

            command.OnExecute(() =>
            {

                options.Command = new Command(command, options)
                {
                    OFormat = oFormat.GetValueOrDefault(),
                    DecoderFeatures = decoderFeatures.GetValueOrDefault(),
                    LexiconUrlOption = lexiconOption.GetValueOrDefault(),
                    Urls = urlOption.Values.Count == 0 ? new List<string> {
                        urlOption.GetValueOrDefault()
                    } : urlOption.Values,
                    Host = hostOption.GetValueOrDefault()
                };

                return 0;
            });
        }
        private readonly CommandLineApplication _app;
        private readonly CommandLineOptions _opts;

        private List<string> Urls;
        private string Host;
        private string AudioFormatOption = "auto:0";
        private string ChannelOption = "downmix";
        private string OFormat { get; set; }
        private string LexiconUrlOption { get; set; }
        private string DecoderFeatures { get; set; }

        public Command(CommandLineApplication app, CommandLineOptions opts)
        {
            _app = app;
            _opts = opts;
        }
        public async Task<int> RunAsync(CancellationToken breaker)
        {

            var configuration = new api.proto.Payload
            {
                Chunk =
                    {
                        new api.proto.Item{ Key = Const.i_audio_format, S = AudioFormatOption, Type = "s" },
                        new api.proto.Item { Key = Const.i_audio_channel, S = ChannelOption, Type = "s" },
                        new api.proto.Item { Key = Const.features, S = DecoderFeatures, Type = "s" },
                        CmdUtils.LexiconFromUrl(LexiconUrlOption)
                    }
            };

            
            var page = new StreamReader(
                typeof(Program).GetTypeInfo().Assembly.GetManifestResourceStream("ntx20.command.ws.atran.atran.html"),
                System.Text.Encoding.UTF8
                ).ReadToEnd().Replace("%HOST%", $"{Host}");
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls(Urls.ToArray());
            var app = builder.Build();
            app.UseWebSockets();

            app.MapGet("/", () =>
            {
                typeof(Program).GetTypeInfo().Assembly.GetManifestResourceStream("ntx20.command.ws.atran.atran.html");
                return Results.Content(page, "text/html");
            });

            app.Map("/audio", async context =>
            {
                if (!context.WebSockets.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 400;
                    return;
                }
                using var ws = await context.WebSockets.AcceptWebSocketAsync();
                using var call = _opts.CreateStreaming();
                var configured = await call.Configure(configuration, breaker);
                var accepts = configured.Chunk.First(x => x.Key == "accepts").Tags.ToArray();
                _logger.LogInformation($"Connected to grpc service");
                var pipe = ws.AsRawAudioSource(breaker)
                            .ViaTaskRunner(call, accepts, false);

                await (OFormat switch
                {
                    "console:v2t" => pipe.RunWithSink(ws.AsConsolePayloadSink("v2t")),
                    "console:ppc" => pipe.RunWithSink(ws.AsConsolePayloadSink("ppc")),
                    "console:pnc" => pipe.RunWithSink(ws.AsConsolePayloadSink("pnc")),
                    _ => throw new NotImplementedException($"unsuported output format {OFormat}"),
                });
                _logger.LogInformation($"Disconnected from grpc service");
            });
            await app.StartAsync();
            _logger.LogInformation($"Listening on {string.Join("", Urls)}");
            await app.WaitForShutdownAsync(breaker);
            _logger.LogInformation($"Completed");
            return 0;
        }

    }
}
