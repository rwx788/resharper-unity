package com.jetbrains.rider.plugins.unity.ideaInterop.fileTypes.asmref.jsonSchema

import com.intellij.openapi.project.Project
import com.intellij.openapi.vfs.VirtualFile
import com.jetbrains.jsonSchema.extension.JsonSchemaFileProvider
import com.jetbrains.jsonSchema.extension.JsonSchemaProviderFactory
import com.jetbrains.jsonSchema.extension.SchemaType
import com.jetbrains.rider.plugins.unity.ideaInterop.fileTypes.asmref.AsmRefFileType

class AsmRefJsonSchemaProviderFactory : JsonSchemaProviderFactory {
    override fun getProviders(project: Project): MutableList<JsonSchemaFileProvider> {
        return mutableListOf(
            object : JsonSchemaFileProvider {
                private val schemaFile = JsonSchemaProviderFactory.getResourceFile(this::class.java, "/schemas/unity/asmref.json")
                override fun isAvailable(file: VirtualFile) = file.fileType == AsmRefFileType
                override fun getName() = "Unity Assembly Definition Reference"
                override fun getSchemaFile() = schemaFile
                override fun getSchemaType() = SchemaType.embeddedSchema
                override fun getRemoteSource() = "https://json.schemastore.org/asmref.json"
            })
    }
}
