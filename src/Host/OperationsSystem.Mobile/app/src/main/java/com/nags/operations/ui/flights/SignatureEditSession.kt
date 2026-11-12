package com.nags.operations.ui.flights

import kotlinx.serialization.Serializable

/** Dialog-local history. The form's original signature changes only after an explicit Save action. */
@Serializable
internal data class SignatureEditSession(
    val original: String,
    val viewingOriginal: Boolean,
    val draft: String = "",
    val history: List<SignatureEditSnapshot> = emptyList(),
    val redoHistory: List<SignatureEditSnapshot> = emptyList(),
) {
    val displayedInk: String get() = if (viewingOriginal) original else draft
    val hasChanges: Boolean get() = !viewingOriginal && draft != original

    fun replace(): SignatureEditSession = change("", false)
    fun draw(value: String): SignatureEditSession = if (viewingOriginal) this else change(value, false)
    fun clear(): SignatureEditSession = if (viewingOriginal || draft.isBlank()) this else change("", false)
    fun undo(): SignatureEditSession {
        val previous = history.lastOrNull() ?: return this
        val (retained, redo) = boundedHistory(history.dropLast(1), redoHistory + snapshot(), preferUndo = false)
        return copy(draft = previous.draft, viewingOriginal = previous.viewingOriginal, history = retained, redoHistory = redo)
    }

    fun redo(): SignatureEditSession {
        val next = redoHistory.lastOrNull() ?: return this
        val (retained, redo) = boundedHistory(history + snapshot(), redoHistory.dropLast(1), preferUndo = true)
        return copy(draft = next.draft, viewingOriginal = next.viewingOriginal, history = retained, redoHistory = redo)
    }

    private fun change(value: String, preview: Boolean): SignatureEditSession {
        if (value == draft && preview == viewingOriginal) return this
        val (retained, _) = boundedHistory(history + snapshot(), emptyList(), preferUndo = true)
        return copy(draft = value, viewingOriginal = preview, history = retained, redoHistory = emptyList())
    }

    private fun snapshot() = SignatureEditSnapshot(draft, viewingOriginal)

    private fun boundedHistory(
        undo: List<SignatureEditSnapshot>,
        redo: List<SignatureEditSnapshot>,
        preferUndo: Boolean,
    ): Pair<List<SignatureEditSnapshot>, List<SignatureEditSnapshot>> {
        var retainedUndo = undo
        var retainedRedo = redo
        // Both directions share the existing Bundle budget; retain the nearest reversible step.
        while (retainedUndo.size + retainedRedo.size > 32 ||
            retainedUndo.sumOf { it.draft.length.toLong() } + retainedRedo.sumOf { it.draft.length.toLong() } > 64_000L) {
            if (retainedRedo.isNotEmpty() && (preferUndo || retainedUndo.isEmpty())) retainedRedo = retainedRedo.drop(1)
            else retainedUndo = retainedUndo.drop(1)
        }
        return retainedUndo to retainedRedo
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
