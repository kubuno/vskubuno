using System.Collections.Generic;
using System.Linq;
using Kubuno.Core.DevAssistant.Logic.Changes;
using Kubuno.Core.DevAssistant.Logic.Secrets;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Core.DevAssistant.Tests
{
    /// <summary>
    /// docs/AI-ASSISTANT.md section 8.3. The sample tokens are assembled at run time (never literal in the source) so
    /// the repository itself never holds a string a secret scanner would flag.
    /// </summary>
    [TestClass]
    public sealed class SecretMaskingTests
    {
        private static string Repeat(string chars, int count) => string.Concat(Enumerable.Range(0, count).Select(i => chars[i % chars.Length]));

        private static readonly string Anthropic = "sk-" + "ant-api03-" + Repeat("aB3dE5fG7h", 40);
        private static readonly string OpenAi = "sk-" + "proj-" + Repeat("Qw8Er7Ty6U", 40);
        private static readonly string GitHub = "gh" + "p_" + Repeat("Zx9Cv8Bn7M", 36);
        private static readonly string GitHubPat = "github" + "_pat_" + Repeat("11AbCdEf2G", 30);
        private static readonly string Npm = "np" + "m_" + Repeat("Kq5WzTb8Ys", 36);
        private static readonly string Aws = "AK" + "IA" + Repeat("Q7R4T2Y9U3", 16).ToUpperInvariant();
        private static readonly string Slack = "xo" + "xb-" + Repeat("1234567890", 12) + "-" + Repeat("aZbYcX", 24);
        private static readonly string Jwt = "ey" + "JhbGciOiJIUzI1NiJ9" + "." + "ey" + "JzdWIiOiIxMjM0NTY3ODkwIn0" + "." + Repeat("SflKxwRJSMeKKF2QT4", 43);
        private static readonly string Pem = "-----BEGIN " + "RSA PRIVATE KEY-----\nMIIEow" + Repeat("IBAAKCAQEA", 12) + "\n-----END " + "RSA PRIVATE KEY-----";

        public static IEnumerable<object[]> Corpus => new[]
        {
            new object[] { "key = \"" + Anthropic + "\"", "anthropic-key" },
            new object[] { "OPENAI=" + OpenAi, "openai-key" },
            new object[] { "remote token " + GitHub + " end", "github-token" },
            new object[] { GitHubPat, "github-token" },
            new object[] { "//registry.npmjs.org/:_authToken=" + Npm, "npm-token" },
            new object[] { "aws " + Aws, "aws-key" },
            new object[] { Slack, "slack-token" },
            new object[] { "Authorization: Bearer " + Jwt, "jwt" },
            new object[] { Pem, "private-key" },
            new object[] { "DATABASE_URL=postgres://kubuno:" + "S3cr" + "et!pw@localhost/kubuno_dev", "url-password" },
            new object[] { "Host=db;Username=app;Password=" + "Tr0ub4dor&3;", "password" },
            new object[] { "internal_secret = \"" + "f9Q2x7LmZ4" + "pR8vT1kW\"", "secret" },
            new object[] { "\"apiKey\": \"" + "aK9mQ2" + "xZ7pL4wR\"", "api-key" },
            new object[] { "- **Admin local** : `admin@example.test` / mot de passe `" + "Zorglub#@]" + "4096`", "password" },
        };

        [TestMethod]
        [DynamicData(nameof(Corpus))]
        public void Every_known_format_is_masked(string text, string kind)
        {
            var masker = new SecretMasker("salt");
            var masked = masker.Mask(text, out var count);
            Assert.IsTrue(count >= 1, "nothing masked in: " + text);
            StringAssert.Contains(masked, "«secret:" + kind + "#");
            Assert.IsTrue(SecretMasker.ContainsPlaceholder(masked));
            Assert.AreEqual(text, masker.Restore(masked).Text, "restoring gives the original back");
        }

        [TestMethod]
        public void Code_and_placeholders_are_not_secrets()
        {
            foreach (var text in new[]
            {
                "let token = self.token.clone();",
                "password: String,",
                "api_key = std::env::var(\"API_KEY\")?;",
                "secret = ${KUBUNO_SECRET}",
                "password = \"changeme\"",
                "token: Option<String>",
                "ApiKey = key,",
                "<Label x:Name=\"token_label\" Text=\"{Res token_text}\"/>",
            })
            {
                Assert.AreEqual(0, SecretScanner.Scan(text).Count, text);
            }
        }

        [TestMethod]
        public void Masking_is_deterministic_per_conversation_and_differs_across_conversations()
        {
            var text = "npm token " + Npm;
            var first = new SecretMasker("conversation-A").Mask(text);
            var again = new SecretMasker("conversation-A").Mask(text);
            var other = new SecretMasker("conversation-B").Mask(text);
            Assert.AreEqual(first, again, "same salt, same placeholder (history and cache stay identical, even after a restart)");
            Assert.AreNotEqual(first, other);
            Assert.IsFalse(first.Contains(Npm));
        }

        [TestMethod]
        public void An_edit_keeping_a_placeholder_restores_the_real_value()
        {
            var file = "[registry]\ntoken = \"" + Npm + "\"\nname = \"old\"\n";
            var masker = new SecretMasker("s");
            var seen = masker.Mask(file);
            var placeholder = seen.Split('"')[1];
            StringAssert.StartsWith(placeholder, "«secret:npm-token#");

            // The model saw the masked line and rewrites it, keeping the marker.
            var proposal = AnchoredEditApplier.Apply(file, new[]
            {
                new AnchoredEdit { OldText = "token = \"" + placeholder + "\"\nname = \"old\"", NewText = "token = \"" + placeholder + "\"\nname = \"new\"" },
            }, masker);

            Assert.IsTrue(proposal.Succeeded, proposal.Error);
            Assert.AreEqual("[registry]\ntoken = \"" + Npm + "\"\nname = \"new\"\n", proposal.Text, "the real secret is kept, never overwritten by its placeholder");
        }

        [TestMethod]
        public void An_invented_placeholder_is_refused()
        {
            var masker = new SecretMasker("s");
            var proposal = AnchoredEditApplier.Apply("a = 1\n", new[] { new AnchoredEdit { OldText = "a = 1", NewText = "a = «secret:password#abcdef»" } }, masker);
            Assert.IsFalse(proposal.Succeeded);
            StringAssert.Contains(proposal.Error, "unknown secret placeholder");
        }

        [TestMethod]
        public void Denied_files_and_folders()
        {
            var roots = new[] { @"C:\work\solution" };
            Assert.IsNull(DeniedFiles.WhyDenied(@"C:\work\solution\src\main.rs", roots));
            Assert.AreEqual("outside-solution", DeniedFiles.WhyDenied(@"C:\work\other\main.rs", roots));
            Assert.AreEqual("outside-solution", DeniedFiles.WhyDenied(@"C:\work\solution\..\CLAUDE.md", roots));
            foreach (var name in new[] { ".env", ".env.local", "id_rsa", "server.pem", "cert.pfx", "store.kdbx", ".npmrc", "credentials.json", ".git-credentials", "secrets.json" })
            {
                Assert.AreEqual("secret-file", DeniedFiles.WhyDenied(@"C:\work\solution\" + name, roots), name);
            }

            Assert.AreEqual("protected-folder", DeniedFiles.WhyDenied(@"C:\work\solution\.git\config", roots));
            Assert.AreEqual("protected-folder", DeniedFiles.WhyDenied(@"C:\work\solution\target\debug\x.rs", roots));
            Assert.AreEqual("outside-solution", DeniedFiles.WhyDenied(@"C:\work\solution\a.rs", new string[0]));
        }
    }
}
