package com.jetbrains.rider.plugins.unity

import com.intellij.ide.projectView.ProjectViewNestingRulesProvider
import com.jetbrains.rider.projectView.nesting.RiderNestingRulesLanguageExtensions

class MetaFileNestingRulesProvider : ProjectViewNestingRulesProvider {
    override fun addFileNestingRules(consumer: ProjectViewNestingRulesProvider.Consumer) {
        val languageExtensions = RiderNestingRulesLanguageExtensions.EP_NAME.extensions.flatMap { it.getExtensions() }
        for (languageExtension in languageExtensions) {
            consumer.addNestingRule(".$languageExtension", ".$languageExtension.meta")
            consumer.addNestingRule(".txt", ".txt.meta")
        }
    }
}