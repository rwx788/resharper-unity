using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Application.Threading;
using JetBrains.Collections.Synchronized;
using JetBrains.Collections.Viewable;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.FeaturesStatistics;
using JetBrains.ReSharper.Plugins.Unity.Core.Psi.Modules;
using JetBrains.ReSharper.Plugins.Unity.Packages;
using JetBrains.ReSharper.Resources.Shell;
using JetBrains.UsageStatistics.FUS.EventLog;
using JetBrains.UsageStatistics.FUS.EventLog.Events;
using JetBrains.UsageStatistics.FUS.EventLog.Fus;
using JetBrains.UsageStatistics.FUS.Utils;
using JetBrains.Util.Collections;

namespace JetBrains.ReSharper.Plugins.Unity.Core.Feature.Services.Fus
{
    [SolutionComponent]
    public class UnityAssetInfoCollector : SolutionUsagesCollector
    {
        private EventLogGroup myGroup;
        private readonly VarargEventId myAssetInformation;
        
        private readonly EnumEventField<FileType> myAssetTypeField;
        private readonly BooleanEventField myReadonlyAssetField;
        private readonly LongEventField myBucketSizeField;
        private readonly IntEventField myBucketCountField;
        
        private readonly EventId2<long, bool> myMetaFileAverage;
        private readonly EventId1<long> myFilesAverage;

        private IViewableProperty<bool> IsReady { get; } = new ViewableProperty<bool>(false);
        private IViewableProperty<bool> InitialUpdateFinished { get; } = new ViewableProperty<bool>(false);

        public UnityAssetInfoCollector(Lifetime lifetime, PackageManager packageManager, FeatureUsageLogger featureUsageLogger)
        {
            myGroup = new EventLogGroup("unity.assets.state", "Unity Asset Information", 1, featureUsageLogger);
            
            myAssetTypeField = EventFields.Enum<FileType>("assetType", "Asset Type");
            myReadonlyAssetField = EventFields.Boolean("isReadonly", "Is Readonly");
            myBucketSizeField = EventFields.Long("bucketSize", "Bucket Size (mb)");
            myBucketCountField = EventFields.Int("bucketCount", "File Count in Bucket");

            myAssetInformation = myGroup.RegisterVarargEvent("assetInformation", "Asset Information",
                myAssetTypeField,
                myReadonlyAssetField,
                myBucketSizeField,
                myBucketCountField
            );

            myMetaFileAverage = myGroup.RegisterEvent("metaAverage", "Meta Files Average", EventFields.Long("average", "Average (bytes)"), EventFields.Boolean("isReadonly", "IsReadonly"));
            myFilesAverage = myGroup.RegisterEvent("assetAverage", "All Asset Files Average", EventFields.Long("average", "Average (mb)"));

            InitialUpdateFinished.AdviseUntil(lifetime, v =>
            {
                if (v)
                {
                    packageManager.IsInitialUpdateFinished.AdviseUntil(lifetime, v2 =>
                    {
                        if (v2)
                        {
                            IsReady.Value = true;
                            return true;
                        }

                        return false;
                    });
                    return true;
                }

                return false;
            });
        }
        
        public override EventLogGroup GetGroup()
        {
            return myGroup;
        }

        public override Task<ISet<MetricEvent>> GetMetricsAsync(Lifetime lifetime)
        {
            var tcs = lifetime.CreateTaskCompletionSource<ISet<MetricEvent>>(TaskCreationOptions.RunContinuationsAsynchronously);

            IsReady.AdviseUntil(lifetime, v =>
            {
                if (v)
                {
                    lifetime.StartBackground(() =>
                    {
                        var hashSet = new HashSet<MetricEvent>();

                        ulong totalSize = 0;
                        ulong metaSize = 0;
                        ulong readonlyMetaSize = 0;
                        
                        var readonlyAssets = new CountingSet<(FileType, ulong)>();
                        var assets = new CountingSet<(FileType, ulong)>();
                        
                        foreach (var statistic in myStatistics)
                        {
                            totalSize += statistic.Length;
                            if (statistic.FileType == FileType.Meta)
                            {
                                if (statistic.IsUserEditable)
                                {
                                    metaSize += statistic.Length;
                                }
                                else
                                {
                                    readonlyMetaSize += statistic.Length;
                                }
                            }

                            if (statistic.IsUserEditable)
                            {
                                assets.Add((statistic.FileType, GetSize(statistic.Length)));
                            }
                            else
                            {
                                readonlyAssets.Add((statistic.FileType, GetSize(statistic.Length)));
                            }
                            
                        }

                        Report(assets, false, hashSet);
                        Report(readonlyAssets, true, hashSet);

                        hashSet.Add(myMetaFileAverage.Metric(StatisticsUtil.GetNextPowerOfTwo((long)metaSize), false));
                        hashSet.Add(myMetaFileAverage.Metric(StatisticsUtil.GetNextPowerOfTwo((long)readonlyMetaSize), true));
                        hashSet.Add(myFilesAverage.Metric(StatisticsUtil.GetNextPowerOfTwo((long)totalSize / 1024 / 1024)));
                        
                        tcs.TrySetResult(hashSet);
                    });

                    return true;
                }

                return false;
            });
            
            return tcs.Task;
        }

        private void Report(CountingSet<(FileType, ulong)> assets, bool isReadonly, HashSet<MetricEvent> hashSet)
        {
            var items = assets.GetItems().OrderBy(t => t.Item2);

            foreach (var item in items)
            {
                var count = StatisticsUtil.GetNextPowerOfTwo(assets.GetCount(item));
                hashSet.Add(
                    myAssetInformation.Metric(myAssetTypeField.With(item.Item1),
                    myReadonlyAssetField.With(isReadonly),
                    myBucketSizeField.With((long)item.Item2),
                    myBucketCountField.With(count)
                ));
            }
            
        }

        private static readonly ulong[] ourDistributions = new ulong[] {1, 5, 10, 20, 30, 40, 50, 75, 100, 500};
        private static readonly ulong ourMaxDefault = 1000;
        private ulong GetSize(ulong length)
        {
            var mb = length / 1024 / 1024;

            for (int i = 0; i < ourDistributions.Length; i++)
            {
                if (mb <= ourDistributions[i])
                    return ourDistributions[i];
            }

            return ourMaxDefault;
        }

        public struct AssetData
        {
            public readonly FileType FileType;
            public readonly ulong Length;
            public readonly bool IsUserEditable;

            public AssetData(FileType fileType, ulong length, bool isUserEditable)
            {
                FileType = fileType;
                Length = length;
                IsUserEditable = isUserEditable;
            }
        }
        
        public enum FileType
        {
            Asset,
            Prefab,
            Scene,
            AsmDef,
            Meta,

            KnownBinary,
            ExcludedByName
        }
        
        private struct Data
        {
            public readonly FileType FileType;
            public readonly ulong Length;
            public readonly bool IsUserEditable;

            public Data(FileType fileType, ulong length, bool isUserEditable)
            {
                FileType = fileType;
                Length = length;
                IsUserEditable = isUserEditable;
            }
        }

        public void FinishInitialUpdate()
        {
            InitialUpdateFinished.Value = true;
        }

        // called from MT
        private readonly SynchronizedList<Data> myStatistics = new();
        public void AddStatistic(FileType fileType, ulong externalFileLength, bool externalFileIsUserEditable)
        {
            if (IsReady.Value)
                return;
            
            myStatistics.Add(new Data(fileType, externalFileLength, externalFileIsUserEditable));
        }
    }
}