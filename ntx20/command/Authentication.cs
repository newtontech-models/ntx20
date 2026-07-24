using Grpc.Core;
using Microsoft.Extensions.CommandLineUtils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ntx20.command
{
    static class Authentication
    {
        public static void ConfigureRun(CommandLineApplication command, CommandLineOptions options)
        {
            options.RunUsernameEnvOption = command.Option("--username-env <variable>",
                "read authentication username from environment variable",
                CommandOptionType.SingleValue);
            options.RunPasswordEnvOption = command.Option("--password-env <variable>",
                "read authentication password from environment variable",
                CommandOptionType.SingleValue);
        }

        public static void ConfigureWs(CommandLineApplication command, CommandLineOptions options)
        {
            options.WsUsernameEnvOption = command.Option("--username-env <variable>",
                "read authentication username from environment variable",
                CommandOptionType.SingleValue);
            options.WsPasswordEnvOption = command.Option("--password-env <variable>",
                "read authentication password from environment variable",
                CommandOptionType.SingleValue);
        }

        public static Metadata CreateMetadata(Uri uri, CommandOption usernameEnvOption,
            CommandOption passwordEnvOption, IEnumerable<string> headers)
        {
            var hasUsernameEnv = usernameEnvOption.HasValue();
            var hasPasswordEnv = passwordEnvOption.HasValue();

            if (hasUsernameEnv != hasPasswordEnv)
            {
                throw Error("Options --username-env and --password-env must be used together");
            }

            var userInfo = uri.UserInfo;
            if (hasUsernameEnv)
            {
                if (userInfo.Length > 0)
                {
                    throw Error("Connection string cannot contain credentials when --username-env and --password-env are used");
                }

                if (headers.Any(IsAuthorizationHeader))
                {
                    throw Error("Authorization header cannot be used when --username-env and --password-env are used");
                }

                var usernameEnv = usernameEnvOption.Value();
                var passwordEnv = passwordEnvOption.Value();
                var username = Environment.GetEnvironmentVariable(usernameEnv);
                var password = Environment.GetEnvironmentVariable(passwordEnv);

                if (username == null)
                {
                    throw Error($"Environment variable {usernameEnv} is not set");
                }
                if (password == null)
                {
                    throw Error($"Environment variable {passwordEnv} is not set");
                }
                if (username.Length == 0)
                {
                    Console.Error.WriteLine($"Warning: Environment variable {usernameEnv} is empty");
                }
                if (password.Length == 0)
                {
                    Console.Error.WriteLine($"Warning: Environment variable {passwordEnv} is empty");
                }

                userInfo = $"{username}:{password}";
            }

            return new Metadata { { "Authorization", $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes(userInfo))}" } };
        }

        private static bool IsAuthorizationHeader(string header)
        {
            var separator = header.IndexOf(':');
            return separator >= 0 &&
                header.Substring(0, separator).Equals("Authorization", StringComparison.OrdinalIgnoreCase);
        }

        private static ArgumentException Error(string message)
        {
            Console.Error.WriteLine($"Error: {message}");
            return new ArgumentException(message);
        }
    }
}
