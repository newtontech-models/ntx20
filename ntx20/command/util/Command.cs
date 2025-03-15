using Microsoft.Extensions.CommandLineUtils;
using System.Threading;
using System.Threading.Tasks;

namespace ntx20.command.util
{
    
    class Command :ICommand
    {
        internal static void Configure(CommandLineApplication command, CommandLineOptions options)
        {
            command.Description = "stream utils";
            command.Command("print", (c) => print.Command.Configure(c,options));
            command.Command("conv", (c) => conv.Command.Configure(c, options));
            command.Command("trsx", (c) => trsx.Command.Configure(c, options));

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
