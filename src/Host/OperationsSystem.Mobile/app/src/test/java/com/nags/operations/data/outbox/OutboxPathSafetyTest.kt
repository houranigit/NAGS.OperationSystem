package com.nags.operations.data.outbox

import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class OutboxPathSafetyTest {
    @Test
    fun `canonical UUID is accepted as a directory name`() {
        val id = "123e4567-e89b-12d3-a456-426614174000"

        assertEquals(id, canonicalClientMutationId(id))
    }

    @Test
    fun `path traversal and non-canonical UUIDs are rejected`() {
        listOf(
            "..",
            "../datastore",
            "/data/user/0/com.nags.operations/files",
            "123E4567-E89B-12D3-A456-426614174000",
            "1-1-1-1-1",
        ).forEach { value ->
            assertThrows(IllegalArgumentException::class.java) {
                canonicalClientMutationId(value)
            }
        }
    }
}
