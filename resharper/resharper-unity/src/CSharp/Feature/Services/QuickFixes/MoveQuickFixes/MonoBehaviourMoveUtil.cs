using System;
using System.Linq;
using JetBrains.Annotations;
using JetBrains.Application.Threading;
using JetBrains.ReSharper.Plugins.Unity.CSharp.Psi.Resolve;
using JetBrains.ReSharper.Plugins.Unity.Utils;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp;
using JetBrains.ReSharper.Psi.CSharp.Parsing;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.CSharp.Util;
using JetBrains.ReSharper.Psi.ExtensionsAPI.Resolve.Managed;
using JetBrains.ReSharper.Psi.Naming.Extentions;
using JetBrains.ReSharper.Psi.Naming.Settings;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.ReSharper.Resources.Shell;
using JetBrains.Util;

namespace JetBrains.ReSharper.Plugins.Unity.CSharp.Feature.Services.QuickFixes.MoveQuickFixes
{
    internal static class MonoBehaviourMoveUtil
    {
        [CanBeNull]
        public static IMethodDeclaration GetMonoBehaviourMethod([NotNull] IClassDeclaration classDeclaration, [NotNull] string name)
        {
            classDeclaration.GetPsiServices().Locks.AssertReadAccessAllowed();
            
            return classDeclaration.MethodDeclarations.FirstOrDefault(t => t.NameIdentifier.Name.Equals(name));
        }
        
        [NotNull]
        public static IMethodDeclaration GetOrCreateMethod([NotNull] IClassDeclaration classDeclaration, [NotNull] string methodName)
        {
            classDeclaration.GetPsiServices().Locks.AssertReadAccessAllowed();
            
            var result = GetMonoBehaviourMethod(classDeclaration, methodName);
            if (result == null)
            {
                var factory = CSharpElementFactory.GetInstance(classDeclaration);
                var declaration = (IMethodDeclaration)factory.CreateTypeMemberDeclaration("void $0(){}", methodName);

                result = classDeclaration.AddClassMemberDeclarationAfter(declaration, classDeclaration.FieldDeclarations.FirstOrDefault());
               
            }
            return result;
        }

        public static bool IsExpressionAccessibleInMethod([NotNull]ICSharpExpression expression, [NotNull] string methodName)
        {
            expression.GetPsiServices().Locks.AssertReadAccessAllowed();
            
            if (!expression.IsValid())
                return false;
            
            var methodDeclaration = expression.GetContainingNode<IMethodDeclaration>();
            if (methodDeclaration == null)
                return false;
            
            var statement = expression.GetContainingStatementLike();
            if (statement == null)
                return false;

            return IsAvailableToMoveFromMethodToMethod(expression, methodName);
        }

        /// <summary>
        /// Is node available to move outside the loop
        /// </summary>
        public static bool IsAvailableToMoveFromLoop([NotNull] ITreeNode toMove, [NotNull] ILoopStatement loop)
        {
            toMove.GetPsiServices().Locks.AssertReadAccessAllowed();

            var sourceFile = toMove.GetSourceFile();
            if (sourceFile == null)
                return false;
            
            var loopStartOffset = loop.GetTreeStartOffset().Offset;
            return IsAvailableToMoveInner(toMove, declaredElement => 
                    declaredElement.GetDeclarationsIn(sourceFile)
                    .FirstOrDefault(declaration => declaration.GetSourceFile() == sourceFile)?.GetTreeStartOffset().Offset < loopStartOffset
            );
        }

        /// <summary>
        /// Is node available to move from one method to another in MonoBehaviour class 
        /// </summary>
        public static bool IsAvailableToMoveFromMethodToMethod([NotNull] ITreeNode toMove, [NotNull] string methodName)
        {
            toMove.GetPsiServices().Locks.AssertReadAccessAllowed();
            var classDeclaration = toMove.GetContainingNode<IClassDeclaration>();
            if (classDeclaration == null)
                return false;

            var modifiers = classDeclaration.ModifiersList;
            
            if (modifiers == null || !modifiers.ModifiersEnumerable.Any(t => t.GetTokenType().Equals(CSharpTokenType.ABSTRACT_KEYWORD)))
                return IsAvailableToMoveInner(toMove);

            var method = GetMonoBehaviourMethod(classDeclaration, methodName);
            if (method == null)
                return IsAvailableToMoveInner(toMove);;

            var methodModifiers = method.ModifiersList;
            if (methodModifiers == null || !methodModifiers.ModifiersEnumerable.Any(t => t.GetTokenType().Equals(CSharpTokenType.ABSTRACT_KEYWORD)))
                return IsAvailableToMoveInner(toMove);
            
            return false;
        }
        
        private static bool IsAvailableToMoveInner([NotNull] ITreeNode toMove, [CanBeNull] Func<IDeclaredElement, bool> isElementIgnored = null)
        {
            var nodeEnumerator = toMove.ThisAndDescendants();
            while (nodeEnumerator.MoveNext())
            {
                var current = nodeEnumerator.Current;
                IDeclaredElement declaredElement;
                switch (current)
                {
                    case IDeclaration declaration:
                        declaredElement = declaration.DeclaredElement;
                        break;
                    case IReferenceExpression referenceExpression:
                        declaredElement = referenceExpression.Reference.Resolve().DeclaredElement;
                        break;
                    default:
                        declaredElement = null;
                        break;
                }
                
                if (declaredElement == null || isElementIgnored != null && isElementIgnored(declaredElement))
                    continue;
                // if declared element is local, we can't move our expression outside the method
                switch (declaredElement)
                {
                    case ILocalVariableDeclaration _:                  
                    case IParameterDeclaration _:
                    case ILocalFunctionDeclaration _:
                    case IDelegateDeclaration _:
                    case ILambdaExpression _:
                    case IAnonymousFunctionExpression _:
                    case IQueryParameterPlatform _:
                        return false;
                }
            }
            return true;
        }

        public static void MoveToMethodWithFieldIntroduction([NotNull]IClassDeclaration classDeclaration, [NotNull]ICSharpExpression expression, [NotNull] string methodName, string fieldName = null)
        {
            classDeclaration.GetPsiServices().Locks.AssertReadAccessAllowed();
            
            var methodDeclaration = GetOrCreateMethod(classDeclaration, methodName);
            MoveToMethodWithFieldIntroduction(classDeclaration, methodDeclaration, expression, fieldName);
        }
        
        public static void MoveToMethodWithFieldIntroduction([NotNull]IClassDeclaration classDeclaration,[NotNull] IMethodDeclaration methodDeclaration,
            [NotNull]ICSharpExpression expression, string fieldName = null)
        {
            classDeclaration.GetPsiServices().Locks.AssertReadAccessAllowed();
            
            var result = GetDeclaredElementFromParentDeclaration(expression);
            
            var factory = CSharpElementFactory.GetInstance(classDeclaration);

            var type = expression.Type(new ResolveContext(classDeclaration.GetPsiModule()));
            if (type.IsUnknown)
                type = TypeFactory.CreateTypeByCLRName("System.Object", classDeclaration.GetPsiModule());

            var baseName = fieldName ?? CreateBaseName(expression, result);
            var name = NamingUtil.GetUniqueName(expression, baseName, NamedElementKinds.PrivateInstanceFields,
                collection => collection.Add(expression, new EntryOptions { }),
                de => !de.Equals(result));

            var isVoid = type.IsVoid();

            if (!isVoid)
            {
                var field = factory.CreateFieldDeclaration(type, name);
                field.SetAccessRights(AccessRights.PRIVATE);

                classDeclaration.AddClassMemberDeclaration(field);
            }

            var initialization = isVoid ? factory.CreateStatement("$1;", name, expression.CopyWithResolve()) : 
                factory.CreateStatement("$0 = $1;", name, expression.CopyWithResolve());
            
            var body = methodDeclaration.EnsureStatementMemberBody();
            body.AddStatementAfter(initialization, null);

            RenameOldUsages(expression, result, name, factory);
        }

        public static void RenameOldUsages([NotNull]ICSharpExpression originExpression, [CanBeNull]IDeclaredElement localVariableDeclaredElement, 
            [NotNull] string newName, [NotNull] CSharpElementFactory factory)
        {
            originExpression.GetPsiServices().Locks.AssertReadAccessAllowed();

            var statement = ExpressionStatementNavigator.GetByExpression(originExpression);
            if (statement != null)
            {
                statement.RemoveOrReplaceByEmptyStatement();
            }
            else
            {
                if (localVariableDeclaredElement == null)
                {
                    originExpression.ReplaceBy(factory.CreateReferenceExpression(newName));
                }
                else if (!newName.Equals(localVariableDeclaredElement.ShortName))
                {
                    var provider = DefaultUsagesProvider.Instance;
                    var usages = provider.GetUsages(localVariableDeclaredElement,
                        originExpression.GetContainingNode<IMethodDeclaration>().NotNull("scope != null"));
                    originExpression.GetContainingStatement().NotNull("expression.GetContainingStatement() != null")
                        .RemoveOrReplaceByEmptyStatement();
                    foreach (var usage in usages)
                    {
                        if (usage.IsValid() && usage is IReferenceExpression node)
                            node.ReplaceBy(factory.CreateReferenceExpression(newName));
                    }
                }
            }
        }

        /// <summary>
        /// If current expression is used as initializer for local variable, declared element for this variable will be returned
        /// </summary>
        public static IDeclaredElement GetDeclaredElementFromParentDeclaration([NotNull] ICSharpExpression expression)
        {
            expression.GetPsiServices().Locks.AssertReadAccessAllowed();
            
            var localVariableDeclaration =
                LocalVariableDeclarationNavigator.GetByInitial(
                    ExpressionInitializerNavigator.GetByValue(expression.GetContainingParenthesizedExpression()));
            return localVariableDeclaration?.DeclaredElement;
        }

        public static string CreateBaseName([NotNull]ICSharpExpression toMove, [CanBeNull] IDeclaredElement variableDeclaration)
        {
            toMove.GetPsiServices().Locks.AssertReadAccessAllowed();
            
            string baseName = null;

            if (variableDeclaration != null)
            {
                baseName = variableDeclaration.ShortName;
            }
            else
            {
                if (toMove is IInvocationExpression invocationExpression)
                {
                    var arguments = invocationExpression.Arguments;
                    if (arguments.Count > 0)
                    {
                        var argument = arguments[0].Value;
                        var reference = argument.GetReferences<UnityObjectTypeOrNamespaceReference>().FirstOrDefault();
                        if (reference != null && reference.Resolve().ResolveErrorType.IsAcceptable)    
                        {
                            baseName = (argument.ConstantValue.Value as string).NotNull(
                                "argument.ConstantValue.Value as string != null");
                        }
                    }
                }
            }

            return baseName;
        }
    }
}