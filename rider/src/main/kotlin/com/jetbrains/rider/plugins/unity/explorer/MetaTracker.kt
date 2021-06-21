@file:Suppress("UnstableApiUsage")

package com.jetbrains.rider.plugins.unity.explorer

import com.intellij.openapi.Disposable
import com.intellij.openapi.command.CommandProcessor
import com.intellij.openapi.command.impl.UndoManagerImpl
import com.intellij.openapi.command.undo.UndoManager
import com.intellij.openapi.project.Project
import com.intellij.openapi.vfs.LocalFileSystem
import com.intellij.openapi.vfs.VfsUtil
import com.intellij.openapi.vfs.VirtualFile
import com.intellij.openapi.vfs.VirtualFileManager
import com.intellij.openapi.vfs.newvfs.BulkFileListener
import com.intellij.openapi.vfs.newvfs.events.*
import com.intellij.util.PathUtil
import com.jetbrains.rd.platform.util.application
import com.jetbrains.rd.platform.util.getLogger
import com.jetbrains.rider.projectView.VfsBackendRequester
import com.microsoft.alm.helpers.Environment
import org.jetbrains.annotations.Nls
import java.nio.file.Path
import java.nio.file.Paths
import java.time.LocalDateTime
import java.time.ZoneOffset
import java.util.*
import kotlin.io.path.name

class MetaTracker(private val project: Project) : BulkFileListener, VfsBackendRequester, Disposable {

    companion object {
        private val logger = getLogger<MetaTracker>()
    }

    private var nextGroupIdIndex = 0

    init {
        project.messageBus.connect(this).subscribe(VirtualFileManager.VFS_CHANGES, this)
    }

    override fun after(events: MutableList<out VFileEvent>) {

        // Collect modified meta files at first (usually there is no such files, but still)
        val metaFiles = hashSetOf<Path>()
        for (event in events) {
            if (!translateEvent(event)) continue
            if (isMetaFile(event)) {
                metaFiles.add(Paths.get(event.path))
            }
        }

        // ... and then construct meta actions
        val actions = MetaActionList(metaFiles)
        for (event in events) {
            if (!translateEvent(event) || isMetaFile(event)) continue

            try {
                when (event) {
                    is VFileCreateEvent -> {
                        val metaFileName = getMetaFileName(event.childName)
                        val metaFile = event.parent.toNioPath().resolve(metaFileName)
                        actions.add(metaFile) {
                            createMetaFile(event.parent, metaFileName)
                        }
                    }
                    is VFileDeleteEvent -> {
                        val metaFile = getMetaFile(event.path) ?: continue
                        actions.add(metaFile) {
                            VfsUtil.findFile(metaFile, true)?.delete(this)
                        }
                    }
                    is VFileCopyEvent -> {
                        val metaFile = getMetaFile(event.file.path) ?: continue
                        actions.add(metaFile) {
                            createMetaFile(event.newParent, getMetaFileName(event.newChildName))
                        }
                    }
                    is VFileMoveEvent -> {
                        val metaFile = getMetaFile(event.oldPath) ?: continue
                        actions.add(metaFile) {
                            VfsUtil.findFile(metaFile, true)?.move(this, event.newParent)
                        }
                    }
                    is VFilePropertyChangeEvent -> {
                        if (!event.isRename) continue
                        val metaFile = getMetaFile(event.oldPath) ?: continue
                        actions.add(metaFile) {
                            val target = getMetaFileName(event.newValue as String)
                            val origin = VfsUtil.findFile(metaFile, true)
                            origin?.parent?.findChild(target)?.delete(this) // remove conflicting meta file if any
                            origin?.rename(this, target)
                        }
                    }
                }
            }
            catch (t: Throwable) {
                logger.error(t)
                continue
            }
        }

        if (actions.isEmpty()) return

        val commandProcessor = CommandProcessor.getInstance()
        var groupId = commandProcessor.currentCommandGroupId
        if (groupId == null) {
            groupId = MetaGroupId(nextGroupIdIndex++)
            commandProcessor.currentCommandGroupId = groupId
        }

        application.invokeLater {
            (UndoManagerImpl.getInstance(project) as UndoManagerImpl).allowMergeGlobalCommands {
                commandProcessor.executeCommand(project, {
                    actions.executeUnderWriteLock()
                }, actions.getCommandName(), groupId)
            }
        }
    }

    private fun translateEvent(event: VFileEvent): Boolean {
        if (event.isFromRefresh) return false
        if (UndoManager.getInstance(project).isUndoOrRedoInProgress) return false
        if (event.fileSystem !is LocalFileSystem) return false
        return CommandProcessor.getInstance().currentCommand != null
    }

    private fun isMetaFile(event: VFileEvent): Boolean {
        val extension = event.file?.extension ?: PathUtil.getFileExtension(event.path)
        return "meta".equals(extension, true)
    }

    private fun getMetaFile(path: String?): Path? {
        path ?: return null
        val file = Paths.get(path)
        val metaFileName = getMetaFileName(file.name)
        return file.parent.resolve(metaFileName)
    }

    private fun getMetaFileName(fileName: String) = "$fileName.meta"

    private fun createMetaFile(parent: VirtualFile, metaFileName: String) {
        val file = parent.createChildData(this, metaFileName)
        val guid = UUID.randomUUID().toString().replace("-", "").substring(0, 32)
        val timestamp = LocalDateTime.now(ZoneOffset.UTC).atZone(ZoneOffset.UTC).toEpochSecond() // LocalDateTime to epoch seconds
        val content = "fileFormatVersion: 2${Environment.NewLine}guid: ${guid}${Environment.NewLine}timeCreated: $timestamp"
        VfsUtil.saveText(file, content)
    }

    override fun dispose() = Unit

    private class MetaActionList(private val changedMetaFiles: HashSet<Path>) {

        private val actions = mutableListOf<MetaAction>()

        fun add(metaFile: Path, action: () -> Unit) {
            if (changedMetaFiles.contains(metaFile)) return
            actions.add(MetaAction(metaFile, action))
        }

        fun isEmpty() = actions.isEmpty()

        fun executeUnderWriteLock() {
            application.runWriteAction {
                actions.forEach { it.execute() }
            }
        }

        @Nls
        fun getCommandName(): String {
            return if (actions.count() == 1)
                "Process '${actions.single().metaFile.name}'"
            else
                "Process ${actions.count()} .meta Files"
        }
    }

    private class MetaAction(val metaFile: Path, private val action: () -> Unit) {
        fun execute() {
            try {
                action()
            }
            catch (ex: Throwable) {
                logger.error(ex)
            }
        }
    }

    private class MetaGroupId(val index: Int) {
        override fun toString() = "MetaGroupId$index"
    }
}