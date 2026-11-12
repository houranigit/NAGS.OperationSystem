package com.nags.operations.ui.flights

/** A local Add/edit/Delete cannot reorder or replace a submitted record sharing the form. */
internal fun <T> editLocalWorkDrafts(current: List<T>, recordedIds: Set<String>, id: (T) -> String,
    edit: (List<T>) -> List<T>): List<T> {
    val next = edit(current.filterNot { id(it) in recordedIds }).filterNot { id(it) in recordedIds }.distinctBy(id)
    val remaining = next.associateBy(id).toMutableMap()
    return current.mapNotNull { item -> if (id(item) in recordedIds) item else remaining.remove(id(item)) } +
        next.filter { id(it) in remaining }
}
