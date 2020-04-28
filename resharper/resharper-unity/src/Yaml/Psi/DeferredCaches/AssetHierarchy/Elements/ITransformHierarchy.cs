using JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.DeferredCaches.AssetHierarchy.References;

namespace JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.DeferredCaches.AssetHierarchy.Elements
{
    public interface ITransformHierarchy : IComponentHierarchy
    {
        // TODO : think about string only parent anchor, because file id is stored in Location
        LocalReference Parent { get; }
        int RootIndex { get; }
    }
}