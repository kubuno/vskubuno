using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Kubuno.Core.DevAssistant.Logic.Protocol;

namespace Kubuno.Core.DevAssistant.Logic.Conversations
{
    /// <summary>A conversation as kept by the extension: masked turns only, plus its running cost.</summary>
    public sealed class Conversation
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string Title { get; set; } = string.Empty;

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        public string Provider { get; set; } = ProviderIds.Anthropic;

        public string Model { get; set; } = string.Empty;

        /// <summary>The masking salt (<see cref="Secrets.SecretMasker"/>): random, not a secret, needed for stable placeholders.</summary>
        public string Salt { get; set; } = Secrets.SecretMasker.NewSalt();

        public decimal CostUsd { get; set; }

        public UsageInfo Usage { get; set; } = new UsageInfo();

        public List<ChatTurn> Turns { get; set; } = new List<ChatTurn>();
    }

    /// <summary>One line of the history list.</summary>
    public sealed class ConversationSummary
    {
        public string Id { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public DateTime UpdatedUtc { get; set; }

        public string Model { get; set; } = string.Empty;

        public decimal CostUsd { get; set; }
    }

    /// <summary>
    /// Conversations of one solution, in <c>&lt;root&gt;\.vs\Kubuno\dev-assistant\conversations\</c> (docs/AI-ASSISTANT.md
    /// section 9.7): <c>.vs</c> is per user and ignored by git, nothing is synced. One JSON record per line, append-only
    /// (a header, then one record per turn and per usage update); an <c>index.json</c> lists titles for the history.
    /// Turns are stored exactly as sent - already masked - so a secret never lands in <c>.vs</c> through the assistant.
    /// </summary>
    public sealed class ConversationStore
    {
        private readonly string _folder;

        public ConversationStore(string solutionRoot)
        {
            _folder = Path.Combine(solutionRoot, ".vs", "Kubuno", "dev-assistant", "conversations");
        }

        public string Folder => _folder;

        private string IndexPath => Path.Combine(_folder, "index.json");

        private string FileOf(string id) => Path.Combine(_folder, id + ".jsonl");

        /// <summary>Starts the file of a new conversation (header record).</summary>
        public void Create(Conversation conversation)
        {
            Directory.CreateDirectory(_folder);
            var header = new ConversationRecord
            {
                Kind = "header",
                Id = conversation.Id,
                Title = conversation.Title,
                AtUtc = conversation.CreatedUtc,
                Provider = conversation.Provider,
                Model = conversation.Model,
                Salt = conversation.Salt,
            };
            File.WriteAllText(FileOf(conversation.Id), Line(header), Encoding.UTF8);
            UpdateIndex(conversation);
        }

        /// <summary>Appends turns (and the conversation's new totals) to its file.</summary>
        public void Append(Conversation conversation, IEnumerable<ChatTurn> turns)
        {
            Directory.CreateDirectory(_folder);
            if (!File.Exists(FileOf(conversation.Id)))
            {
                Create(conversation);
            }

            var builder = new StringBuilder();
            foreach (var turn in turns)
            {
                builder.Append(Line(new ConversationRecord { Kind = "turn", AtUtc = DateTime.UtcNow, Turn = turn }));
            }

            builder.Append(Line(new ConversationRecord { Kind = "totals", AtUtc = DateTime.UtcNow, Model = conversation.Model, CostUsd = conversation.CostUsd, Usage = conversation.Usage, Title = conversation.Title }));
            File.AppendAllText(FileOf(conversation.Id), builder.ToString(), Encoding.UTF8);
            conversation.UpdatedUtc = DateTime.UtcNow;
            UpdateIndex(conversation);
        }

        /// <summary>Reads a conversation back (turns in order, last totals); null when missing or unreadable.</summary>
        public Conversation? Load(string id)
        {
            var path = FileOf(id);
            if (!File.Exists(path))
            {
                return null;
            }

            Conversation? conversation = null;
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (line.Length == 0)
                {
                    continue;
                }

                ConversationRecord? record;
                try
                {
                    record = JsonSerializer.Deserialize<ConversationRecord>(line, RpcCodec.Options);
                }
                catch (JsonException)
                {
                    continue; // A torn last line after a crash: keep what precedes it.
                }

                if (record is null)
                {
                    continue;
                }

                switch (record.Kind)
                {
                    case "header":
                        conversation = new Conversation
                        {
                            Id = record.Id ?? id,
                            Title = record.Title ?? string.Empty,
                            CreatedUtc = record.AtUtc,
                            UpdatedUtc = record.AtUtc,
                            Provider = record.Provider ?? ProviderIds.Anthropic,
                            Model = record.Model ?? string.Empty,
                            Salt = record.Salt ?? string.Empty,
                        };
                        break;
                    case "turn" when conversation is not null && record.Turn is not null:
                        conversation.Turns.Add(record.Turn);
                        break;
                    case "totals" when conversation is not null:
                        conversation.CostUsd = record.CostUsd ?? conversation.CostUsd;
                        conversation.Usage = record.Usage ?? conversation.Usage;
                        conversation.Model = record.Model ?? conversation.Model;
                        conversation.Title = record.Title ?? conversation.Title;
                        conversation.UpdatedUtc = record.AtUtc;
                        break;
                }
            }

            return conversation;
        }

        /// <summary>The history, most recent first.</summary>
        public IReadOnlyList<ConversationSummary> List()
        {
            if (!File.Exists(IndexPath))
            {
                return Array.Empty<ConversationSummary>();
            }

            try
            {
                var entries = JsonSerializer.Deserialize<List<ConversationSummary>>(File.ReadAllText(IndexPath, Encoding.UTF8), RpcCodec.Options) ?? new List<ConversationSummary>();
                return entries.Where(e => File.Exists(FileOf(e.Id))).OrderByDescending(e => e.UpdatedUtc).ToList();
            }
            catch (JsonException)
            {
                return Array.Empty<ConversationSummary>();
            }
        }

        public void Delete(string id)
        {
            if (File.Exists(FileOf(id)))
            {
                File.Delete(FileOf(id));
            }

            WriteIndex(List().Where(e => e.Id != id).ToList());
        }

        /// <summary>« Tout effacer ».</summary>
        public void DeleteAll()
        {
            if (Directory.Exists(_folder))
            {
                foreach (var file in Directory.EnumerateFiles(_folder, "*.jsonl"))
                {
                    File.Delete(file);
                }

                if (File.Exists(IndexPath))
                {
                    File.Delete(IndexPath);
                }
            }
        }

        private void UpdateIndex(Conversation conversation)
        {
            var entries = List().Where(e => e.Id != conversation.Id).ToList();
            entries.Add(new ConversationSummary { Id = conversation.Id, Title = conversation.Title, UpdatedUtc = conversation.UpdatedUtc, Model = conversation.Model, CostUsd = conversation.CostUsd });
            WriteIndex(entries);
        }

        private void WriteIndex(List<ConversationSummary> entries)
        {
            Directory.CreateDirectory(_folder);
            var temporary = IndexPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(entries, RpcCodec.IndentedOptions), Encoding.UTF8);
            if (File.Exists(IndexPath))
            {
                File.Delete(IndexPath);
            }

            File.Move(temporary, IndexPath);
        }

        private static string Line(ConversationRecord record) => JsonSerializer.Serialize(record, RpcCodec.Options) + "\n";

        private sealed class ConversationRecord
        {
            public string Kind { get; set; } = string.Empty;

            public string? Id { get; set; }

            public string? Title { get; set; }

            public DateTime AtUtc { get; set; }

            public string? Provider { get; set; }

            public string? Model { get; set; }

            public string? Salt { get; set; }

            public ChatTurn? Turn { get; set; }

            public decimal? CostUsd { get; set; }

            public UsageInfo? Usage { get; set; }
        }
    }
}
