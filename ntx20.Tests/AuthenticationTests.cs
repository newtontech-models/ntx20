using System.Text;
using Grpc.Core;
using Microsoft.Extensions.CommandLineUtils;
using ntx20.command;

namespace ntx20.Tests;

public class AuthenticationTests
{
    [Fact]
    public void CreateMetadata_PreservesLegacyUrlCredentials()
    {
        var uri = new Uri("https://legacy-user:p%40ss%3Aword@example.test");
        var (usernameOption, passwordOption) = ParseCredentialOptions();

        var metadata = Authentication.CreateMetadata(
            uri, usernameOption, passwordOption, Array.Empty<string>());

        Assert.Equal(ExpectedBasicValue(uri.UserInfo), AuthorizationValue(metadata));
    }

    [Fact]
    public void CreateMetadata_ReadsBothEnvironmentVariablesVerbatim()
    {
        var usernameVariable = UniqueVariable("USERNAME");
        var passwordVariable = UniqueVariable("PASSWORD");
        const string username = "test-user";
        const string password = "p@ss:/?#[]!$&'()*+,;=% +žluťoučký";

        using var usernameEnvironment = new EnvironmentVariable(usernameVariable, username);
        using var passwordEnvironment = new EnvironmentVariable(passwordVariable, password);
        var (usernameOption, passwordOption) = ParseCredentialOptions(
            "--username-env", usernameVariable,
            "--password-env", passwordVariable);

        var metadata = Authentication.CreateMetadata(
            new Uri("https://example.test"),
            usernameOption,
            passwordOption,
            Array.Empty<string>());

        Assert.Equal(ExpectedBasicValue($"{username}:{password}"), AuthorizationValue(metadata));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CreateMetadata_RejectsOnlyOneEnvironmentOption(bool provideUsername)
    {
        var variable = UniqueVariable(provideUsername ? "USERNAME" : "PASSWORD");
        var arguments = provideUsername
            ? new[] { "--username-env", variable }
            : new[] { "--password-env", variable };
        var (usernameOption, passwordOption) = ParseCredentialOptions(arguments);

        var exception = Assert.Throws<ArgumentException>(() => Authentication.CreateMetadata(
            new Uri("https://example.test"),
            usernameOption,
            passwordOption,
            Array.Empty<string>()));

        Assert.Contains("must be used together", exception.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CreateMetadata_RejectsMissingEnvironmentVariable(bool usernameIsMissing)
    {
        var usernameVariable = UniqueVariable("USERNAME");
        var passwordVariable = UniqueVariable("PASSWORD");

        using var usernameEnvironment = new EnvironmentVariable(
            usernameVariable, usernameIsMissing ? null : "test-user");
        using var passwordEnvironment = new EnvironmentVariable(
            passwordVariable, usernameIsMissing ? "test-password" : null);
        var (usernameOption, passwordOption) = ParseCredentialOptions(
            "--username-env", usernameVariable,
            "--password-env", passwordVariable);

        var exception = Assert.Throws<ArgumentException>(() => Authentication.CreateMetadata(
            new Uri("https://example.test"),
            usernameOption,
            passwordOption,
            Array.Empty<string>()));

        var missingVariable = usernameIsMissing ? usernameVariable : passwordVariable;
        Assert.Contains($"Environment variable {missingVariable} is not set", exception.Message);
    }

    [Fact]
    public void CreateMetadata_RejectsEnvironmentOptionsWithUrlCredentials()
    {
        var usernameVariable = UniqueVariable("USERNAME");
        var passwordVariable = UniqueVariable("PASSWORD");
        using var usernameEnvironment = new EnvironmentVariable(usernameVariable, "test-user");
        using var passwordEnvironment = new EnvironmentVariable(passwordVariable, "test-password");
        var (usernameOption, passwordOption) = ParseCredentialOptions(
            "--username-env", usernameVariable,
            "--password-env", passwordVariable);

        var exception = Assert.Throws<ArgumentException>(() => Authentication.CreateMetadata(
            new Uri("https://url-user:url-password@example.test"),
            usernameOption,
            passwordOption,
            Array.Empty<string>()));

        Assert.Contains("Connection string cannot contain credentials", exception.Message);
    }

    [Fact]
    public void CreateMetadata_RejectsEnvironmentOptionsWithAuthorizationHeader()
    {
        var usernameVariable = UniqueVariable("USERNAME");
        var passwordVariable = UniqueVariable("PASSWORD");
        using var usernameEnvironment = new EnvironmentVariable(usernameVariable, "test-user");
        using var passwordEnvironment = new EnvironmentVariable(passwordVariable, "test-password");
        var (usernameOption, passwordOption) = ParseCredentialOptions(
            "--username-env", usernameVariable,
            "--password-env", passwordVariable);

        var exception = Assert.Throws<ArgumentException>(() => Authentication.CreateMetadata(
            new Uri("https://example.test"),
            usernameOption,
            passwordOption,
            new[] { "aUtHoRiZaTiOn:Bearer explicit-token" }));

        Assert.Contains("Authorization header cannot be used", exception.Message);
    }

    private static (CommandOption Username, CommandOption Password) ParseCredentialOptions(
        params string[] arguments)
    {
        var application = new CommandLineApplication();
        var usernameOption = application.Option(
            "--username-env <variable>", string.Empty, CommandOptionType.SingleValue);
        var passwordOption = application.Option(
            "--password-env <variable>", string.Empty, CommandOptionType.SingleValue);

        Assert.Equal(0, application.Execute(arguments));
        return (usernameOption, passwordOption);
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
