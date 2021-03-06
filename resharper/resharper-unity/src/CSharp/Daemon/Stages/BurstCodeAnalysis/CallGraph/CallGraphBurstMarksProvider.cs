using System.Collections.Generic;
using System.Linq;
using JetBrains.Application.Threading;
using JetBrains.Diagnostics;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Daemon.CSharp.CallGraph;
using JetBrains.ReSharper.Plugins.Unity.CSharp.Daemon.Stages.BurstCodeAnalysis.Analyzers;
using JetBrains.ReSharper.Plugins.Unity.CSharp.Daemon.Stages.PerformanceCriticalCodeAnalysis;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.CSharp.Util;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.ReSharper.Psi.Util;
using JetBrains.Util;
using JetBrains.Util.DataStructures.Collections;
using static JetBrains.ReSharper.Plugins.Unity.CSharp.Daemon.Stages.BurstCodeAnalysis.BurstCodeAnalysisUtil;

namespace JetBrains.ReSharper.Plugins.Unity.CSharp.Daemon.Stages.BurstCodeAnalysis.CallGraph
{
    [SolutionComponent]
    public class CallGraphBurstMarksProvider : CallGraphRootMarksProviderBase
    {
        private readonly List<IBurstBannedAnalyzer> myBurstBannedAnalyzers;

        public CallGraphBurstMarksProvider(ISolution solution,
            IEnumerable<IBurstBannedAnalyzer> prohibitedContextAnalyzers)
            : base(nameof(CallGraphBurstMarksProvider),
                new CallGraphBurstPropagator(solution, nameof(CallGraphBurstMarksProvider)))
        {
            myBurstBannedAnalyzers = prohibitedContextAnalyzers.ToList();
        }

        public override LocalList<IDeclaredElement> GetRootMarksFromNode(ITreeNode currentNode,
            IDeclaredElement containingFunction)
        {
            var result = new HashSet<IDeclaredElement>();
            switch (currentNode)
            {
                case IStructDeclaration structDeclaration
                    when structDeclaration.DeclaredElement is IStruct @struct &&
                         @struct.HasAttributeInstance(KnownTypes.BurstCompileAttribute, AttributesSource.Self):
                {
                    var superTypes = @struct.GetAllSuperTypes();
                    var interfaces = superTypes
                        .Where(declaredType => declaredType.IsInterfaceType())
                        .Select(declaredType => declaredType.GetTypeElement())
                        .WhereNotNull()
                        .Where(typeElement => typeElement.HasAttributeInstance(KnownTypes.JobProducerAttrubyte, AttributesSource.Self))
                        .ToList();
                    var structMethods = @struct.Methods.ToList();
                    
                    foreach (var @interface in interfaces)
                    {
                        var interfaceMethods = @interface.Methods.ToList();
                        var overridenMethods = structMethods
                            .Where(m => interfaceMethods.Any(m.OverridesOrImplements))
                            .ToList();
                        
                        foreach (var overridenMethod in overridenMethods)
                            result.Add(overridenMethod);
                    }

                    break;
                }
                case IInvocationExpression invocationExpression:
                {
                    if (!(CallGraphUtil.GetCallee(invocationExpression) is IMethod method))
                        break;
                    var containingType = method.GetContainingType();
                    if (containingType == null)
                        break;
                    if (method.Parameters.Count == 1 &&
                        method.TypeParameters.Count == 1 &&
                        method.ShortName == "CompileFunctionPointer" &&
                        containingType.GetClrName().Equals(KnownTypes.BurstCompiler))
                    {
                        var argumentList = invocationExpression.ArgumentList.Arguments;
                        if (argumentList.Count != 1)
                            break;
                        var argument = argumentList[0].Value;
                        if (argument == null)
                            break;
                        var possibleDeclaredElements = CallGraphUtil.ExtractCallGraphDeclaredElements(argument);
                        foreach (var declaredElement in possibleDeclaredElements)
                        {
                            if (declaredElement != null)
                                result.Add(declaredElement);
                        }
                    }

                    break;
                }
            }

            return new LocalList<IDeclaredElement>(result);
        }

        public override LocalList<IDeclaredElement> GetBanMarksFromNode(ITreeNode currentNode,
            IDeclaredElement containingFunction)
        {
            var result = new LocalList<IDeclaredElement>();
            if (containingFunction == null)
                return result;
            var functionDeclaration = currentNode as IFunctionDeclaration;
            var function = functionDeclaration?.DeclaredElement;
            if (function == null)
                return result;
            if (IsBurstContextBannedFunction(function) || CheckBurstBannedAnalyzers(functionDeclaration))
                result.Add(function);
            return result;
        }

        private bool CheckBurstBannedAnalyzers(IFunctionDeclaration node)
        {
            var processor = new BurstBannedProcessor(myBurstBannedAnalyzers);
            node.ProcessDescendants(processor);
            return processor.IsBurstProhibited;
        }
        
        private class BurstBannedProcessor : IRecursiveElementProcessor
        {
            public bool IsBurstProhibited;
            private readonly SeldomInterruptChecker myInterruptChecker = new SeldomInterruptChecker();
            private readonly List<IBurstBannedAnalyzer> myBurstBannedAnalyzers;
            
            public BurstBannedProcessor(List<IBurstBannedAnalyzer> burstBannedAnalyzers)
            {
                myBurstBannedAnalyzers = burstBannedAnalyzers;
            }

            public bool InteriorShouldBeProcessed(ITreeNode element)
            {
                myInterruptChecker.CheckForInterrupt();
                return !IsFunctionNode(element) && !IsBurstContextBannedNode(element);
            }

            public void ProcessBeforeInterior(ITreeNode element)
            {
                foreach (var contextAnalyzer in myBurstBannedAnalyzers)
                {
                    if (contextAnalyzer.Check(element))
                    {
                        IsBurstProhibited = true;
                        return;
                    }
                }

            }

            public void ProcessAfterInterior(ITreeNode element)
            {
            }

            public bool ProcessingIsFinished => IsBurstProhibited;
        }
    }
}