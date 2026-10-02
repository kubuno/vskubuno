using System;
using System.Collections.Generic;
using Kubuno.Shared.DevAssistant.Logic.Protocol;

namespace Kubuno.Shared.DevAssistant.Logic.Cost
{
    /// <summary>Prices of one model in US dollars per million tokens.</summary>
    public sealed class ModelPrice
    {
        public ModelPrice(decimal input, decimal output, decimal cacheRead, decimal cacheWrite)
        {
            Input = input;
            Output = output;
            CacheRead = cacheRead;
            CacheWrite = cacheWrite;
        }

        public decimal Input { get; }

        public decimal Output { get; }

        public decimal CacheRead { get; }

        /// <summary>5-minute cache writes (1.25 × input).</summary>
        public decimal CacheWrite { get; }
    }

    /// <summary>
    /// The local price table (docs/AI-ASSISTANT.md section 7.3): an estimate, labelled as such in the UI, shipped with the
    /// extension. Unknown models are priced like the most expensive known one so the cost cap errs on the safe side.
    /// </summary>
    public static class PriceTable
    {
        private static readonly Dictionary<string, ModelPrice> Prices = new Dictionary<string, ModelPrice>(StringComparer.OrdinalIgnoreCase)
        {
            ["claude-opus-5-5"] = new ModelPrice(4m, 20m, 0.20m, 5m),
            ["claude-sonnet-5-5"] = new ModelPrice(2m, 10m, 0.20m, 2.5m),
            ["claude-haiku-4-5"] = new ModelPrice(1m, 5m, 0.10m, 1.25m),
            ["claude-opus-5"] = new ModelPrice(5m, 25m, 0.50m, 6.25m),
            ["claude-sonnet-5"] = new ModelPrice(2m, 10m, 0.20m, 2.5m),
            ["claude-fable-5-1"] = new ModelPrice(10m, 50m, 0.25m, 12.5m),
            ["kubuno-fake"] = new ModelPrice(4m, 20m, 0.20m, 5m),
        };

        private static readonly ModelPrice Fallback = new ModelPrice(10m, 50m, 1m, 12.5m);

        /// <summary>The prices of <paramref name="model"/> (fallback: the most expensive tier).</summary>
        public static ModelPrice For(string model) => Prices.TryGetValue(model ?? string.Empty, out var price) ? price : Fallback;

        /// <summary>Whether <paramref name="model"/> has its own entry.</summary>
        public static bool IsKnown(string model) => Prices.ContainsKey(model ?? string.Empty);

        /// <summary>The estimated cost of <paramref name="usage"/> on <paramref name="model"/>.</summary>
        public static decimal Cost(string model, UsageInfo usage)
        {
            var price = For(model);
            return ((usage.InputTokens * price.Input) + (usage.OutputTokens * price.Output) +
                    (usage.CacheReadInputTokens * price.CacheRead) + (usage.CacheCreationInputTokens * price.CacheWrite)) / 1_000_000m;
        }
    }

    /// <summary>
    /// The hard cost cap of a session (section 7.3): the host checks it before every model call and stops the turn once
    /// the session's estimated spend reaches the cap.
    /// </summary>
    public sealed class CostLedger
    {
        public CostLedger(decimal capUsd, decimal spentUsd)
        {
            CapUsd = capUsd;
            SpentUsd = spentUsd;
        }

        public decimal CapUsd { get; }

        /// <summary>Spent in the whole session (before this turn and during it).</summary>
        public decimal SpentUsd { get; private set; }

        /// <summary>Spent during this turn only.</summary>
        public decimal TurnUsd { get; private set; }

        public UsageInfo TurnUsage { get; } = new UsageInfo();

        /// <summary>True when no further model call may start.</summary>
        public bool IsExhausted => CapUsd > 0 && SpentUsd >= CapUsd;

        /// <summary>Records one model call; returns its cost.</summary>
        public decimal Record(string model, UsageInfo usage)
        {
            var cost = PriceTable.Cost(model, usage);
            SpentUsd += cost;
            TurnUsd += cost;
            TurnUsage.Add(usage);
            return cost;
        }

        /// <summary>The share of input tokens served from the cache (0 to 1), for the « cache 92 % » display.</summary>
        public static double CacheHitRate(UsageInfo usage)
        {
            long total = usage.InputTokens + usage.CacheReadInputTokens + usage.CacheCreationInputTokens;
            return total == 0 ? 0 : (double)usage.CacheReadInputTokens / total;
        }
    }
}
