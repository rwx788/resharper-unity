using System.Collections.Generic;
using JetBrains.ReSharper.Daemon.CSharp.CallGraph;
using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Plugins.Unity.CSharp.Daemon.Stages.ContextSystem;
using JetBrains.ReSharper.Plugins.Unity.ProjectModel;
using JetBrains.Util;

namespace JetBrains.ReSharper.Plugins.Unity.CSharp.Daemon.Stages.CallGraphStage
{
    [DaemonStage(GlobalAnalysisStage = true, OverridenStages = new[] {typeof(CallGraphLocalStage)})]
    public class CallGraphGlobalStage : CallGraphAbstractStage
    {
        public CallGraphGlobalStage(CallGraphSwaExtensionProvider swaExtensionProvider,
            IEnumerable<ICallGraphContextProvider> contextProviders,
            IEnumerable<ICallGraphProblemAnalyzer> problemAnalyzers,
            UnityReferencesTracker tracker,
            ILogger logger)
            : base(swaExtensionProvider, contextProviders, problemAnalyzers, tracker, logger)
        {
        }
    }
}