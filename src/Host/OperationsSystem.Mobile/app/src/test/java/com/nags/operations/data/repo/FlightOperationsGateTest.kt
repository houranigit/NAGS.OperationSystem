package com.nags.operations.data.repo

import java.util.Collections
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeout
import org.junit.Assert.*
import org.junit.Test

class FlightOperationsGateTest {
    @Test fun nestedSectionWritesKeepTheGateAcrossTransactionContextChanges() = runBlocking {
        val gate = FlightOperationsGate()
        withTimeout(2_000) {
            gate.withLock {
                withContext(Dispatchers.Default) {
                    gate.withLock { withContext(Dispatchers.IO) { gate.withLock { assertTrue(true) } } }
                }
            }
        }
    }

    @Test fun standaloneQueueWriterWaitsBeforeTakingTheRepositoryLock() = runBlocking {
        val gate = FlightOperationsGate()
        val repositoryLock = FlightOperationsLocalLock(gate)
        val transactionStarted = CompletableDeferred<Unit>()
        val writerReady = CompletableDeferred<Unit>()
        val events = Collections.synchronizedList(mutableListOf<String>())
        // This writer is created outside the transaction context and cannot inherit its gate marker.
        val standalone = async(Dispatchers.Default) {
            transactionStarted.await()
            writerReady.complete(Unit)
            repositoryLock.withLock { events += "standalone" }
        }
        withTimeout(2_000) {
            gate.withLock {
                events += "transaction-start"
                transactionStarted.complete(Unit)
                writerReady.await()
                // A reversed lock order would let standalone hold repositoryLock and deadlock here.
                withContext(Dispatchers.IO) { repositoryLock.withLock { events += "nested-section" } }
                assertFalse(standalone.isCompleted)
                events += "transaction-end"
            }
            standalone.await()
        }
        assertEquals(listOf("transaction-start", "nested-section", "transaction-end", "standalone"), events)
    }

    @Test fun failedTransactionReleasesTheGateForTheNextSave() = runBlocking {
        val gate = FlightOperationsGate()
        try { gate.withLock { throw IllegalStateException("late validation") } }
        catch (_: IllegalStateException) { }
        withTimeout(2_000) { withContext(Dispatchers.Default) { gate.withLock { assertTrue(true) } } }
    }
}
