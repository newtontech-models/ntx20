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

namespace ntx20.command.run.atran
{

    class Command : ICommand
    {
        private static readonly ILogger _logger = Logging.LoggerFactory.CreateLogger("ntx20.command.run.atran");

        
        internal static void Configure(CommandLineApplication command, CommandLineOptions options, bool dry = false)
        {
            command.Description = $"converts audio input to text";
            command.HelpOption("-h|--help");
            if(options.TheService!=null)
                command.FullName = $"Application: {options.TheService.Service}:{options.TheService.Version}";

            var inputUriOption = command.Option(@"-i|--input",
            "one mode: input audio url | batch mode: stream with command lines for individual tasks",
            CommandOptionType.SingleValue
            );
            var outputUriOption = command.Option("-o|--output <->",
                 "output result url",
                 CommandOptionType.SingleValue
                 );

            var processingMode = command.Option("-m|--mode <one>",
                 "processing mode one|batch:xx where xx is paralelism ",
                 CommandOptionType.SingleValue
                 );

            var iFormat = command.Option($"-r|--reader <raw:4096>",
                "read input as raw:$chunkSizeBytes|proto|json",
                CommandOptionType.SingleValue
                );

            var oFormat = command.Option($"-w|--writer <json>",
                "write output as json|proto|simple|text:v2t|text:ppc|text:pnc|ntext:v2t|ntext:ppc|ntext:pnc|console:v2t|console:ppc|console:pnc",
                CommandOptionType.SingleValue
                );
            var label=command.Option($"-l| --label",
                "add client metadata to every chunk --label name:value",
                CommandOptionType.MultipleValue
                );
            var wrap = command.Option("--wrap",
                "wrap stream with open and close message",
                CommandOptionType.NoValue
                );
            var flush = command.Option("-f|--flush",
                "enable flush on every write",
                CommandOptionType.NoValue
                );
            var overwrite = command.Option("--overwrite",
                "overwrite existing output",
                CommandOptionType.NoValue
                );
            var nomkdir = command.Option("--nodirs",
                "dont create output path if not exist",
                CommandOptionType.NoValue
                );
            var retryOption = command.Option("--retry <10:1000:1.5>"
              , "retry with exponencial backoff (batch mode only) count:initDelayMs:multiplier"
             , CommandOptionType.SingleValue);

            /*input*/
            command.ExtendedHelpText = Environment.NewLine + "Options: " + Environment.NewLine;
            var audioFormatOption = command.Option($"--{Const.i_audio_format} <auto:0>",
                 $"input format auto:$probeSizeBytes|pcm:$pcmFormat:$sampleRate:$channelLayout",
                CommandOptionType.SingleValue
                );
            

            var channelOption = command.Option($"--{Const.i_audio_channel} <downmix>"
               , $"choose audio channel {string.Join("|", AudioDecoder.channelSelects)}"
              , CommandOptionType.SingleValue);

            command.ExtendedHelpText += audioFormatOption.RenderOption();
            command.ExtendedHelpText += AudioDecoder.pcmFormats.RenderEnumHelp("$pcmFormat");
            command.ExtendedHelpText += AudioDecoder.sampleRates.RenderEnumHelp("$sampleRate");
            command.ExtendedHelpText += AudioDecoder.channelLayouts.RenderEnumHelp("$channelLayout");

            command.ExtendedHelpText += channelOption.RenderOption();

            var decoderFeatures = command.Option($"--{Const.features} <none>",
                 $"features (lookahead,latency,novad,nospk,novprint,noppc,nopnc)",
                CommandOptionType.SingleValue
                );

            var pipe = command.Option("-p|--pipe",
                "run in pipe mode",
                CommandOptionType.NoValue);

            var lexiconOption = command.Option($"--{Const.lexicon} <none>",
                 $"extra lexicon to use",
                CommandOptionType.SingleValue
                );

            command.ExtendedHelpText += decoderFeatures.RenderOption();
            command.ExtendedHelpText += lexiconOption.RenderOption();

            command.OnExecute(() =>
            {

                
                inputUriOption.MustSetValue(command);

                options.Command = new Command(command, options)
                {
                    OutputUriOption = outputUriOption.GetValueOrDefault(),
                    InputUriOption = inputUriOption.GetValueOrDefault(),
                    AudioFormatOption = audioFormatOption.GetValueOrDefault(),
                    ChannelOption = channelOption.GetValueOrDefault(),
                    IFormat = iFormat.GetValueOrDefault().StartsWith("raw:") ? "raw" : iFormat.GetValueOrDefault(),
                    ChunkSizeBytes = iFormat.GetValueOrDefault().StartsWith("raw:") ? uint.Parse(iFormat.GetValueOrDefault()[4..]) : 0,
                    OFormat = oFormat.GetValueOrDefault(),
                    Flush = flush.HasValue(),
                    Pipe = pipe.HasValue(),
                    DecoderFeatures = decoderFeatures.GetValueOrDefault(),
                    LexiconUrlOption = lexiconOption.GetValueOrDefault(),
                    ProcessingMode = processingMode.GetValueOrDefault().StartsWith("batch:") ? "batch" : processingMode.GetValueOrDefault(),
                    Parallelism = processingMode.GetValueOrDefault().StartsWith("batch:") ? uint.Parse(processingMode.GetValueOrDefault()[6..]) : 1,
                    Retry = api.util.RetryWithBackoff.ParseFromCmd(retryOption.GetValueOrDefault()),
                    DontMakeDirs = nomkdir.HasValue(),
                    OverWrite = overwrite.HasValue(),
                    Wrap = wrap.HasValue(),
                    Labels = label.HasValue() ? label.Values.ToDictionary(x=>  x.Split(':', 2)[0], x=> x.Split(':', 2)[1]) : null,

                };

                return 0;
            });
        }
        private readonly CommandLineApplication _app;
        private readonly CommandLineOptions _opts;
        private string OutputUriOption { get; set; }
        private string InputUriOption { get; set; }
        private string AudioFormatOption { get; set; }
        private string ChannelOption { get; set; }
        private uint ChunkSizeBytes { get; set; }
        private string IFormat { get; set; }
        private string OFormat { get; set; }

        private string LexiconUrlOption { get; set; }
        private string DecoderFeatures { get; set; }

        private string ProcessingMode { get; set; }
        private uint Parallelism { get; set; }
        private bool Pipe { get; set; }
        
        private bool DontMakeDirs { get; set; }
        private bool OverWrite { get; set; }
        private bool Flush { get; set; }

        private bool Wrap { get; set; }

        private Dictionary<string,string> Labels { get; set; }
        private api.util.RetryWithBackoff Retry { get; set; }
        public Command(CommandLineApplication app, CommandLineOptions opts)
        {
            _app = app;
            _opts = opts;
        }
        public async Task<int> RunAsync(CancellationToken breaker)
        {
            if(ProcessingMode == "one")
                return await RunOneAsync(this, breaker);
            if (ProcessingMode != "batch")
                throw new NotImplementedException($"mode: {ProcessingMode}");

            //TODO redesign this pyramidal ubershit

            using BlockingCollection<string> bc = Parallelism== 0 ? new BlockingCollection<string>() : new BlockingCollection<string>(2*(int)Parallelism);
            using var writer = Task.Run(async () =>
            {
                using (var reader = new StreamReader(LazyStream.Input(InputUriOption, breaker)))
                {
                    while (true)
                    {
                        var line = await reader.ReadLineAsync();
                        if (line == null)
                            break;
                        if (line.Trim().StartsWith("#"))
                        {
                            continue;
                        }
                        if (line.Trim().Length == 0)
                            continue;
                        bc.Add(line.Trim());
                    }
                }
                bc.CompleteAdding();
            });

            Task[] readers = new Task[Parallelism];
            
            long total_count = 0;
            for(var i=0;i<Parallelism; i++)
            {
                _logger.LogInformation($"Starting worker {_opts.TheService.Service}:{_opts.TheService.Version}/{i}");
                readers[i] = Task.Run(async() =>
                {
                    long z = i;
                    long count = 0;
                    foreach (var task_command in bc.GetConsumingEnumerable()){
                        var app = new CommandLineApplication();
                        var opts = new CommandLineOptions();
                        Configure(app, opts, true);
                        var args = CommandLineOptions.SplitAsCmdArguments(task_command);
                        app.Execute(args);
                        var task_params = opts.Command as Command;
                        var _retry = Retry.Clone();
                        while (true)
                        {
                            try
                            {
                                await RunOneAsync(task_params, breaker);
                                break;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning($"Failed: {ex.Message} => {task_params.InputUriOption}");
                                if (breaker.IsCancellationRequested)
                                    throw;
                                if (await _retry.Next(breaker))
                                    throw;
                                _logger.LogWarning($"Retry: #{_retry.Retry}/{_retry.MaxRetries} => {task_params.InputUriOption}");
                            }
                        }
                        Interlocked.Increment(ref total_count);
                        count++;
                        _logger.LogInformation($"Completed: {z}/{count}/{total_count} => {task_params.InputUriOption}");
                    }
                    _logger.LogInformation($"Finishing worker {_opts.TheService.Service}:{_opts.TheService.Version}/{z}");
                });
            }

            await Task.WhenAll(readers);

            return readers.Where(x => x.IsFaulted).Count();

        }
        public async Task<int> RunOneAsync(Command param,  CancellationToken breaker)
        {

            
            
            using var call = _opts.CreateStreaming();

            bool isFile = param.OutputUriOption != "-";

            using var input = LazyStream.Input(param.InputUriOption, breaker);
            if(isFile && File.Exists(param.OutputUriOption))
            {
                if (!OverWrite)
                {
                    _logger.LogWarning($"Allready exists, skipping {param.OutputUriOption}");
                    return 0;
                }

                
            }
            using var output = LazyStream.Output(param.OutputUriOption, "binary", breaker,!DontMakeDirs);

            var configuration = new api.proto.Payload
            {
                Chunk =
                    {
                        new api.proto.Item{ Key = Const.i_audio_format, S = param.AudioFormatOption, Type = "s" },
                        new api.proto.Item { Key = Const.i_audio_channel, S = param.ChannelOption, Type = "s" },
                        new api.proto.Item { Key = Const.features, S = param.DecoderFeatures, Type = "s" },
                        CmdUtils.LexiconFromUrl(param.LexiconUrlOption)
                    }
            };




            var configured = await call.Configure(configuration, breaker);
            var accepts = configured.Chunk.First(x => x.Key == "accepts").Tags.ToArray();

            if(param._opts.TheService!=null)
                _logger.LogInformation($"Task {param._opts.TheService.Service}:{param._opts.TheService.Version} configured");

            var pipe = (param.IFormat switch
            {
                "raw" => input.AsRawAudioSource(chunkSize: (int)ChunkSizeBytes, cancellationToken: breaker),
                "proto" => input.AsProtoBinarySource<api.proto.Payload>(breaker),
                "json" => input.AsProtoJsonSource<api.proto.Payload>(breaker),
                _ => throw new NotImplementedException($"unsuported input format {param.IFormat}"),
            });


            pipe = pipe.ViaTaskRunner(call, accepts, Pipe);
            if (param.Wrap || Wrap)
                pipe = pipe.ViaOCWrapper();

            if (param.Labels != null)
                pipe = pipe.ViaClientMetaInjector(param.Labels);

            await (param.OFormat switch
            {
                "proto" => pipe.RunWithSink(output.AsBinaryProtoSink<api.proto.Payload>(), autoFlush: param.Flush, cancellationToken: breaker),
                "json" => pipe.RunWithSink(output.AsJsonProtoSink<api.proto.Payload>(), autoFlush: param.Flush, cancellationToken: breaker),
                "simple" => pipe.ToSimpleText().RunWithSink(output.AsTextChunkSink(), autoFlush: param.Flush, cancellationToken: breaker),
                "text:v2t" => pipe.ToText("v2t").RunWithSink(output.AsTextChunkSink(), autoFlush: param.Flush, cancellationToken: breaker),
                "text:ppc" => pipe.ToText("ppc").RunWithSink(output.AsTextChunkSink(), autoFlush: param.Flush, cancellationToken: breaker),
                "text:pnc" => pipe.ToText("pnc").RunWithSink(output.AsTextChunkSink(), autoFlush: param.Flush, cancellationToken: breaker),
                "ntext:v2t" => pipe.ToNText("v2t").RunWithSink(output.AsTextChunkSink(), autoFlush: param.Flush, cancellationToken: breaker),
                "ntext:ppc" => pipe.ToNText("ppc").RunWithSink(output.AsTextChunkSink(), autoFlush: param.Flush, cancellationToken: breaker),
                "ntext:pnc" => pipe.ToNText("pnc").RunWithSink(output.AsTextChunkSink(), autoFlush: param.Flush, cancellationToken: breaker),
                "console:v2t" => pipe.RunWithSink(Sink.ConsolePayloadSink("v2t"), autoFlush: param.Flush, cancellationToken: breaker),
                "console:ppc" => pipe.RunWithSink(Sink.ConsolePayloadSink("ppc"), autoFlush: param.Flush, cancellationToken: breaker),
                "console:pnc" => pipe.RunWithSink(Sink.ConsolePayloadSink("pnc"), autoFlush: param.Flush, cancellationToken: breaker),
                _ => throw new NotImplementedException($"unsuported output format {param.OFormat}"),
            });
            output.Complete();

            if (param._opts.TheService != null)
                _logger.LogInformation($"Task {param._opts.TheService.Service}:{param._opts.TheService.Version} completed");
            return 0;
        }


    }


}
