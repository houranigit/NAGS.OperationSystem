package com.nags.operations.notifications

import android.content.Context
import com.nags.operations.AppGraph
import kotlinx.coroutines.FlowPreview
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.debounce
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.flowOf

internal const val SYNC_ISSUE_NOTIFICATION_KIND = "FlightSyncIssue"

internal data class SyncIssueRecord(val owner: String, val id: String, val status: String, val hasError: Boolean) {
    val needsAttention: Boolean get() = status == "failed" || status == "conflict" || (status == "pending" && hasError)
    val fingerprint: String get() = "$id:$status:$hasError"
}

/** Includes failed retries that remain pending; ordinary offline saves do not alert. */
internal data class SyncIssueSnapshot(val owner: String, val records: List<SyncIssueRecord>)

/** Start every queue read for the new owner before publishing a ready snapshot. */
@OptIn(ExperimentalCoroutinesApi::class)
internal fun observeSyncIssues(graph: AppGraph, owners: Flow<String?>): Flow<SyncIssueSnapshot?> =
    owners.distinctUntilChanged().flatMapLatest { owner ->
        if (owner == null) flowOf(null)
        else observeAccountSyncIssues(graph).map { SyncIssueSnapshot(owner, it) }
    }

@OptIn(FlowPreview::class)
private fun observeAccountSyncIssues(graph: AppGraph): Flow<List<SyncIssueRecord>> = combine(listOf(
    graph.flightArrivals.changes.map { rows -> rows.map {
        SyncIssueRecord(it.ownerSubject, "arrival:${it.flightId}:${it.changeId}", it.status, it.error != null)
    } },
    graph.flightDepartures.changes.map { rows -> rows.map {
        SyncIssueRecord(it.ownerSubject, "departure:${it.flightId}:${it.changeId}", it.status, it.error != null)
    } },
    graph.flightCancellations.changes.map { rows -> rows.map {
        SyncIssueRecord(it.ownerSubject, "cancellation:${it.flightId}:${it.changeId}", it.status, it.error != null)
    } },
    graph.flightServices.changes.map { rows -> rows.map {
        SyncIssueRecord(it.ownerSubject, "service:${it.flightId}:${it.serviceRecordId}:${it.changeId}", it.status, it.error != null)
    } },
    graph.flightTasks.changes.map { rows -> rows.map {
        SyncIssueRecord(it.ownerSubject, "task:${it.flightId}:${it.taskRecordId}:${it.changeId}", it.status, it.error != null)
    } },
    graph.flightReturnsToRamp.changes.map { rows -> rows.map {
        SyncIssueRecord(it.ownerSubject, "rtr:${it.flightId}:${it.occurrenceId}:${it.changeId}", it.status, it.error != null)
    } },
    graph.adHocFlights.pending.map { rows -> rows.map {
        SyncIssueRecord(it.ownerSubject, "adhoc:${it.flightId}:${it.savedAtUtc}", it.status, it.error != null)
    } },
)) { snapshots -> snapshots.flatMap { it }.filter { it.needsAttention } }
    .debounce(750).distinctUntilChanged()

/** Remember the current trouble set per API/account so refresh and process restart do not repeat alerts. */
internal class SyncIssueAlertTracker(
    private val read: (String) -> Set<String>,
    private val write: (String, Set<String>) -> Unit,
) {
    constructor(context: Context) : this(
        read = { owner -> context.getSharedPreferences("sync_issue_alerts", Context.MODE_PRIVATE)
            .getStringSet(owner, emptySet()).orEmpty().toSet() },
        write = { owner, values -> context.getSharedPreferences("sync_issue_alerts", Context.MODE_PRIVATE)
            .edit().putStringSet(owner, values).apply() },
    )

    fun update(owner: String, records: List<SyncIssueRecord>, enabled: Boolean): Boolean {
        val current = records.filter { it.owner == owner && it.needsAttention }.map { it.fingerprint }.toSet()
        val previous = read(owner)
        write(owner, current)
        return enabled && (current - previous).isNotEmpty()
    }
}
