namespace AwtrixSharpWeb.Interfaces
{
    public interface IAppConfig
    {
        string Type { get; }
        string Name { get; }

    }

    public interface IAppKeys
    {

        string Get(string key);
    }

}