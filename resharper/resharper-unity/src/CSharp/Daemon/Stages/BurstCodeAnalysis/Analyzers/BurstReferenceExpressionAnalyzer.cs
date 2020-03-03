using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Plugins.Unity.CSharp.Daemon.Errors;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.ReSharper.Psi.Util;
using static JetBrains.ReSharper.Plugins.Unity.CSharp.Daemon.Stages.BurstCodeAnalysis.BurstCodeAnalysisUtil;

namespace JetBrains.ReSharper.Plugins.Unity.CSharp.Daemon.Stages.BurstCodeAnalysis.Analyzers
{
    [SolutionComponent]
    public class BurstReferenceExpressionAnalyzer : BurstProblemAnalyzerBase<IReferenceExpression>
    {
        protected override void Analyze(IReferenceExpression referenceExpression, IDaemonProcess daemonProcess, DaemonProcessKind kind,
            IHighlightingConsumer consumer)
        {
            var element = referenceExpression.Reference.Resolve().DeclaredElement;

            //here I want to handle next situations
            //1. accessing typemembers, whether static or not, including: properties, fields, events, localVariable, Parameters
            //CGTD: figure w2d with function pointers

            //non auto property are not interested cuz they are not prohibited,
            //and any backing field will be handled inside accessor 
            if (element is IProperty property && property.IsAuto || element is IField || element is IEvent )
            {
                var typeMember = (ITypeMember) element;
                if (referenceExpression.GetAccessType().HasFlag(ExpressionAccessType.Read) && typeMember.IsStatic &&
                    !typeMember.IsReadonly && !typeMember.IsConstant() && !typeMember.IsEnumMember() &&
                    !(typeMember is IProperty prop && !prop.IsWritable && prop.IsReadable))
                {
                    consumer.AddHighlighting(new BurstWarning(referenceExpression.GetDocumentRange(),
                        "read access to static non-readonly element"));
                }
                if (referenceExpression.GetAccessType().HasFlag(ExpressionAccessType.Write) && typeMember.IsStatic)
                {    
                    //there are no static write-only auto properties
                    consumer.AddHighlighting(new BurstWarning(referenceExpression.GetDocumentRange(), "write access to static element"));
                }
            }

            if (element is ITypeOwner typeOwner)
            {
                if (!InBurstAttribute(referenceExpression) &&
                    (!typeOwner.Type().IsSuitableForBurst() ||
                     element is IModifiersOwner modifiersOwner &&
                     (modifiersOwner.IsVirtual || modifiersOwner.IsOverride || modifiersOwner.IsAbstract) 
                     // && !IsQualifierOpenType(referenceExpression) <---- if member is open type and has virtual/abstract/override member, then it has class based constraints, so it cannot be instantiated with struct
                     ))
                {
                    //virtual and abstract cannot be in struct. only override is getHashCode -> function
                    consumer.AddHighlighting(new BurstWarning(referenceExpression.GetDocumentRange(), "accessing to managed object"));
                }
            }
        }
    }
}