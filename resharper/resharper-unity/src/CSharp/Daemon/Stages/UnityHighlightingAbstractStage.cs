using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using JetBrains.Application.Settings;
using JetBrains.Diagnostics;
using JetBrains.ReSharper.Daemon.CallGraph;
using JetBrains.ReSharper.Daemon.CSharp.CallGraph;
using JetBrains.ReSharper.Daemon.CSharp.Stages;
using JetBrains.ReSharper.Daemon.UsageChecking;
using JetBrains.ReSharper.Feature.Services.CSharp.Daemon;
using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Plugins.Unity.CSharp.Daemon.Stages.Analysis;
using JetBrains.ReSharper.Plugins.Unity.CSharp.Daemon.Stages.BurstCodeAnalysis.CallGraph;
using JetBrains.ReSharper.Plugins.Unity.CSharp.Daemon.Stages.Highlightings.IconsProviders;
using JetBrains.ReSharper.Plugins.Unity.CSharp.Daemon.Stages.PerformanceCriticalCodeAnalysis.Analyzers;
using JetBrains.ReSharper.Plugins.Unity.CSharp.Daemon.Stages.PerformanceCriticalCodeAnalysis.CallGraph;
using JetBrains.ReSharper.Plugins.Unity.ProjectModel;
using JetBrains.ReSharper.Plugins.Unity.Settings;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.Util;

namespace JetBrains.ReSharper.Plugins.Unity.CSharp.Daemon.Stages
{
    public abstract class UnityHighlightingAbstractStage : CSharpDaemonStageBase
    {
        private readonly CallGraphSwaExtensionProvider myCallGraphSwaExtensionProvider;
        private readonly PerformanceCriticalCodeCallGraphMarksProvider myPerformanceCriticalCodeCallGraphMarksProvider;
        private readonly CallGraphBurstMarksProvider myCallGraphBurstMarksProvider;
        protected readonly IEnumerable<IUnityDeclarationHighlightingProvider> HiglightingProviders;
        protected readonly IEnumerable<IUnityProblemAnalyzer> PerformanceProblemAnalyzers;
        protected readonly UnityApi API;
        private readonly UnityCommonIconProvider myCommonIconProvider;
        private readonly IElementIdProvider myProvider;
        protected readonly ILogger Logger;

        protected UnityHighlightingAbstractStage(CallGraphSwaExtensionProvider callGraphSwaExtensionProvider,
            PerformanceCriticalCodeCallGraphMarksProvider performanceCriticalCodeCallGraphMarksProvider,
            CallGraphBurstMarksProvider callGraphBurstMarksProvider,
            IEnumerable<IUnityDeclarationHighlightingProvider> higlightingProviders,
            IEnumerable<IUnityProblemAnalyzer> performanceProblemAnalyzers, UnityApi api,
            UnityCommonIconProvider commonIconProvider, IElementIdProvider provider, ILogger logger)
        {
            myCallGraphSwaExtensionProvider = callGraphSwaExtensionProvider;
            myPerformanceCriticalCodeCallGraphMarksProvider = performanceCriticalCodeCallGraphMarksProvider;
            myCallGraphBurstMarksProvider = callGraphBurstMarksProvider;
            HiglightingProviders = higlightingProviders;
            PerformanceProblemAnalyzers = performanceProblemAnalyzers;
            API = api;
            myCommonIconProvider = commonIconProvider;
            myProvider = provider;
            Logger = logger;
        }

        protected override IDaemonStageProcess CreateProcess(IDaemonProcess process,
            IContextBoundSettingsStore settings,
            DaemonProcessKind processKind, ICSharpFile file)
        {

            return new UnityHighlightingProcess(process, file, myCallGraphSwaExtensionProvider,
                myCallGraphBurstMarksProvider,PerformanceProblemAnalyzers,processKind,
                myProvider ,Logger);
        }
    }

    public class UnityHighlightingProcess : CSharpDaemonStageProcessBase
    {
        private readonly CallGraphSwaExtensionProvider myCallGraphSwaExtensionProvider;
        private readonly CallGraphBurstMarksProvider myCallGraphBurstMarksProvider;
        private readonly DaemonProcessKind myProcessKind;
        private readonly IElementIdProvider myProvider;
        private readonly ILogger myLogger;
        private readonly JetHashSet<IMethod> myEventFunctions;
        private readonly HashSet<IDeclaredElement> myCollectedRootElements = new HashSet<IDeclaredElement>();

        private readonly Dictionary<UnityProblemAnalyzerContext, List<IUnityProblemAnalyzer>>
            myProblemAnalyzersByContext;

        private readonly Stack<UnityProblemAnalyzerContext> myProblemAnalyzerContexts =
            new Stack<UnityProblemAnalyzerContext>();

        private readonly Stack<UnityProblemAnalyzerContext> myProhibitedContexts =
            new Stack<UnityProblemAnalyzerContext>();

        public UnityHighlightingProcess([NotNull] IDaemonProcess process, [NotNull] ICSharpFile file,
            CallGraphSwaExtensionProvider callGraphSwaExtensionProvider,
            CallGraphBurstMarksProvider callGraphBurstMarksProvider,
            IEnumerable<IUnityProblemAnalyzer> performanceProblemAnalyzers,
            DaemonProcessKind processKind, IElementIdProvider provider,
            ILogger logger)
            : base(process, file)
        {
            myCallGraphSwaExtensionProvider = callGraphSwaExtensionProvider;
            myCallGraphBurstMarksProvider = callGraphBurstMarksProvider;
            myProcessKind = processKind;
            myProvider = provider;
            myLogger = logger;

            myEventFunctions = DaemonProcess.CustomData.GetData(UnityEventFunctionAnalyzer.UnityEventFunctionNodeKey)
                ?.Where(t => t != null && t.IsValid()).ToJetHashSet();

            DaemonProcess.CustomData.PutData(UnityEventFunctionAnalyzer.UnityEventFunctionNodeKey, myEventFunctions);

            myProblemAnalyzersByContext = performanceProblemAnalyzers.GroupBy(t => t.Context)
                .ToDictionary(t => t.Key, t => t.ToList());
        }

        public override void Execute(Action<DaemonStageResult> committer)
        {
            var highlightingConsumer = new FilteringHighlightingConsumer(DaemonProcess.SourceFile, File,
                DaemonProcess.ContextBoundSettingsStore);
            File.ProcessThisAndDescendants(this, highlightingConsumer);

            committer(new DaemonStageResult(highlightingConsumer.Highlightings));
        }

        private UnityProblemAnalyzerContext GetProblemAnalyzerContext(ITreeNode element)
        {
            var res = new UnityProblemAnalyzerContext();
            if (IsBurstDeclaration(element))
                res |= UnityProblemAnalyzerContext.BURST_CONTEXT;
            return res;
        }

        private bool IsProhibitedNode(ITreeNode node)
        {
            switch (node)
            {
                case IThrowStatement _:
                case IThrowExpression _:
                    return true;
                default:
                    return false;
            }
        }

        private UnityProblemAnalyzerContext GetProhibitedContexts(ITreeNode node)
        {
            var context = UnityProblemAnalyzerContext.NONE;
            if (node is IThrowStatement || node is IThrowExpression)
                context |= UnityProblemAnalyzerContext.BURST_CONTEXT;
            return context;
        }

        public void CollectRootElements(ITreeNode node)
        {
            var roots = myCallGraphBurstMarksProvider.GetRootMarksFromNode(node, null);
            foreach (var element in roots)
                myCollectedRootElements.Add(element);
        }

        public override void ProcessBeforeInterior(ITreeNode element, IHighlightingConsumer consumer)
        {
            // it's ok that creating context does not force new prohibiting context
            // reason: prohibiting context has higher priority than creating. example: burst
            CollectRootElements(element);
            if (IsFunctionNode(element))
                myProblemAnalyzerContexts.Push(GetProblemAnalyzerContext(element));
            if (IsProhibitedNode(element))
                myProhibitedContexts.Push(GetProhibitedContexts(element));

            try
            {
                if (myProblemAnalyzerContexts.Count > 0)
                {
                    const byte end = UnityProblemAnalyzerContextUtil.UnityProblemAnalyzerContextSize;
                    var possibleContexts = myProblemAnalyzerContexts.Peek();
                    var prohibitedContexts = UnityProblemAnalyzerContext.NONE;
                    if (myProhibitedContexts.Count > 0)
                        prohibitedContexts = myProhibitedContexts.Peek();
                    for (byte context = 1, index = 0; index < end; index++, context <<= 1)
                    {
                        var enumContext = (UnityProblemAnalyzerContext) context;
                        if (possibleContexts.HasFlag(enumContext) && !prohibitedContexts.HasFlag(enumContext))
                        {
                            //be aware of https://johnthiriet.com/back-to-basics-csharp-casting-an-integer-into-an-enumeration/
                            foreach (var performanceProblemAnalyzer in myProblemAnalyzersByContext[enumContext])
                            {
                                performanceProblemAnalyzer.RunInspection(element, DaemonProcess, myProcessKind,
                                    consumer);
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                myLogger.Error(exception, "An exception occured during performance problem analyzer execution");
            }
        }

        public override void ProcessAfterInterior(ITreeNode element, IHighlightingConsumer consumer)
        {
            base.ProcessAfterInterior(element, consumer);
            if (IsFunctionNode(element))
            {
                Assertion.Assert(myProblemAnalyzerContexts.Count > 0, "myProblemAnalyzerContexts.Count > 0");
                myProblemAnalyzerContexts.Pop();
            }

            if (IsProhibitedNode(element))
            {
                Assertion.Assert(myProhibitedContexts.Count > 0, "myProhibitedContexts.Count > 0");
                myProhibitedContexts.Pop();
            }
        }


        private static bool IsFunctionNode(ITreeNode node)
        {
            switch (node)
            {
                case IFunctionDeclaration _:
                case ICSharpClosure _:
                    return true;
                default:
                    return false;
            }
        }
        
        private bool IsBurstDeclaration(ITreeNode element)
        {
            return IsRootDeclaration(element,
                declaration => myCollectedRootElements.Contains(declaration.DeclaredElement),
                myCallGraphBurstMarksProvider.Id);
        }

        private bool IsRootDeclaration(ITreeNode node, 
            Func<ICSharpDeclaration, bool> isRootedPredicate, 
            CallGraphRootMarksProviderId rootMarksProviderId)
        {
            if (!(node is ICSharpDeclaration declaration))
                return false;
            var declaredElement = declaration.DeclaredElement;
            if (declaredElement == null)
                return false;
            var isRooted = isRootedPredicate(declaration);
            var isGlobalStage = myProcessKind == DaemonProcessKind.GLOBAL_WARNINGS;
            if (!isRooted && isGlobalStage)
            {
                var id = myProvider.GetElementId(declaredElement);
                if (!id.HasValue)
                    return false;
                return myCallGraphSwaExtensionProvider.IsMarkedByCallGraphRootMarksProvider(
                    rootMarksProviderId, isGlobalStage, id.Value);
            }

            return isRooted;
        }
    }
}