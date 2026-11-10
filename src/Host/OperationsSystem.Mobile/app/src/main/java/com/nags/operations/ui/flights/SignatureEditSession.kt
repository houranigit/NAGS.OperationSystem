package com.nags.operations.ui.flights

import kotlinx.serialization.Serializable

/** Dialog-local history. The form's original signature changes only after an explicit Use action. */
@Serializable
internal data class SignatureEditSession(
    val original: String,
    val viewingOriginal: Boolean,
    val draft: String = "",
    val history: List<SignatureEditSnapshot> = emptyList(),
) {
    val displayedInk: String get() = if (viewingOriginal) original else draft

    fun replace(): SignatureEditSession = change("", false)
    fun draw(value: String): SignatureEditSession = if (viewingOriginal) this else change(value, false)
    fun clear(): SignatureEditSession = if (viewingOriginal || draft.isBlank()) this else change("", false)
    fun undo(): SignatureEditSession {
        val previous = history.lastOrNull() ?: return this
        return copy(draft = previous.draft, viewingOriginal = previous.viewingOriginal, history = history.dropLast(1))
    }

    private fun change(value: String, preview: Boolean): SignatureEditSession {
        var retained = (history + SignatureEditSnapshot(draft, viewingOriginal)).takeLast(32)
        // Avoid duplicating long drawing snapshots into Android's saved-state Bundle.
        while (retained.sumOf { it.draft.length } > 64_000) retained = retained.drop(1)
        return copy(draft = value, viewingOriginal = preview, history = retained)
    }
}

@Serializable
internal data class SignatureEditSnapshot(val draft: String, val viewingOriginal: Boolean)

/** Bound decoded image allocation independently of the PNG's compressed byte count. */
internal fun signaturePreviewSampleSize(width: Int, height: Int): Int {
    var sample = 1
    while ((width.toLong() + sample - 1) / sample > 1024 || (height.toLong() + sample - 1) / sample > 1024) sample *= 2
    return sample
}
