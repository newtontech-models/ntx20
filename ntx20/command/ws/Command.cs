using Grpc.Core;using Grpc.Net.Client;using Microsoft.Extensions.CommandLineUtils;using Microsoft.Extensions.Logging;using ntx20.api;using ntx20.api.io;using ntx20.api.pipe;using ntx20.api.proto;using ntx20.api.utils;using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Net.Http;using System.Threading;using System.Threading.Tasks;namespace ntx20.command.ws{    class Command : ICommand    {        private static readonly ILogger _logger = Logging.LoggerFactory.CreateLogger("ntx20.command.run");
        private static Dictionary<string,string> knowAppTypes = new Dictionary<string, string>()
        { { "atran","ntx20" }, { "ppc","ntx20"  }, { "adsp","ntx20"  }, { "diar","ntx20"  }, { "vad","ntx20"  }, { "dtran","ntx20"  }};

        internal static void Configure(CommandLineApplication command, CommandLineOptions options)        {            var resourceOption = command.Argument("task", "name:version@cluster  version is optional or latest, cluster is either https://usr:psw@example.com or environment variable", false); ;            var headersOption = command.Option("--head", "add multiple grpc headers as key:value", CommandOptionType.MultipleValue);            command.Description = "run the task";            if (options.TheService == null)            {                command.OnExecute(() =>                {                    resourceOption.MustSetValue(command);                                        var arg = resourceOption.Value;                    var x = arg.Split("@", 2);                    if (x.Length < 2)                    {                        throw new Exception("Invalid format, requires name:version@cluster");                    }                    var taskname = x[0];                    var taskversion = "latest";                    if (taskname.Contains(":"))                    {                        var zz = taskname.Split(":", 2);                        taskname = zz[0];                        taskversion = zz[1];                    }                    var cluster = x[1].StartsWith("http") ? x[1] : Environment.GetEnvironmentVariable(x[1]);                    if (cluster == null || !cluster.StartsWith("http"))                    {                        throw new Exception($"Invalid cluster format, requires https://usr:psw@example.com");                    }                    var uri = new Uri(cluster);                    var appType= x[0].Split(new string[] { "/", "-",":","." }, StringSplitOptions.RemoveEmptyEntries).Intersect(knowAppTypes.Keys).FirstOrDefault();

                    if (appType == null)
                    {
                        _logger.LogWarning("Uknown apptype, using default client");
                        appType = "app";
                    }                    options.TheService = new ServiceVersion {                             Service = taskname,                             Version = taskversion,                            Labels = { {"app.type", $"{appType}" } }                    };

                    if (knowAppTypes[appType] == "ntx20")
                    {


                        var meta = new Metadata { { "Authorization", $"Basic {Convert.ToBase64String(System.Text.ASCIIEncoding.UTF8.GetBytes(uri.UserInfo))}" } };
                        if (options.TheService.Version.Length > 0 && options.TheService.Version != "latest")
                        {
                            meta.Add("service", $"{options.TheService.Service}:{options.TheService.Version}");
                        }
                        else
                        {
                            meta.Add("service", $"{options.TheService.Service}");
                        }

                        foreach (var h in headersOption.Values)
                        {
                            if (h.Contains(":"))
                            {
                                var zz = h.Split(":", 2);
                                meta.Add(zz[0], zz[1]);
                            }
                            else
                            {
                                throw new Exception($"Invalid grpc header {h}");
                            }
                        }


                        options.Channel = GrpcChannel.ForAddress(uri);
                        options.CreateStreaming = () => new EngineService.EngineServiceClient(options.Channel).Streaming(meta);
                    }                                        options.Command = new Command(command);                    return 0;                });            }            else            {                var appType = options.TheService.Labels["app.type"];                switch (appType)                {                    case "atran":                        atran.Command.Configure(command, options);                        break;                    default:                        throw new Exception($"Unsuported application type: {appType}");                }            }        }        private readonly CommandLineApplication _app;        public Command(CommandLineApplication app)        {            _app = app;        }        public async Task<int> RunAsync(CancellationToken breaker)        {            await _app.ShowHelpAsync();            return 1;        }    }}