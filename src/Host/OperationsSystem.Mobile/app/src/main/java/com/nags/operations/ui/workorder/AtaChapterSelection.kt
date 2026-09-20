package com.nags.operations.ui.workorder

import com.nags.operations.data.db.entities.AtaChapterEntity

/** Keep recorded labels unchanged when administrators rename or disable a chapter or category. */
internal fun TaskFormRow.ataChapterDisplayLabel(activeChapters: List<AtaChapterEntity>): String {
    if (ataChapterId == null) return ""
    val recordedLabel = listOfNotNull(ataChapterCode, ataChapterTitle).joinToString(". ")
    return recordedLabel.ifBlank {
        activeChapters.firstOrNull { it.id == ataChapterId }?.displayLabel ?: "Saved ATA chapter"
    }
}
