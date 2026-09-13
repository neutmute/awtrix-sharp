using AwtrixSharpWeb.Domain;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace AwtrixSharpWeb.Apps.Configs
{
    public class ValueMap : Dictionary<string, string>
    {
        public const string MatcherKey = "ValueMatcher";

        /// <summary>
        /// Keys ignore case, matching IConfiguration (CR-22). Clone() uses this constructor, so clones do too.
        /// </summary>
        public ValueMap() : base(StringComparer.OrdinalIgnoreCase)
        {
        }

        public string ValueMatcher
        {
            get => this.TryGetValue(MatcherKey, out var value) ? value : string.Empty;
            set => this[MatcherKey] = value;
        }

        public ValueMap Clone()
        {
            var clone = new ValueMap();
            foreach (var key in this.Keys)
            {
                clone.Add(key, this[key]);
            }
            return clone;
        }

        public bool IsMatch(string input)
        {
            if (string.IsNullOrEmpty(ValueMatcher) || string.IsNullOrEmpty(input))
                return false;

            try
            {
                return Regex.IsMatch(input, ValueMatcher, RegexOptions.IgnoreCase);
            }
            catch
            {
                // Fallback to string comparison if regex is invalid (reported once at load by GetConfigurationProblems)
                return input.Contains(ValueMatcher, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Apply every mapped key to <paramref name="message"/> via the static setter table.
        /// Unknown keys and invalid values are skipped; they were already reported at Warning when the app
        /// was constructed, so only Debug is logged here (Decorate can run every second).
        /// </summary>
        public void Decorate(AwtrixAppMessage message, ILogger logger)
        {
            foreach (var (key, value) in this)
            {
                if (IsMatcherKey(key))
                {
                    continue;
                }

                if (!ValueMapSetters.TryApply(message, key, value))
                {
                    logger?.LogDebug("ValueMap key '{Key}' with value '{Value}' not applied (unknown key or invalid value)", key, value);
                }
            }
        }

        /// <summary>
        /// Human-readable configuration problems: invalid non-empty regex, unknown keys, invalid values.
        /// An empty ValueMatcher is valid (it never matches, but maps may be used positionally).
        /// </summary>
        public IReadOnlyList<string> GetConfigurationProblems()
        {
            var problems = new List<string>();

            if (!string.IsNullOrEmpty(ValueMatcher))
            {
                try
                {
                    _ = new Regex(ValueMatcher);
                }
                catch (ArgumentException ex)
                {
                    problems.Add($"ValueMatcher '{ValueMatcher}' is not a valid regular expression ({ex.Message}); falling back to a case-insensitive substring match");
                }
            }

            foreach (var (key, value) in this)
            {
                if (IsMatcherKey(key))
                {
                    continue;
                }

                if (!ValueMapSetters.IsKnown(key))
                {
                    problems.Add($"Unknown key '{key}' is ignored");
                }
                else if (!ValueMapSetters.IsValidValue(key, value))
                {
                    problems.Add($"Value '{value}' for key '{key}' is invalid and is ignored");
                }
            }

            return problems;
        }

        public override string ToString()
        {
            return $"ValueMatcher={ValueMatcher}";
        }

        private static bool IsMatcherKey(string key) => string.Equals(key, MatcherKey, StringComparison.OrdinalIgnoreCase);
    }
}
