using Microsoft.Extensions.CommandLineUtils;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ntx20.command.auth
{
    
    class Command :ICommand
    {
        internal static void Configure(CommandLineApplication command, CommandLineOptions options)
        {

            command.Description = "auth tools";
            
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
