using System;
using DebugPilot.Core;
using Xunit;

namespace DebugPilot.Core.Tests
{
    public class SecretRedactorTests
    {
        // Fake secrets are assembled from pieces so repository secret scanners do not flag this file.
        private static readonly string Aws = "AKIA" + "IOSFODNN7EXAMPLE";
        private static readonly string GitHub = "gh" + "p_" + "abcdefghijklmnopqrstuvwxyz0123456789";
        private static readonly string Anthropic = "sk-" + "ant-api03-" + "abcdefghijklmnopqrstuvwxyz0123456789ABCDEF";
        private static readonly string OpenAi = "sk-" + "proj1234567890abcdefghijklmnop";
        private static readonly string Jwt = "eyJhbGciOiJIUzI1NiJ9" + "." + "eyJzdWIiOiIxMjM0NTY3ODkwIn0" + "." + "dBjftJeZ4CVPmB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        private static readonly string Google = "AIza" + "SyA1234567890abcdefghijklmnopqrstuv";
        private static readonly string Slack = "xox" + "b-1234567890-abcdefghij";

        [Theory]
        [InlineData("aws")]
        [InlineData("github")]
        [InlineData("anthropic")]
        [InlineData("openai")]
        [InlineData("jwt")]
        [InlineData("google")]
        [InlineData("slack")]
        public void Known_token_formats_are_masked(string kind)
        {
            string secret = kind == "aws" ? Aws : kind == "github" ? GitHub : kind == "anthropic" ? Anthropic :
                kind == "openai" ? OpenAi : kind == "jwt" ? Jwt : kind == "google" ? Google : Slack;
            string text = SecretRedactor.Redact("build failed while calling the service with " + secret + " and retrying");
            Assert.DoesNotContain(secret, text);
            Assert.Contains("build failed while calling the service", text);
        }

        [Fact]
        public void Private_key_block_is_masked()
        {
            string text = SecretRedactor.Redact("x\n-----BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEAxyz123\nabcDEF456\n-----END RSA PRIVATE KEY-----\ny");
            Assert.DoesNotContain("MIIEowIBAAKCAQEAxyz123", text);
            Assert.DoesNotContain("abcDEF456", text);
            Assert.Contains("<REDACTED:PRIVATE_KEY>", text);
            Assert.Contains("y", text);
        }

        [Fact]
        public void Truncated_private_key_block_without_end_marker_is_still_masked()
        {
            string text = SecretRedactor.Redact("log\n-----BEGIN PRIVATE KEY-----\nMIIEvQIBADANBgkqhkiG9w0BAQEFAASC");
            Assert.DoesNotContain("MIIEvQIBADANBgkq", text);
        }

        [Fact]
        public void Url_credentials_are_masked()
        {
            string text = SecretRedactor.Redact("clone https://admin:hunter2pass@git.example.com/repo.git failed");
            Assert.DoesNotContain("hunter2pass", text);
            Assert.DoesNotContain("admin:", text);
            Assert.Contains("git.example.com", text);
        }

        [Fact]
        public void Authorization_header_token_is_not_left_behind()
        {
            string text = SecretRedactor.Redact("Authorization: Bearer abcdefghijklmnop1234567890");
            Assert.DoesNotContain("abcdefghijklmnop1234567890", text);
        }

        [Fact]
        public void Basic_auth_is_masked()
        {
            string text = SecretRedactor.Redact("Authorization: Basic dXNlcjpwYXNzd29yZDEyMzQ1Ng==");
            Assert.DoesNotContain("dXNlcjpwYXNzd29yZDEyMzQ1Ng", text);
        }

        [Fact]
        public void Quoted_secret_containing_spaces_is_fully_masked()
        {
            string text = SecretRedactor.Redact("var password = \"my secret phrase 42\"; var other = 1;");
            Assert.DoesNotContain("secret phrase", text);
            Assert.DoesNotContain("phrase 42", text);
            Assert.Contains("var other = 1;", text);
        }

        [Fact]
        public void Verbatim_and_json_quoted_secrets_are_masked()
        {
            string text = SecretRedactor.Redact("string client_secret = @\"abc def ghi\"; {\"apiKey\": \"zzz yyy\"}");
            Assert.DoesNotContain("abc def ghi", text);
            Assert.DoesNotContain("zzz yyy", text);
        }

        [Fact]
        public void Connection_string_password_is_masked()
        {
            string text = SecretRedactor.Redact("Server=tcp:x.database.windows.net;User ID=sa;Password=Abc123!!;Encrypt=True");
            Assert.DoesNotContain("Abc123", text);
            Assert.Contains("Encrypt=True", text);
        }

        [Fact]
        public void Ordinary_code_and_diagnostics_are_preserved()
        {
            string input = "src\\Program.cs(12,5): error CS0246: The type or namespace name 'Widget' could not be found";
            Assert.Equal(input, SecretRedactor.Redact(input));
            Assert.Equal("int count = 5;", SecretRedactor.Redact("int count = 5;"));
        }

        [Fact]
        public void Redaction_is_idempotent()
        {
            string once = SecretRedactor.Redact("password=\"a b\" token=zzz Bearer abcdefghijklmnop1234 " + GitHub + " me@example.com C:\\Users\\Alice\\x");
            Assert.Equal(once, SecretRedactor.Redact(once));
        }

        [Fact]
        public void Large_input_is_handled_without_throwing()
        {
            string big = new string('a', 1_000_000) + " token=abc123";
            string text = SecretRedactor.Redact(big);
            Assert.DoesNotContain("abc123", text);
        }

        [Fact]
        public void Null_and_empty_are_safe()
        {
            Assert.Equal(string.Empty, SecretRedactor.Redact(null));
            Assert.Equal(string.Empty, SecretRedactor.Redact(string.Empty));
        }
    }
}
