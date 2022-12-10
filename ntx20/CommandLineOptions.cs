using Grpc.Core;
using Microsoft.Extensions.CommandLineUtils;
using ntx20.api.proto;
using ntx20.command;
using Serilog.Events;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ntx20
{
    public static class Helper
    {
        public async static Task ShowHelpAsync(this CommandLineApplication app)
        {
            await Task.Run(() => app.ShowHelp());
        }

        public static string GetValueOrDefault(this CommandOption option)
        {
            return option.HasValue() ? option.Value() : option.ValueName;
        }

        public static void MustSetValue(this CommandOption option,CommandLineApplication command)
        {
            if(!option.HasValue())
            {
                command.ShowHelp();
                command.Error.WriteLine($"Please set  --{option.LongName}"+ Environment.NewLine+Environment.NewLine);
                throw new ArgumentException ("Invalid option");
            }
        }

        public static void MustSetValue(this CommandArgument option, CommandLineApplication command)
        {
            if (option.Value == null)
            {
                command.ShowHelp();
                command.Error.WriteLine($"Please set  {option.Name}" + Environment.NewLine + Environment.NewLine);
                throw new ArgumentException("Misssing argument");
            }
        }

        public static string RenderEnumHelp(this string[] values, string name)
        {
            return $"\t{name}=({string.Join("|", values)})" + Environment.NewLine;

        }

        public static string RenderOption(this CommandOption option)
        {
            option.ShowInHelpText = false;
            return "  " + option.Template + "" + "\t" + option.Description + Environment.NewLine;
        }

    }

    public class CommandLineOptions
    {
        public string LogLevel { get; set; }
        public ICommand Command { get; set; }
        public string Version { get; set; }
        public string LogFilter { get; set; }

        public Grpc.Net.Client.GrpcChannel Channel { get; set; }
        public Func<AsyncDuplexStreamingCall<api.proto.Payload,api.proto.Payload>> CreateStreaming { get; set; }
        
        public ntx20.api.proto.ServiceVersion TheService { get; set; }

        public static string[] SplitAsCmdArguments(string args)
        {
            char[] parmChars = args.ToCharArray();
            bool inSingleQuote = false;
            bool inDoubleQuote = false;
            bool escaped = false;
            bool lastSplitted = false;
            bool justSplitted = false;
            bool lastQuoted = false;
            bool justQuoted = false;

            int i, j;

            for (i = 0, j = 0; i < parmChars.Length; i++, j++)
            {
                parmChars[j] = parmChars[i];

                if (!escaped)
                {
                    if (parmChars[i] == '^')
                    {
                        escaped = true;
                        j--;
                    }
                    else if (parmChars[i] == '"' && !inSingleQuote)
                    {
                        inDoubleQuote = !inDoubleQuote;
                        parmChars[j] = '\n';
                        justSplitted = true;
                        justQuoted = true;
                    }
                    else if (parmChars[i] == '\'' && !inDoubleQuote)
                    {
                        inSingleQuote = !inSingleQuote;
                        parmChars[j] = '\n';
                        justSplitted = true;
                        justQuoted = true;
                    }
                    else if (!inSingleQuote && !inDoubleQuote && parmChars[i] == ' ')
                    {
                        parmChars[j] = '\n';
                        justSplitted = true;
                    }

                    if (justSplitted && lastSplitted && (!lastQuoted || !justQuoted))
                        j--;

                    lastSplitted = justSplitted;
                    justSplitted = false;

                    lastQuoted = justQuoted;
                    justQuoted = false;
                }
                else
                {
                    escaped = false;
                }
            }

            if (lastQuoted)
                j--;

            return (new string(parmChars, 0, j)).Split(new[] { '\n' });
        }
       
        public static CommandLineOptions Parse(string cmd)
        {
            return Parse(SplitAsCmdArguments(cmd));
        }
        public static CommandLineOptions Parse(string[] args, CommandLineOptions options = null)
        {
            Console.OutputEncoding = Encoding.UTF8;
            

            if(options==null)
                options = new CommandLineOptions();

            //Get program version
            
            
            var location = typeof(CommandLineOptions)
            .GetTypeInfo()
            .Assembly.Location;


            options.Version = typeof(CommandLineOptions)
           .GetTypeInfo()
           .Assembly
           .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
           .InformationalVersion;



            //.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            //.InformationalVersion;

            var app = new CommandLineApplication
            {
                Name = "ntx20",
                FullName = $"ntx20 cli ({options.Version})",
                
                 
            };

            //app.VersionOption("-v|--version", options.Version);
            app.HelpOption("-h|--help");
            
            var loggingOption = app.Option("-l|--loglevel <info>", 
                "global logging level trace|debug|info|warn|error", 
                CommandOptionType.SingleValue);
            var loggingFilterOption = app.Option("-f|--logfilter <.*>",
                "global logging filter",
                CommandOptionType.SingleValue);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (Environment.GetEnvironmentVariable("HOME") == null)
                {
                    Environment.SetEnvironmentVariable("HOME", 
                        Environment.ExpandEnvironmentVariables("%HOMEDRIVE%%HOMEPATH%"
                        ));
                }
            }
            command.Command.Configure(app, options);
            
            var _args = args.ToList();
            try
            {
                var result = app.Execute(_args.ToArray());
                if(result!=0)
                    throw new Exception("Parsing failed");
                
            }catch(ArgumentException)
            {
                //Console.Error.WriteLine($"Error parsing cmd: {ex.Message}");
                return null;
            }
            catch(Exception ex)
            {
                Console.Error.WriteLine($"Error parsing cmd: {ex}");
                return null;
            }
            options.LogLevel = loggingOption.HasValue() ? loggingOption.Value() : loggingOption.ValueName;
            options.LogFilter = loggingFilterOption.HasValue() ? loggingFilterOption.Value() : loggingFilterOption.ValueName;

            return options;
        }
        public static LogEventLevel ParseLoggingLevel(string cmd)
        {
            return cmd switch
            {
                "trace" => LogEventLevel.Verbose,
                "debug" => LogEventLevel.Debug,
                "info" => LogEventLevel.Information,
                "warn" => LogEventLevel.Warning,
                "error" => LogEventLevel.Error,
                _ => throw new Exception($"invalid loglevel {cmd}"),
            };
        }

    }
}
