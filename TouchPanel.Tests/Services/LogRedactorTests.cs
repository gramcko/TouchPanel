using TouchPanel.Services.Logging;

namespace TouchPanel.Tests.Services;

public class LogRedactorTests
{
    [Theory]
    [InlineData("password")]
    [InlineData("Password")]
    [InlineData("PASSWORD")]
    [InlineData("passwd")]
    [InlineData("pwd")]
    [InlineData("user_password")]
    [InlineData("secret")]
    [InlineData("clientSecret")]
    [InlineData("token")]
    [InlineData("access_token")]
    [InlineData("accessToken")]
    [InlineData("apiKey")]
    [InlineData("api_key")]
    [InlineData("api-key")]
    [InlineData("X-Api-Key")]
    [InlineData("authorization")]
    [InlineData("Authorization")]
    [InlineData("credential")]
    [InlineData("private_key")]
    [InlineData("connectionString")]
    [InlineData("bearerToken")]
    [InlineData("key")]
    public void IsSensitiveKey_SensitiveNames_ReturnsTrue(string key)
    {
        Assert.True(LogRedactor.IsSensitiveKey(key));
    }

    [Theory]
    [InlineData("username")]
    [InlineData("command")]
    [InlineData("workingDirectory")]
    [InlineData("arguments")]
    [InlineData("url")]
    [InlineData("monkey")]
    [InlineData("keyboard")]
    [InlineData("hotkey")]
    [InlineData("label")]
    public void IsSensitiveKey_NonSensitiveNames_ReturnsFalse(string key)
    {
        Assert.False(LogRedactor.IsSensitiveKey(key));
    }

    [Theory]
    [InlineData("password", "hunter2")]
    [InlineData("ApiKey", "abc123")]
    [InlineData("authorization", "Bearer abc.def.ghi")]
    [InlineData("access_token", "opaque-value")]
    public void RedactValue_SensitiveKey_ReturnsMask(string key, string value)
    {
        var redacted = LogRedactor.RedactValue(key, value);

        Assert.Equal(LogRedactor.Mask, redacted);
        Assert.DoesNotContain(value, redacted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("username", "alice")]
    [InlineData("command", @"C:\Program Files\app.exe")]
    [InlineData("workingDirectory", @"C:\Temp")]
    public void RedactValue_NonSensitiveKey_ReturnsValueUnchanged(string key, string value)
    {
        Assert.Equal(value, LogRedactor.RedactValue(key, value));
    }

    [Fact]
    public void RedactValue_SensitiveKeyWithEmptyValue_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, LogRedactor.RedactValue("password", string.Empty));
    }

    [Fact]
    public void RedactParameters_MasksSensitiveKeysAndScrubsEmbeddedSecrets()
    {
        var parameters = new Dictionary<string, string?>
        {
            ["token"] = "abc123",
            ["api_key"] = "topsecret",
            ["command"] = @"C:\Program Files\app.exe",
            ["url"] = "https://host/api?api_key=embedded-secret&user=bob",
            ["header"] = "Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.payload.sig",
            ["empty"] = null,
        };

        var redacted = LogRedactor.RedactParameters(parameters);

        Assert.Equal(LogRedactor.Mask, redacted["token"]);
        Assert.Equal(LogRedactor.Mask, redacted["api_key"]);
        Assert.Equal(@"C:\Program Files\app.exe", redacted["command"]);
        Assert.DoesNotContain("embedded-secret", redacted["url"], StringComparison.Ordinal);
        Assert.Contains("user=bob", redacted["url"], StringComparison.Ordinal);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", redacted["header"], StringComparison.Ordinal);
        Assert.Null(redacted["empty"]);
    }

    [Fact]
    public void RedactText_ScrubsKeyValuePairs()
    {
        Assert.DoesNotContain("hunter2", LogRedactor.RedactText("login failed: password=hunter2"));
        Assert.DoesNotContain("hunter2", LogRedactor.RedactText("PASSWORD: hunter2"));
        Assert.DoesNotContain("abc123", LogRedactor.RedactText("token=abc123&user=bob"));
        Assert.DoesNotContain("s3cr3t", LogRedactor.RedactText("password = \"s3cr3t value\""));    }

    [Fact]
    public void RedactText_ScrubsJsonFields()
    {
        Assert.DoesNotContain("abc123", LogRedactor.RedactText("""{"apiKey":"abc123"}"""));
        Assert.DoesNotContain("abc.def", LogRedactor.RedactText("""{"token":"abc.def"}"""));
        Assert.DoesNotContain("p@ss w0rd", LogRedactor.RedactText("""{"password": "p@ss w0rd"}"""));
    }

    [Fact]
    public void RedactText_ScrubsBearerAndBasicTokens()
    {
        var bearer = LogRedactor.RedactText(
            "request headers: Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.payload.signature");
        var basic = LogRedactor.RedactText("request headers: authorization: basic dXNlcjpwYXNzd29yZA==");

        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", bearer, StringComparison.Ordinal);
        Assert.DoesNotContain("dXNlcjpwYXNzd29yZA==", basic, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactText_LeavesOrdinaryTextUntouched()
    {
        const string text = "action failed: hotkey Ctrl+Alt+T not registered; notepad.exe exited with code 0";

        Assert.Equal(text, LogRedactor.RedactText(text));
    }

    [Fact]
    public void RedactText_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, LogRedactor.RedactText(null));
        Assert.Equal(string.Empty, LogRedactor.RedactText(string.Empty));
    }
}
