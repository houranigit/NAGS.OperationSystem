package com.nags.operations.data.outbox

import android.content.Context
import androidx.work.CoroutineWorker
import androidx.work.WorkerParameters

/** Consumes persisted requests from older app versions without opening or sending their data. */
class PersistentOutboxDrainWorker(
    context: Context,
    parameters: WorkerParameters,
) : CoroutineWorker(context, parameters) {
    override suspend fun doWork(): Result = Result.success()
}
