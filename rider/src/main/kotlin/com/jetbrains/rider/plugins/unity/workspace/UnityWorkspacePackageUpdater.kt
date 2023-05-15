@file:Suppress("UnstableApiUsage")

package com.jetbrains.rider.plugins.unity.workspace

import com.intellij.openapi.components.service
import com.intellij.openapi.diagnostic.Logger
import com.intellij.openapi.project.Project
import com.intellij.openapi.vfs.VfsUtil
import com.intellij.openapi.vfs.VirtualFile
import com.intellij.openapi.vfs.VirtualFilePrefixTreeFactory
import com.intellij.util.application
import com.intellij.workspaceModel.ide.WorkspaceModel
import com.intellij.workspaceModel.ide.getInstance
import com.intellij.workspaceModel.ide.toVirtualFileUrl
import com.intellij.workspaceModel.storage.MutableEntityStorage
import com.intellij.workspaceModel.storage.url.VirtualFileUrlManager
import com.jetbrains.rd.ide.model.Solution
import com.jetbrains.rd.platform.util.idea.LifetimedService
import com.jetbrains.rd.protocol.ProtocolExtListener
import com.jetbrains.rd.util.lifetime.Lifetime
import com.jetbrains.rd.util.reactive.AddRemove
import com.jetbrains.rd.util.reactive.adviseNotNull
import com.jetbrains.rd.util.reactive.adviseUntil
import com.jetbrains.rider.plugins.unity.model.frontendBackend.FrontendBackendModel
import com.jetbrains.rider.plugins.unity.model.frontendBackend.UnityPackage
import com.jetbrains.rider.plugins.unity.model.frontendBackend.UnityPackageSource
import com.jetbrains.rider.projectDir
import com.jetbrains.rider.projectView.workspace.RiderEntitySource
import com.jetbrains.rider.projectView.workspace.getOrCreateRiderModuleEntity
import java.nio.file.Paths

class UnityWorkspacePackageUpdater(private val project: Project) : LifetimedService() {

    companion object {
        private val logger = Logger.getInstance(UnityWorkspacePackageUpdater::class.java)
        fun getInstance(project: Project): UnityWorkspacePackageUpdater = project.service()
    }

    private var initialEntityStorage: MutableEntityStorage? = MutableEntityStorage.create()
    val sourceRootsTree = VirtualFilePrefixTreeFactory.createSet()

    class ProtocolListener : ProtocolExtListener<Solution, FrontendBackendModel> {
        override fun extensionCreated(lifetime: Lifetime, project: Project, parent: Solution, model: FrontendBackendModel) {

            // Subscribe to package changes. If we subscribe after the backend has loaded the initial list of packages,
            // this map will already be populated, and we'll be called for each item. If we subscribe before the list
            // is loaded, updateWorkspaceModel will cache the changes until the packagesUpdating flag is reset. At this
            // point, we sync the cached changes, and switch to updating the workspace model directly. We then expect
            // Unity to only add/remove a single package at a time.
            model.packages.adviseAddRemove(lifetime) { action, _, unityPackage ->
                when (action) {
                    AddRemove.Add -> getInstance(project).updateWorkspaceModel { entityStorage -> getInstance(project).addPackage(unityPackage, entityStorage) }
                    AddRemove.Remove -> getInstance(project).updateWorkspaceModel { entityStorage -> getInstance(project).removePackage(unityPackage, entityStorage) }
                }
            }

            // Wait for the property to become false. On the backend, it is initially null, set to true before initially
            // calculating the packages, and then set to false. Depending on when we subscribe, we might not see all
            // states, but as long as it's false, we know it's done the initial bulk update and isn't working right now,
            // so we can flush the cached changes from the initial update.
            model.packagesUpdating.adviseUntil(lifetime) { updating ->
                if (updating == false) {
                    getInstance(project).syncInitialEntityStorage()
                    return@adviseUntil true
                }
                return@adviseUntil false
            }

            model.packagesUpdating.adviseNotNull(lifetime) { updating ->
                if (!updating) {
                    getInstance(project).syncSourceRootsTree(project)
                }
            }
        }
    }

    private fun addPackage(unityPackage: UnityPackage, entityStorage: MutableEntityStorage) {
        logger.trace("Adding Unity package: ${unityPackage.id}")

        val packageFolder = unityPackage.packageFolderPath?.let { VfsUtil.findFile(Paths.get(it), true) }
        val contentRootEntity = if (packageFolder != null && unityPackage.source != UnityPackageSource.Unknown) {
            entityStorage.addContentRootEntity(
                packageFolder.toVirtualFileUrl(VirtualFileUrlManager.getInstance(project)),
                listOf(),
                UNITY_EXCLUDED_PATTERNS,
                entityStorage.getOrCreateRiderModuleEntity(),
                RiderUnityPackageEntitySource
            )
        } else null

        val entity = UnityPackageEntity(unityPackage, RiderUnityPackageEntitySource) {
            this.descriptor = unityPackage
            this.contentRootEntity = contentRootEntity
        }
        entityStorage.addEntity(entity)

        val mapping = entityStorage.getMutableExternalMapping<String>(UNITY_PACKAGE_ID_MAPPING)
        mapping.addMapping(entity, entity.packageId)
    }

    private fun removePackage(unityPackage: UnityPackage, builder: MutableEntityStorage) {
        logger.trace("Removing Unity package: ${unityPackage.id}")

        val mapping = builder.getExternalMapping<String>(UNITY_PACKAGE_ID_MAPPING)
        for (entity in mapping.getEntities(unityPackage.id)) {
            builder.removeEntity(entity)
        }
    }

    private fun updateWorkspaceModel(action: (MutableEntityStorage) -> Unit) {
        application.runWriteAction {
            val entityStorage = initialEntityStorage
            if (entityStorage != null) {
                action.invoke(entityStorage)
            } else {
                WorkspaceModel.getInstance(project).updateProjectModel("Unity: update packages", action)
            }
        }
    }

    private fun syncInitialEntityStorage() {
        val initialEntityStorage = initialEntityStorage ?: return
        logger.trace("Sync Unity packages after startup...")

        application.runWriteAction {
            this.initialEntityStorage = null
            WorkspaceModel.getInstance(project).updateProjectModel("Unity: sync packages") { entityStorage ->
                entityStorage.replaceBySource({ it is RiderUnityPackageEntitySource }, initialEntityStorage)
            }
        }
    }

    private fun syncSourceRootsTree(project: Project){
        val sourceRoots = getSourceRoots(project)
        for (root in sourceRoots)
            sourceRootsTree.add(root)
    }

    private fun getSourceRoots(project:Project): MutableSet<VirtualFile> {
        val roots = mutableSetOf<VirtualFile>()
        val assets = project.projectDir.findChild("Assets") ?: return roots
        roots.add(assets)
        val editablePackages = WorkspaceModel.getInstance(project).getPackages().filter { it.isEditable() }.mapNotNull { it.packageFolder }
        roots.addAll(editablePackages)
        return roots
    }

    object RiderUnityPackageEntitySource : RiderEntitySource
}