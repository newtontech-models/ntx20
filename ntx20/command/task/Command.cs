using Microsoft.Extensions.CommandLineUtils;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ntx20.command.task
{
    
    class Command :ICommand
    {
        internal static void Configure(CommandLineApplication command, CommandLineOptions options)
        {

            command.Description = "task tools";
            command.Command("run", (c) => run.Command.Configure(c, options), false);
            
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
