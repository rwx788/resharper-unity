using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using JetBrains.Application.Threading;
using JetBrains.Collections;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Plugins.Unity.Core.Psi.Modules;
using JetBrains.ReSharper.Plugins.Unity.UnityEditorIntegration;
using JetBrains.ReSharper.Plugins.Unity.UnityEditorIntegration.Packages;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Caches;
using JetBrains.Util;
using JetBrains.Util.Collections;
using JetBrains.Util.Extension;
using static JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.Caches.UnityProjectSettingsUtils;

namespace JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.Caches
{
    [SolutionComponent]
    public class UnityProjectSettingsCache : SimpleICache<ProjectSettingsCacheItem>
    {
        private readonly AssetSerializationMode myAssetSerializationMode;
        private readonly IEnumerable<IProjectSettingsAssetHandler> myProjectSettingsAssetHandlers;
        private readonly PackageManager myPackageManager;
        private readonly ProjectSettingsCacheItem myLocalCache = new();

        private readonly CountingSet<string> myShortNameAtBuildSettings = new();
        private readonly CountingSet<string> myDisabledShortNameAtBuildSettings = new();
        private readonly CountingSet<string> myShortNameAll = new();

        public UnityProjectSettingsCache(Lifetime lifetime,
                                         IShellLocks shellLocks,
                                         IPersistentIndexManager persistentIndexManager,
                                         AssetSerializationMode assetSerializationMode,
                                         IEnumerable<IProjectSettingsAssetHandler> projectSettingsAssetHandlers,
                                         PackageManager packageManager)
            : base(lifetime, shellLocks, persistentIndexManager, ProjectSettingsCacheItem.Marshaller)
        {
            myAssetSerializationMode = assetSerializationMode;
            myProjectSettingsAssetHandlers = projectSettingsAssetHandlers;
            myPackageManager = packageManager;

            myLocalCache.Tags.AddItems("Finish", "Player", "Respawn", "Untagged", "EditorOnly", "MainCamera", "GameController");
        }

        public bool IsAvailable() => myAssetSerializationMode.IsForceText;

        protected override bool IsApplicable(IPsiSourceFile sourceFile)
        {
            return IsAvailable() && sourceFile.PsiModule is UnityExternalFilesPsiModule;
        }

        public override object Build(IPsiSourceFile sourceFile, bool isStartup)
        {
            if (!IsApplicable(sourceFile))
                return null;

            var cacheItem = new ProjectSettingsCacheItem();
            if (sourceFile.IsScene())
                cacheItem.Scenes.SceneNames.Add(GetUnityPathFor(sourceFile, myPackageManager));

            foreach (var projectSettingsAssetHandler in myProjectSettingsAssetHandlers)
            {
                if (projectSettingsAssetHandler.IsApplicable(sourceFile))
                    projectSettingsAssetHandler.Build(sourceFile, cacheItem);
            }

            if (cacheItem.IsEmpty())
                return null;

            return cacheItem;
        }

        public override void Merge(IPsiSourceFile sourceFile, object builtPart)
        {
            RemoveFromLocalCache(sourceFile);
            AddToLocalCache(builtPart as ProjectSettingsCacheItem);
            base.Merge(sourceFile, builtPart);
        }

        public override void MergeLoaded(object data)
        {
            base.MergeLoaded(data);
            PopulateLocalCache();
        }

        public override void Drop(IPsiSourceFile sourceFile)
        {
            RemoveFromLocalCache(sourceFile);
            base.Drop(sourceFile);
        }


        private void PopulateLocalCache()
        {
            foreach (var (_, cacheItem) in Map)
                AddToLocalCache(cacheItem);
        }

        private void RemoveFromLocalCache(IPsiSourceFile sourceFile)
        {
            if (Map.TryGetValue(sourceFile, out var cacheItem))
            {
                RemoveScenes(cacheItem.Scenes);

                foreach (var name in cacheItem.Inputs)
                {
                    myLocalCache.Inputs.Remove(name);
                }

                foreach (var name in cacheItem.Layers)
                {
                    myLocalCache.Layers.Remove(name);
                }

                foreach (var name in cacheItem.Tags)
                {
                    myLocalCache.Tags.Remove(name);
                }
            }
        }

        private void RemoveScenes(ProjectSettingsCacheItem.ProjectSettingsSceneData sceneData)
        {
            foreach (var name in sceneData.SceneNamesFromBuildSettings)
            {
                myLocalCache.Scenes.SceneNamesFromBuildSettings.Remove(name);
                myShortNameAtBuildSettings.Remove(GetShortNameForSceneName(name));
            }

            foreach (var name in sceneData.DisabledSceneNamesFromBuildSettings)
            {
                myLocalCache.Scenes.DisabledSceneNamesFromBuildSettings.Remove(name);
                myDisabledShortNameAtBuildSettings.Remove(GetShortNameForSceneName(name));
            }

            foreach (var name in sceneData.SceneNames)
            {
                myLocalCache.Scenes.SceneNames.Remove(name);
                myShortNameAll.Remove(GetShortNameForSceneName(name));
            }
        }


        private void AddToLocalCache([CanBeNull] ProjectSettingsCacheItem cacheItem)
        {
            if (cacheItem == null)
                return;

            AddScenes(cacheItem.Scenes);

            foreach (var name in cacheItem.Layers)
            {
                myLocalCache.Layers.Add(name);
            }

            foreach (var name in cacheItem.Tags)
            {
                myLocalCache.Tags.Add(name);
            }

            foreach (var name in cacheItem.Inputs)
            {
                myLocalCache.Inputs.Add(name);
            }
        }

        private void AddScenes(ProjectSettingsCacheItem.ProjectSettingsSceneData sceneData)
        {
            foreach (var name in sceneData.SceneNamesFromBuildSettings)
            {
                myLocalCache.Scenes.SceneNamesFromBuildSettings.Add(name);
                myShortNameAtBuildSettings.Add(GetShortNameForSceneName(name));
            }

            foreach (var name in sceneData.DisabledSceneNamesFromBuildSettings)
            {
                myLocalCache.Scenes.DisabledSceneNamesFromBuildSettings.Add(name);
                myDisabledShortNameAtBuildSettings.Add(GetShortNameForSceneName(name));
            }

            foreach (var name in sceneData.SceneNames)
            {
                myLocalCache.Scenes.SceneNames.Add(name);
                myShortNameAll.Add(GetShortNameForSceneName(name));
            }
        }

        public IEnumerable<string> GetAllPossibleSceneNames()
        {
            var result = new HashSet<string>();
            foreach (var (value, count) in myShortNameAll)
            {
                if (count == 1)
                    result.Add(value);
            }

            foreach (var value in myLocalCache.Scenes.SceneNames)
            {
                result.Add(value);
            }

            return result;
        }

        public IEnumerable<string> GetAllTags()
        {
            foreach (var value in myLocalCache.Tags)
            {
                yield return value;
            }
        }

        public int SceneCount => myLocalCache.Scenes.SceneNamesFromBuildSettings.Count;

        public bool IsScenePresentedAtEditorBuildSettings(string sceneName, out bool ambiguousDefinition)
        {
            ambiguousDefinition = false;
            var shortCount = myShortNameAtBuildSettings.GetCount(sceneName);
            if (shortCount > 1)
            {
                ambiguousDefinition = true;
                return true;
            }

            if (shortCount == 1)
                return true;

            return myLocalCache.Scenes.SceneNamesFromBuildSettings.Contains(sceneName);
        }

        public bool IsSceneExists(string sceneName)
        {
            return myShortNameAll.Contains(sceneName) || myLocalCache.Scenes.SceneNames.Contains(sceneName);
        }

        public string GetShortNameForSceneName(string name)
        {
            return name.Split('/').Last().RemoveEnd(UnityFileExtensions.SceneFileExtensionWithDot,
                StringComparison.InvariantCultureIgnoreCase);
        }

        public bool IsSceneDisabledAtEditorBuildSettings(string sceneName)
        {
            return myDisabledShortNameAtBuildSettings.Contains(sceneName) ||
                   myLocalCache.Scenes.DisabledSceneNamesFromBuildSettings.Contains(sceneName);
        }

        public IEnumerable<string> GetScenesFromBuildSettings(bool onlyEnabled = true)
        {
            foreach (var scene in myLocalCache.Scenes.SceneNamesFromBuildSettings)
            {
                yield return scene;
            }

            if (!onlyEnabled)
            {
                foreach (var scene in myLocalCache.Scenes.DisabledSceneNamesFromBuildSettings)
                {
                    yield return scene;
                }
            }
        }

        public IEnumerable<string> GetAllLayers()
        {
            foreach (var value in myLocalCache.Layers)
            {
                yield return value;
            }
        }

        public IEnumerable<string> GetAllInput()
        {
            foreach (var value in myLocalCache.Inputs)
            {
                yield return value;
            }
        }

        public bool HasInput(string literal)
        {
            return myLocalCache.Inputs.Contains(literal);
        }

        public bool HasTag(string literal)
        {
            return myLocalCache.Tags.Contains(literal);
        }

        public bool HasLayer(string literal)
        {
            return myLocalCache.Layers.Contains(literal);
        }
    }
}