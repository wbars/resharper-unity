using JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.Modules;
using JetBrains.ReSharper.Psi;

namespace JetBrains.ReSharper.Plugins.Unity.Yaml.Psi
{
    public static class UnityYamlPsiSourceFileExtensions
    {
        public static bool IsAsset(this IPsiSourceFile sourceFile)
        {
            return sourceFile is UnityYamlAssetPsiSourceFile || sourceFile.GetLocation().IsAsset();
        }
    }
}