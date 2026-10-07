#nullable enable
using System;
using VividWorld.Core.Config;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Util;

namespace VividWorld.Core.Rumors
{
    public static class StepForwardCalculator
    {
        public static double CalculateChance(
            string responseType,
            TraitProfile? traits,
            double? relationTowardSubject,
            FalseRumorsConfig? config,
            out string formulaDesc)
        {
            var cfg = config ?? new FalseRumorsConfig();
            double minChance = cfg.StepForwardMinChance;
            double maxChance = cfg.StepForwardMaxChance;

            if (string.Equals(responseType, "denial", StringComparison.OrdinalIgnoreCase))
            {
                var denialCfg = cfg.Denial;
                double baseChance = denialCfg.BaseChance;
                int valor = traits?.Valor ?? 0;
                int honor = traits?.Honor ?? 0;

                double valorAdj = 0.0;
                if (valor >= 1) valorAdj = denialCfg.ValorBonus;
                else if (valor <= -1) valorAdj = denialCfg.CautiousPenalty;

                double honorAdj = honor >= 1 ? denialCfg.HonorBonus : 0.0;
                double total = baseChance + valorAdj + honorAdj;
                double clamped = Math.Max(minChance, Math.Min(maxChance, total));

                formulaDesc = $"base {baseChance:0.#}" +
                              (valorAdj != 0 ? $" + valor({valor}) {valorAdj:+0.#;-0.#}" : "") +
                              (honorAdj != 0 ? $" + honor({honor}) {honorAdj:+0.#;-0.#}" : "") +
                              $" = {total:0.#} (clamped {clamped:0.#}%)";
                return clamped;
            }
            else
            {
                var clarifyCfg = cfg.Clarify;
                double baseChance = clarifyCfg.BaseChance;
                int honor = traits?.Honor ?? 0;
                double honorAdj = honor >= 1 ? clarifyCfg.HonorBonus : 0.0;

                double rel = relationTowardSubject ?? 0.0;
                double relAdj = 0.0;
                if (rel >= clarifyCfg.FriendRelation) relAdj = clarifyCfg.FriendBonus;
                else if (rel <= clarifyCfg.HostileRelation) relAdj = clarifyCfg.HostilePenalty;

                double total = baseChance + honorAdj + relAdj;
                double clamped = Math.Max(minChance, Math.Min(maxChance, total));

                formulaDesc = $"base {baseChance:0.#}" +
                              (honorAdj != 0 ? $" + honor({honor}) {honorAdj:+0.#;-0.#}" : "") +
                              (relAdj != 0 ? $" + rel({rel:0.#}) {relAdj:+0.#;-0.#}" : "") +
                              $" = {total:0.#} (clamped {clamped:0.#}%)";
                return clamped;
            }
        }

        public static double CalculateChance(
            bool isAccused,
            TraitProfile? traits,
            double? relationTowardSubject = null,
            FalseRumorsConfig? config = null)
        {
            string type = isAccused ? "denial" : "clarification";
            return CalculateChance(type, traits, relationTowardSubject, config, out _);
        }

        public static double CalculateChance(
            string responseType,
            TraitProfile? traits,
            double? relationTowardSubject = null,
            FalseRumorsConfig? config = null)
        {
            return CalculateChance(responseType, traits, relationTowardSubject, config, out _);
        }

        public static bool Roll(
            double chance,
            long campaignSeed,
            string eventId,
            string heroId,
            IDeterministicRng? rng = null)
        {
            return Roll(chance, campaignSeed, eventId, heroId, rng ?? new SplitMix64Rng(), out _);
        }

        public static bool Roll(
            double chance,
            long campaignSeed,
            string eventId,
            string heroId,
            IDeterministicRng rng,
            out double rolledValue)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            long seed = RumorSeed.Of(campaignSeed, eventId, heroId, "stepForward");
            rolledValue = rng.NextDouble(seed) * 100.0;
            return rolledValue < chance;
        }
    }
}
