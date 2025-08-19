namespace AwtrixSharpWeb.Interfaces
{
    public interface IAppConfig
    {
        string Name { get; }

        string Get(string key);
    }
}