namespace AwtrixSharpWeb.Domain
{
    /// <summary>
    /// Local data settings. The historical key is Settings:DATA_DIRECTORY (env AWTRIXSHARP_SETTINGS__DATA_DIRECTORY),
    /// which is not a valid property name, so it is read explicitly rather than bound (CR-14).
    /// </summary>
    public class DataSettings
    {
        public const string DataDirectoryKey = "Settings:DATA_DIRECTORY";
        public const string DataDirectoryEnvironmentVariable = "AWTRIXSHARP_SETTINGS__DATA_DIRECTORY";

        /// <summary>Folder holding optional trip cache files; blank disables the cache</summary>
        public string? DataDirectory { get; set; }

        public DataSettings WithEnvironmentFallback()
        {
            if (string.IsNullOrWhiteSpace(DataDirectory))
            {
                DataDirectory = Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable);
            }

            return this;
        }
    }
}
