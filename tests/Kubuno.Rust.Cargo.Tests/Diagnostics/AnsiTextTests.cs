using Kubuno.Cargo.Diagnostics;
using Xunit;

namespace Kubuno.Cargo.Tests.Diagnostics
{
    public class AnsiTextTests
    {
        [Fact]
        public void Strips_SGR_color_and_style_sequences()
        {
            string ansi = "\u001b[1m\u001b[93mwarning\u001b[0m\u001b[1m\u001b[97m: unused variable\u001b[0m";
            Assert.Equal("warning: unused variable", AnsiText.Strip(ansi));
        }

        [Fact]
        public void Leaves_plain_text_untouched()
        {
            Assert.Equal("no escapes here", AnsiText.Strip("no escapes here"));
        }

        [Fact]
        public void Handles_empty_string()
        {
            Assert.Equal(string.Empty, AnsiText.Strip(string.Empty));
        }

        [Fact]
        public void Strips_sequences_from_a_real_captured_rendered_diagnostic()
        {
            string[] lines = TestFixtures.ReadAllLines("Build", "warning-build-clean.jsonl");
            string rendered = System.Text.Json.JsonDocument
                .Parse(lines[0])
                .RootElement
                .GetProperty("message")
                .GetProperty("rendered")
                .GetString()!;

            Assert.Contains('\u001B', rendered);

            string stripped = AnsiText.Strip(rendered);
            Assert.DoesNotContain('\u001B', stripped);
            Assert.Contains("unused variable: `unused`", stripped);
        }
    }
}
