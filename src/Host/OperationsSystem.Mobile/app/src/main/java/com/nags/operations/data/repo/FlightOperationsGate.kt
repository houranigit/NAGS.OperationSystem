package com.nags.operations.data.repo

import kotlin.coroutines.AbstractCoroutineContextElement
import kotlin.coroutines.CoroutineContext
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext

/** Section writes nested inside a combined Room transaction reuse its operations gate. */
class FlightOperationsGate {
    private val mutex = Mutex()
    private class Held(val gate: FlightOperationsGate) : AbstractCoroutineContextElement(Key) {
        companion object Key : CoroutineContext.Key<Held>
    }
    suspend fun <T> withLock(block: suspend () -> T): T {
        if (currentCoroutineContext()[Held]?.gate === this) return block()
        return mutex.withLock { withContext(Held(this)) { block() } }
    }
}

/** Acquire the operation gate before a repository lock, including cache/queue-only readers. */
internal class FlightOperationsLocalLock(private val operations: FlightOperationsGate) {
    private val mutex = Mutex()
    suspend fun <T> withLock(block: suspend () -> T): T = operations.withLock { mutex.withLock { block() } }
}
