namespace FluxDB.Plugin
{
    public interface IFluxDBPlugin
    {
        string Name { get; }
        string Version { get; }
        string Author { get; }
        string Description { get; }
        void Initialize(IPluginContext context);
        void Shutdown();
    }
}