using AwtrixSharpWeb.Interfaces;

namespace AwtrixSharpWeb.Apps.Configs
{
    public class AppConfigKeys : Dictionary<string, string>, IAppKeys
    {
        public AppConfigKeys()
        {
                
        }

        public string Get(string key)
        {
            if (TryGetValue(key, out var value))
            {
                return value;
            }
            return null;
        }

        /// <summary>
        /// Try and get, fall back to env var override
        /// </summary>
        public string Get(string key, string environmentVariable)
        {
            var value = Get(key);
            if (string.IsNullOrWhiteSpace(value))
            {
                value = Environment.GetEnvironmentVariable(environmentVariable);
            }
            return value;
        }

        public AppConfigKeys Clone()
        {
            var clone = new AppConfigKeys();
            foreach (var key in this.Keys) {
                clone.Add(key, this[key]);
            };
            return clone;
        }

        public override string ToString()
        {
            if (this.Keys.Count == 0)
            {
                return "<empty>";
            }
            return string.Join(
                "; ",
                this.Select(kvp => $"{kvp.Key}={kvp.Value}")
            );
        }
    }
}
