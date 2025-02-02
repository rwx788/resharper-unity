﻿using System.Collections.Generic;
using JetBrains.Application;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.LiveTemplates.Context;
using JetBrains.ReSharper.Feature.Services.LiveTemplates.LiveTemplates;
using JetBrains.ReSharper.Feature.Services.LiveTemplates.Scope;
using JetBrains.ReSharper.Plugins.Unity.Core.ProjectModel;
using JetBrains.ReSharper.Plugins.Unity.UnityEditorIntegration.Api;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp;
using JetBrains.ReSharper.Psi.Files;
using JetBrains.ReSharper.Psi.Tree;

namespace JetBrains.ReSharper.Plugins.Unity.CSharp.Feature.Services.LiveTemplates.Scope
{
    [ShellComponent]
    public class UnityTypeScopeProvider : ScopeProvider
    {
        public UnityTypeScopeProvider()
        {
            Creators.Add(TryToCreate<MustBeInUnitySerializableType>);
            Creators.Add(TryToCreate<MustBeInUnityType>);
            Creators.Add(TryToCreate<MustBeInUnityCSharpFile>);
        }

        public override IEnumerable<ITemplateScopePoint> ProvideScopePoints(TemplateAcceptanceContext context)
        {
            if (!context.GetProject().IsUnityProject())
                yield break;

            var sourceFile = context.SourceFile;
            if (sourceFile == null)
                yield break;

            var caretOffset = context.CaretOffset;
            var prefix = LiveTemplatesManager.GetPrefix(caretOffset);
            var documentOffset = caretOffset - prefix.Length;
            if (!documentOffset.IsValid())
                yield break;

            var psiFile = sourceFile.GetPrimaryPsiFile();
            if (psiFile == null)
                yield break;

            if (psiFile.Language.Is<CSharpLanguage>())
                yield return new MustBeInUnityCSharpFile();

            var element = psiFile.FindTokenAt(caretOffset - prefix.Length);
            var typeDeclaration = element?.GetContainingNode<ITypeDeclaration>();

            var unityApi = context.Solution.GetComponent<UnityApi>();
            if (unityApi.IsUnityType(typeDeclaration?.DeclaredElement))
            {
                yield return new MustBeInUnityType();
                yield return new MustBeInUnitySerializableType();
            }
            else if (unityApi.IsSerializableTypeDeclaration(typeDeclaration?.DeclaredElement))
                yield return new MustBeInUnitySerializableType();
        }
    }
}