using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Plugins.Unity.CSharp.Daemon.Stages.PerformanceCriticalCodeAnalysis.Analyzers;
using JetBrains.ReSharper.Psi.Tree;

namespace JetBrains.ReSharper.Plugins.Unity.CSharp.Daemon.Stages.BurstCodeAnalysis.Analyzers
{
    public abstract class BurstProblemAnalyzerBase<T> : UnityProblemAnalyzerBase<T>, IBurstBannedAnalyzer
    {
        public override UnityProblemAnalyzerContext Context { get; } = UnityProblemAnalyzerContext.BURST_CONTEXT;

        protected override void Analyze(T t, IDaemonProcess daemonProcess, DaemonProcessKind kind, IHighlightingConsumer consumer)
        {
            CheckAndAnalyze(t, consumer);
        }

        protected abstract bool CheckAndAnalyze(T t, IHighlightingConsumer consumer);
        public bool Check(ITreeNode node)
        {
            if (node is T t)
                return CheckAndAnalyze(t, null);
            return false;
        }
    }
}