package com.nags.operations.data

import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicInteger
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class CacheCredentialValidationTest {
    @Test fun concurrentChecksVerifyTheSameCiphertextOnlyOnce() = runBlocking {
        val protector = TestProtector()
        val credentials = CacheCredentialValidation(protector)

        val owners = List(20) { async { credentials.subject("protected:original", "first-account") } }.awaitAll()

        assertEquals(List(20) { "first-account" }, owners)
        assertEquals(1, protector.reads.get())
    }

    @Test fun rotationAndSignOutDoNotReuseAnOlderValidation() = runBlocking {
        val protector = TestProtector()
        val credentials = CacheCredentialValidation(protector)
        assertEquals("first-account", credentials.subject("protected:original", "first-account"))
        assertEquals("first-account", credentials.subject("protected:refreshed", "first-account"))
        assertEquals("second-account", credentials.subject("protected:switched", "second-account"))
        assertEquals(3, protector.reads.get())

        assertNull(credentials.subject(null, "second-account"))
        assertNull(credentials.subject("protected:switched", null))
        credentials.clear()
        assertEquals("second-account", credentials.subject("protected:switched", "second-account"))
        assertEquals(4, protector.reads.get())
    }

    @Test fun expiredOrFailedValidationMustBeCheckedAgain() = runBlocking {
        val protector = TestProtector()
        var now = 0L
        val credentials = CacheCredentialValidation(protector) { now }
        assertEquals("account", credentials.subject("protected:token", "account"))

        protector.valid = false
        now = TimeUnit.SECONDS.toNanos(30)
        assertNull(credentials.subject("protected:token", "account"))
        protector.valid = true
        assertEquals("account", credentials.subject("protected:token", "account"))
        assertEquals(3, protector.reads.get())

        protector.valid = false
        credentials.invalidate("protected:token")
        assertNull(credentials.subject("protected:token", "account"))
        assertEquals(4, protector.reads.get())
    }

    @Test fun plaintextIsNeverMemoized() = runBlocking {
        val protector = TestProtector()
        val credentials = CacheCredentialValidation(protector)

        assertEquals("account", credentials.subject("legacy-token", "account"))
        assertEquals("account", credentials.subject("legacy-token", "account"))
        assertEquals(2, protector.reads.get())
    }

    private class TestProtector : TokenProtector {
        val reads = AtomicInteger()
        @Volatile var valid = true

        override fun protect(value: String) = "protected:$value"

        override fun unprotect(value: String): String {
            reads.incrementAndGet()
            check(valid)
            return value.removePrefix("protected:")
        }

        override fun isProtected(value: String) = value.startsWith("protected:")
    }
}
