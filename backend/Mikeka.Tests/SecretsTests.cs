using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Mikeka.Api.Services;

namespace Mikeka.Tests;

public class SecretsTests
{
    internal static AccountSecrets Create(string? key = null) =>
        new(Options.Create(new EncryptionOptions { Key = key ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));

    [Fact]
    public void Round_trips_and_does_not_store_plain_text()
    {
        var s = Create();
        var stored = s.Protect("Password@12345");
        Assert.StartsWith("v1:", stored);
        Assert.DoesNotContain("Password", stored);
        Assert.NotEqual(stored, s.Protect("Password@12345")); // random nonce each time
        Assert.Equal("Password@12345", s.Unprotect(stored));
    }

    [Fact]
    public void Wrong_key_cannot_decrypt()
    {
        var stored = Create().Protect("secret");
        Assert.ThrowsAny<CryptographicException>(() => Create().Unprotect(stored));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64")]
    [InlineData("AAAA")] // too short
    public void Missing_or_bad_key_fails_at_startup(string key) =>
        Assert.Throws<InvalidOperationException>(() => Create(key));
}
