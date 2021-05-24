using Serilog.Core;
using System;
using System.Linq;
using Serilog.Events;
using Serilog;
using System.Text;
using System.Globalization;
using System.Threading;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Google.Protobuf.WellKnownTypes;
using System.Reflection;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace ntx20
{
    class UtcTimestampEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory lepf)
        {
            logEvent.AddPropertyIfAbsent(
              lepf.CreateProperty("UtcTimestamp", logEvent.Timestamp.UtcDateTime.ToString("o")));
        }
    }

    class Program
    {

        static void SetEnv(string path)
        {
            if (!File.Exists(path))
                return;
            try
            {

                string line;

                // Read the file and display it line by line.  
                System.IO.StreamReader file =
                    new(path, true);
                while ((line = file.ReadLine()) != null)
                {
                    if (!line.Contains('='))
                        continue;
                    if (line.Trim().StartsWith('#'))
                        continue;
                    var ret = line.Split("=", 2);
                    if (ret.Length == 2)
                    {
                        System.Environment.SetEnvironmentVariable(ret[0], ret[1]);
                    }
                    else
                    {
                        System.Environment.SetEnvironmentVariable(ret[0], null);
                    }
                }

            }
            catch
            {

            }
        }


        static async Task<int> Main(string[] args)
        {

            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

            try
            {
                //disable error-gui on windows, maybe not needed on netcore ... 
                _ = WinFail.SetErrorMode(WinFail.ErrorModes.FailCriticalErrors | WinFail.ErrorModes.NoGpFaultErrorBox | WinFail.ErrorModes.NoOpenFileErrorBox);
            }
            catch { }


            var config_path = Path.GetDirectoryName(typeof(Program).GetTypeInfo().Assembly.Location);
            var system = "linux-x64";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                system = "windows-x64";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                system = "osx-x64";

            var profile = Environment.GetEnvironmentVariable("NTX20_PROFILE") ?? ".env";
            SetEnv(Path.Combine(config_path, profile));
            SetEnv(Path.Combine(config_path, $"{system}.env"));




            bool firstBreak = false;

            var breaker = new CancellationTokenSource();
            
            var options = CommandLineOptions.Parse(args);


            if (options == null || options.Command==null)
            {
                return 1;
            }


            api.Logging.Level = options.LogLevel;

            var loggingLevelSwicth = new LoggingLevelSwitch { MinimumLevel = LogEventLevel.Information };
            try
            {
                loggingLevelSwicth.MinimumLevel = CommandLineOptions.ParseLoggingLevel(options.LogLevel);
            }
            catch { }
            Func<LogEvent, bool> filter = (e) => true;
            if (options.LogFilter != ".*")
            {
                var matcher = Regex.Unescape(options.LogFilter);
                filter = (x) => x.Properties.ContainsKey("SourceContext")
                && Regex.IsMatch(x.Properties["SourceContext"].ToString(), matcher);
            }


            //configure Serilog logger for console
            var logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.ControlledBy(loggingLevelSwicth)
                .Enrich.With(new UtcTimestampEnricher())
                .Filter.ByIncludingOnly(filter)
                .WriteTo.Console(
                outputTemplate: "[{UtcTimestamp}] [{Level}] [{SourceContext:l}] {Message}{NewLine}{Exception}",
                restrictedToMinimumLevel: LogEventLevel.Verbose,
                standardErrorFromLevel: LogEventLevel.Verbose)
                .CreateLogger();

            // Serilog logger to MS Logging factory
            api.Logging.LoggerFactory.AddSerilog(logger);

            //Use logger this way ... always, just define context, that can be searched and filtered
            var _logger = api.Logging.LoggerFactory.CreateLogger("ntx20");

            //Check for user interrupt ... to be interactive
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                if (firstBreak)
                {
                    //kill yourself
                    _logger.LogInformation("Forced to commit suicide by Ctrl+C");
                    Environment.Exit(2);
                }
                else
                {
                    //allow gracefull shutdown
                    _logger.LogInformation("Interrupted by Ctrl+C, waiting for gracefull shutdown");
                    breaker.Cancel();
                    firstBreak = true;
                }

            };

            if (options?.Command == null)
            {
                _logger.LogCritical("Invalid command line options, see help");
                return 2;
            }


            try
            {

                _logger.LogDebug("Starting ...");
                var ret = await options.Command.RunAsync(breaker.Token); ;
                if (firstBreak)
                    _logger.LogInformation("Interrupted ...");
                else
                    _logger.LogDebug("Completed ...");

                return ret;
            }
            catch (Exception ex)
            {
                _logger.LogCritical("Failed: \"{0}\"", ex.Message);
                _logger.LogDebug("Exception: \"{0}\"", ex.ToString());
                return 1;
            }
        }

    }
    
}
