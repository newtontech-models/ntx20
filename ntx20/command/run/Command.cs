using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging;
using ntx20.api;
using ntx20.api.io;
using ntx20.api.pipe;
using ntx20.api.proto;
using ntx20.api.utils;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ntx20.command.run
{

    class Command : ICommand
    {

        internal static void Configure(CommandLineApplication command, CommandLineOptions options)
        {


            var resourceOption = command.Argument("task", "name:version@cluster  version is optional or latest, cluster is either https://usr:psw@example.com or environment variable", false); ;
            if (options.TheService == null)
            {
                command.Description = "run the task";
                
                command.OnExecute(() =>
                {
                    resourceOption.MustSetValue(command);
                    
                    
                    
                    var arg = resourceOption.Value;

                    var x = arg.Split("@", 2);
                    if ( x.Length < 2)
                    {
                        throw new Exception("Invalid format, requires name:version@cluster");
                    }
                    var taskname = x[0];
                    var taskversion = "latest";
                    if (taskname.Contains(":"))
                    {
                        var zz = taskname.Split(":", 2);
                        taskname = zz[0];
                        taskversion = zz[1];
                    }
                    var cluster = x[1].StartsWith("http") ? x[1] : Environment.GetEnvironmentVariable(x[1]);
                    if (cluster==null || ! cluster.StartsWith("http"))
                    {
                        throw new Exception($"Invalid cluster format, requires https://usr:psw@example.com");
                    }
                    var uri = new Uri(cluster);
                    
                    using (var httpClient = new HttpClient { DefaultRequestVersion = new Version(2,0), BaseAddress = uri , DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher})
                    {
                        
                        if (uri.UserInfo.Length > 0)
                        {
                            httpClient.DefaultRequestHeaders.Add($"Authorization", $"Basic {Convert.ToBase64String(System.Text.ASCIIEncoding.UTF8.GetBytes(uri.UserInfo))}");
                        }

                        var ret = httpClient.GetStringAsync($"/services/{taskname}");
                        ret.Wait();

                        var service= new Google.Protobuf.JsonParser(Google.Protobuf.JsonParser.Settings.Default.WithIgnoreUnknownFields(true)).Parse<ntx20.api.proto.Service>(ret.Result);
                        if(taskversion=="latest")
                        {
                            options.TheService = service.Versions.First();
                            
                        }
                        else
                        {
                            options.TheService = service.Versions.First(x => x.Version == taskversion);
                        }

                    }
                    var meta = new Metadata
                    {
                        //{ "service",  $"{options.TheService.Service}"},
                        { "service",  $"{options.TheService.Service}:{options.TheService.Version}"},
                        { "Authorization",  $"Basic {Convert.ToBase64String(System.Text.ASCIIEncoding.UTF8.GetBytes(uri.UserInfo))}"},
                    };

                    options.Client = new EngineService.EngineServiceClient(GrpcChannel.ForAddress(uri));
                    options.CreateStreaming = () => options.Client.Streaming(meta);
                    options.Command = new Command(command);
                    return 0;
                });
            }
            else
            {
                var appType = options.TheService.Labels["app.type"];
                switch (appType)
                {
                    case "ntx20-atran":
                        atran.Command.Configure(command, options);
                        break;
                    case "ntx20-ppc":
                        ppc.Command.Configure(command, options);
                        break;
                    default:
                        break;
                }


            }
        }
        private readonly CommandLineApplication _app;
        public Command(CommandLineApplication app)
        {
            _app = app;
        }

        public async Task<int> RunAsync(CancellationToken breaker)
        {
            await _app.ShowHelpAsync();
            return 1;
        }
    }
}
