using System.Text;
using Grpc.Core;

namespace ntx20.Tests;

public class CommandAuthenticationIntegrationTests
{
    [Theory]
    [InlineData("run")]
    [InlineData("ws")]
    public void Parse_WiresEnvironmentAuthenticationIntoCommand(string command)
    {
        var usernameVariable = UniqueVariable("USERNAME");
        var passwordVariable = UniqueVariable("PASSWORD");
        const string username = "integration-user";
        const string password = "integration-p@ss:/?#[]!$&'()*+,;=% +ž";
        var callInvoker = new RecordingCallInvoker();
        Uri invokerAddress = null;

        using var usernameEnvironment = new EnvironmentVariable(usernameVariable, username);
        using var passwordEnvironment = new EnvironmentVariable(passwordVariable, password);

        var arguments = new List<string>
        {
            command,
            "atran-cz-openlex@https://example.test",
            "--username-env", usernameVariable,
            "--password-env", passwordVariable
        };

        if (command == "run")
        {
            arguments.Add("--input");
            arguments.Add("unused-test-input.mp3");
        }

        var initialOptions = new CommandLineOptions
        {
            CallInvokerFactory = address =>
            {
                invokerAddress = address;
                return callInvoker;
            }
        };

        var options = ParseLikeProgram(arguments.ToArray(), initialOptions);

        Assert.NotNull(options);
        Assert.NotNull(options.CreateStreaming);

        var usernameOption = command == "run"
            ? options.RunUsernameEnvOption
            : options.WsUsernameEnvOption;
        var passwordOption = command == "run"
            ? options.RunPasswordEnvOption
            : options.WsPasswordEnvOption;

        Assert.True(usernameOption.HasValue());
        Assert.Equal(usernameVariable, usernameOption.Value());
        Assert.True(passwordOption.HasValue());
        Assert.Equal(passwordVariable, passwordOption.Value());

        using var streamingCall = options.CreateStreaming();

        Assert.Equal(new Uri("https://example.test"), invokerAddress);
        Assert.Equal(1, callInvoker.DuplexStreamingCallCount);
        Assert.Equal(MethodType.DuplexStreaming, callInvoker.MethodType);
        Assert.Equal("/ntx.core20.EngineService/Streaming", callInvoker.MethodName);
        Assert.Equal(
            ExpectedBasicValue($"{username}:{password}"),
            AuthorizationValue(callInvoker.Headers));
    }

    [Theory]
    [InlineData("run")]
    [InlineData("ws")]
    public void Parse_WiresLegacyUrlAuthenticationIntoCommand(string command)
    {
        var serviceUri = new Uri("https://legacy-user:p%40ss%3Aword@example.test");
        var callInvoker = new RecordingCallInvoker();
        Uri invokerAddress = null;
        var arguments = new List<string>
        {
            command,
            $"atran-cz-openlex@{serviceUri}"
        };

        if (command == "run")
        {
            arguments.Add("--input");
            arguments.Add("unused-test-input.mp3");
        }

        var initialOptions = new CommandLineOptions
        {
            CallInvokerFactory = address =>
            {
                invokerAddress = address;
                return callInvoker;
            }
        };

        var options = ParseLikeProgram(arguments.ToArray(), initialOptions);

        Assert.NotNull(options);
        Assert.NotNull(options.CreateStreaming);

        using var streamingCall = options.CreateStreaming();

        Assert.Equal(serviceUri, invokerAddress);
        Assert.Equal(1, callInvoker.DuplexStreamingCallCount);
        Assert.Equal(MethodType.DuplexStreaming, callInvoker.MethodType);
        Assert.Equal("/ntx.core20.EngineService/Streaming", callInvoker.MethodName);
        Assert.Equal(
            ExpectedBasicValue(serviceUri.UserInfo),
            AuthorizationValue(callInvoker.Headers));
    }

    [Theory]
    [InlineData("run")]
    [InlineData("ws")]
    public void Parse_RejectsUrlCredentialsWhenEnvironmentAuthenticationIsConfigured(string command)
    {
        var usernameVariable = UniqueVariable("USERNAME");
        var passwordVariable = UniqueVariable("PASSWORD");

        using var usernameEnvironment = new EnvironmentVariable(usernameVariable, "integration-user");
        using var passwordEnvironment = new EnvironmentVariable(passwordVariable, "integration-password");

        var options = CommandLineOptions.Parse(new[]
        {
            command,
            "atran-cz-openlex@https://url-user:url-password@example.test",
            "--username-env", usernameVariable,
            "--password-env", passwordVariable
        });

        Assert.Null(options);
    }

    private static CommandLineOptions ParseLikeProgram(
        string[] arguments,
        CommandLineOptions options)
    {
        options = CommandLineOptions.Parse(arguments, options);
        Assert.NotNull(options);

        if (options.TheService != null)
        {
            options = CommandLineOptions.Parse(arguments, options);
        }

        return options;
    }

    private static string AuthorizationValue(Metadata metadata)
    {
        return Assert.Single(metadata, entry => entry.Key == "authorization").Value;
    }

    private static string ExpectedBasicValue(string userInfo)
    {
        return $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes(userInfo))}";
    }

    private static string UniqueVariable(string suffix)
    {
        return $"NTX20_TEST_{Guid.NewGuid():N}_{suffix}";
    }

    private sealed class EnvironmentVariable : IDisposable
    {
        private readonly string _name;
        private readonly string _previousValue;

        public EnvironmentVariable(string name, string value)
        {
            _name = name;
            _previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _previousValue);
        }
    }
}
