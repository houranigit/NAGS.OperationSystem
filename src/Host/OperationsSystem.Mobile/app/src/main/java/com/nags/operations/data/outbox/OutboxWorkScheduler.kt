package com.nags.operations.data.outbox

import android.content.Context
import android.util.Log
import androidx.work.BackoffPolicy
import androidx.work.Constraints
import androidx.work.CoroutineWorker
import androidx.work.ExistingWorkPolicy
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.WorkRequest
import androidx.work.WorkerParameters
import androidx.work.await
import com.nags.operations.AppGraph
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import java.util.concurrent.TimeUnit

/**
 * Persists an outbox wake-up in WorkManager so queued onsite work survives process death and
 * device reboot. The unique request is deliberately one-shot: every durable enqueue/session
 * resume schedules it, while retryable transport failures stay in WorkManager's backoff policy.
 */
class OutboxWorkScheduler(
    context: Context,
    private val workManager: WorkManager = WorkManager.getInstance(context.applicationContext),
) {
    private val lifecycleMutex = Mutex()
    private var enabled = false

    suspend fun scheduleAndAwait() {
        lifecycleMutex.withLock {
            if (!enabled) return
            enqueueRequest().await()
        }
    }

    private fun enqueueRequest() =
        workManager.enqueueUniqueWork(
            UNIQUE_WORK_NAME,
            // An enqueue that lands after a running worker's final queue snapshot must not be
            // lost. Append it behind healthy work; replace a failed/cancelled chain on resume.
            ExistingWorkPolicy.APPEND_OR_REPLACE,
            OneTimeWorkRequestBuilder<PersistentOutboxDrainWorker>()
                .setConstraints(
                    Constraints.Builder()
                        .setRequiredNetworkType(NetworkType.CONNECTED)
                        .build(),
                )
                .setBackoffCriteria(
                    BackoffPolicy.EXPONENTIAL,
                    WorkRequest.MIN_BACKOFF_MILLIS,
                    TimeUnit.MILLISECONDS,
                )
                .addTag(WORK_TAG)
                .build(),
        )

    /** Enables durable delivery and plants a wake-up for anything already queued. */
    suspend fun resumeAndSchedule() {
        lifecycleMutex.withLock {
            enabled = true
            enqueueRequest().await()
        }
    }

    /** Disables new wake-ups before cancelling any running/pending uploader during sign-out. */
    suspend fun pauseAndCancel() {
        lifecycleMutex.withLock {
            enabled = false
            workManager.cancelUniqueWork(UNIQUE_WORK_NAME).await()
        }
    }

    companion object {
        internal const val UNIQUE_WORK_NAME = "operations-work-order-outbox"
        internal const val WORK_TAG = "operations-outbox"
    }
}

/** WorkManager entry point. All submission logic remains in the process-singleton [OutboxWorker]. */
class PersistentOutboxDrainWorker(
    appContext: Context,
    workerParameters: WorkerParameters,
) : CoroutineWorker(appContext, workerParameters) {
    override suspend fun doWork(): Result {
        return try {
            val graph = AppGraph.get(applicationContext)
            graph.tokenStore.initializeSecureStorage()

            // Signing out intentionally retains same-user work, but must never submit it without
            // an authenticated owner. The next same-user sign-in schedules a fresh request.
            if (graph.tokenStore.getAccessToken().isNullOrBlank()) {
                return Result.success()
            }

            when (graph.outboxWorker.drainForBackground()) {
                PersistentDrainResult.Complete -> Result.success()
                PersistentDrainResult.Retry -> Result.retry()
            }
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            Log.w(TAG, "Persistent outbox drain failed unexpectedly", e)
            Result.retry()
        }
    }

    private companion object {
        const val TAG = "PersistentOutbox"
    }
}
