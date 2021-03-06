using JetBrains.Application.Settings;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ProjectModel.Caches;
using JetBrains.ReSharper.Plugins.Unity.Rider;

namespace JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.Modules
{
    [SolutionComponent]
    public class RiderUnityYamlDisableStrategy : UnityYamlDisableStrategy
    {
        private readonly UnityHost myUnityHost;

        public RiderUnityYamlDisableStrategy(Lifetime lifetime, ISolution solution, SolutionCaches solutionCaches, ISettingsStore settingsStore,
                                             AssetIndexingSupport assetIndexingSupport, UnityHost unityHost)
            : base(lifetime, solution, solutionCaches, settingsStore, assetIndexingSupport)
        {
            myUnityHost = unityHost;

            myUnityHost.PerformModelAction(t =>
                t.EnableYamlParsing.Advise(lifetime, _ => assetIndexingSupport.IsEnabled.Value = true));
        }

        protected override void NotifyYamlParsingDisabled()
        {
            myUnityHost.PerformModelAction(t => t.NotifyYamlHugeFiles());
        }
    }
}