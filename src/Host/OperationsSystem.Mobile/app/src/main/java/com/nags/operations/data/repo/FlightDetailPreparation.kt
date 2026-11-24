package com.nags.operations.data.repo

import com.nags.operations.data.ApiException
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.FlightDetailsBatch
import java.io.IOException
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Deferred
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope

/** Each batch shares one network request. Only metadata is read ahead while media remains serial. */
internal suspend fun <Stamp : Any> Iterable<String>.prepareFlightDetails(
    beforeRead: suspend (String) -> Stamp?,
    read: suspend (String) -> FlightDetail,
    readBatch: (suspend (List<String>) -> FlightDetailsBatch?)? = null,
    prepare: suspend (String, Stamp, Result<FlightDetail>) -> Unit,
) {
    if (readBatch == null) {
        prepareIndividualDetails(beforeRead, read, prepare)
        return
    }

    val batches = chunked(DETAIL_BATCH_SIZE)
    if (batches.isEmpty()) return
    coroutineScope {
        fun startBatch(ids: List<String>) = async {
            val stamps = ids.mapNotNull { id -> beforeRead(id)?.let { id to it } }
            if (stamps.isEmpty()) return@async emptyList()
            val requested = stamps.map { it.first }
            val batch = readBatch(requested) ?: return@async null
            val covered = batch.items.map { it.id } + batch.missingFlightIds
            if (covered.size != requested.size || covered.toSet() != requested.toSet())
                throw IOException("The flight details did not cover the requested batch.")
            val details = batch.items.associateBy { it.id }
            stamps.map { (id, stamp) ->
                val detail = details[id]
                val response = if (detail != null) Result.success(detail)
                else Result.failure(ApiException(404, "This flight is no longer available."))
                Triple(id, stamp, response)
            }
        }

        var pending = startBatch(batches.first())
        for ((index, _) in batches.withIndex()) {
            val downloaded = pending.await()
            if (downloaded == null) {
                batches.subList(index, batches.size).flatten().prepareIndividualDetails(beforeRead, read, prepare)
                return@coroutineScope
            }
            if (index < batches.lastIndex) pending = startBatch(batches[index + 1])
            for ((id, stamp, response) in downloaded) prepare(id, stamp, response)
        }
    }
}

private suspend fun <Stamp : Any> Iterable<String>.prepareIndividualDetails(
    beforeRead: suspend (String) -> Stamp?,
    read: suspend (String) -> FlightDetail,
    prepare: suspend (String, Stamp, Result<FlightDetail>) -> Unit,
) = coroutineScope {
    val remaining = iterator()
    val pending = ArrayDeque<Pair<String, Deferred<Pair<Stamp, Result<FlightDetail>>?>>>()
    fun fillWindow() {
        while (pending.size < DETAIL_READ_WINDOW && remaining.hasNext()) {
            val id = remaining.next()
            val download = async {
                val stamp = beforeRead(id) ?: return@async null
                val response = try { Result.success(read(id)) }
                catch (cancelled: CancellationException) { throw cancelled }
                catch (error: Exception) { Result.failure(error) }
                stamp to response
            }
            pending.addLast(id to download)
        }
    }

    fillWindow()
    while (pending.isNotEmpty()) {
        val (id, download) = pending.removeFirst()
        val fetched = download.await()
        fillWindow()
        if (fetched != null) prepare(id, fetched.first, fetched.second)
    }
}

private const val DETAIL_READ_WINDOW = 3
private const val DETAIL_BATCH_SIZE = 20
