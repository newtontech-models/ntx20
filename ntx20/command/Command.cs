using Microsoft.Extensions.CommandLineUtils;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ntx20.command
{

    class Command :ICommand
    {
        internal static void Configure(CommandLineApplication command, CommandLineOptions options)
        {
            if (Environment.GetEnvironmentVariable("NTX20_HOME_REMOTE") != null)
            {
                command.Command("app", (c) => app.Command.Configure(c, options), false);
            }
            command.Command("run", (c) =>
            {
                Authentication.ConfigureRun(c, options);
                run.Command.Configure(c, options);
            }, options.TheService != null);
            command.Command("util", (c) => util.Command.Configure(c, options));
            command.Command("ws", (c) =>
            {
                Authentication.ConfigureWs(c, options);
                ws.Command.Configure(c, options);
            }, options.TheService != null);


            command.OnExecute(() =>
            {
                options.Command = new Command(command);
                return 0;
            });
            
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
